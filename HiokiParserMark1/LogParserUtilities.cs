// <copyright file="LogParserUtilities.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace HiokiParserMark1;

using Microsoft.Data.SqlClient;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Parses Hioki 1220-50 output files (group and step), and saves it to a remote database
/// The 'barcode' is harvested directly from the file and may not correspond to the actual barcode.
/// </summary>
public partial class LogParserUtilities // must be marked partial to allow compile-time compilation of regex
{
    private static readonly Regex ValueUnitRegex = ValueUnitSeparator(); // matches scientific notation with an optional unit
    private static readonly ConcurrentDictionary<string, byte> ResultTypeCache = new (); // the cache used to store result types with their respective indices
    private static readonly ConcurrentDictionary<string, byte> TestModeCache = new (); // the cache used to store test modes with their respective indices

    /// <summary>
    /// A DTO that abstracts the four fields common between group files and step files to reduce the arguments passed through.
    /// </summary>
    public record CommonPackage
    {
        /// <summary>
        /// Gets or sets the base product ID.
        /// </summary>
        public string Barcode { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the timestamp of when this product was tested.
        /// </summary>
        public DateTime TestTime { get; set; }

        /// <summary>
        /// Gets or setsthe number of times this product has been tested.
        /// </summary>
        public int TimesTested { get; set; }

        /// <summary>
        /// Gets or sets the group number for this product (ignored for group file parsing).
        /// </summary>
        public int? Group { get; set; }
    }

    /// <summary>
    /// Abstracts the three objects required for parsing plus one for the CommonPackage.
    /// </summary>
    public record ParsingContext(StreamReader reader, SqlConnection conn, SqlTransaction trans, CommonPackage data) // apparently you can put the constructor in the class definition
    {
        /// <summary>
        /// Gets the reader that scans through the file.
        /// </summary>
        public StreamReader Reader { get; } = reader;

        /// <summary>
        /// Gets the connection to the database (reused within batch).
        /// </summary>
        public SqlConnection Connection { get; } = conn;

        /// <summary>
        /// Gets the current DB transaction (reused within batch).
        /// </summary>
        public SqlTransaction Transaction { get; } = trans;

        /// <summary>
        /// Gets the <see cref="CommonPackage"/>  associated with the current file.
        /// </summary>
        public CommonPackage Data { get; } = data;
    }

    /// <summary>
    /// Initializes caches for ResultType and TestMode to reduce DB queries.
    /// </summary>
    /// <param name="connection">The DB connection used to retrieve result types and test modes.</param>
    /// <returns>A Task representing that the caches are ready for use.</returns>
    public static async Task InitializeCaches(SqlConnection connection)
    {
        // Fill ResultTypeCache
        using (SqlCommand cmd = new ("SELECT id, resultType FROM pe3coop.dbo.ResultTypes", connection))
        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                ResultTypeCache.TryAdd(reader.GetString(1).Trim(), reader.GetByte(0));
            }
        }

        // Fill TestModeCache
        using (SqlCommand cmd = new ("SELECT id, testMode FROM pe3coop.dbo.TestModes", connection))
        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                TestModeCache.TryAdd(reader.GetString(1).Trim(), reader.GetByte(0));
            }
        }
    }

    /// <summary>
    /// Checks the cache to see if a certain result type or mode is already in the DB.
    /// If it isn't, this method creates a new row for it in the DB and adds it to the cache to keep it current.
    /// </summary>
    /// <param name="toCheck">A string representing the result or mode for which to check.</param>
    /// <param name="isResultType">Whether the string to check is a result type (or test mode).</param>
    /// <param name="context">For harvesting connection and transaction.</param>
    /// <returns>The id of the type or mode, either existing or new.</returns>
    public static async Task<byte> GetCachedId(string toCheck, bool isResultType, ParsingContext context)
    {
        if (string.IsNullOrWhiteSpace(toCheck))
        {
            return 0;
        }

        ConcurrentDictionary<string, byte> cache = isResultType ? ResultTypeCache : TestModeCache;

        // First, check the cache
        if (cache.TryGetValue(toCheck, out byte existingId))
        {
            return existingId;
        }

        // If it's not there, insert a new one
        string tableName = isResultType ? "pe3coop.dbo.ResultTypes" : "pe3coop.dbo.TestModes";
        string columnName = isResultType ? "resultType" : "testMode";

        string insertSql = $@"
        SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
        BEGIN TRAN
            IF NOT EXISTS (SELECT 1 FROM {tableName} WHERE {columnName} = @val)
            BEGIN
                INSERT INTO {tableName} ({columnName}) VALUES (@val);
            END
            SELECT id FROM {tableName} WHERE {columnName} = @val;
        COMMIT TRAN";

        using SqlCommand command = new (insertSql, context.Connection);
        command.Transaction = context.Transaction;
        command.Parameters.AddWithValue("@val", toCheck);

        var result = await command.ExecuteScalarAsync();
        if (result == null || result == DBNull.Value)
        {
            return 0;
        }

        // Then update cache so we don't hit the DB for this string again
        byte newId = Convert.ToByte(result);
        cache.TryAdd(toCheck, newId);
        return newId;
    }

    /// <summary>
    /// Converts an input string representing a hexadecimal value to a double (in decimal).
    /// </summary>
    /// <param name="input">The string to parse for a hex value.</param>
    /// <returns>The parsed input string as a double.</returns>
    public static double HexOrSciToDouble(string input)
    {
        if (int.TryParse(input, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex))
        {
            return hex;
        }
        else if (double.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out double sciNotation))
        {
            return sciNotation;
        }

        return -1;
    }

    /// <summary>
    /// Parses input string for float and unit.
    /// Returns nullable tuple to convert later to DBNull. Avoiding the use of the "object" keyword here helps the garbage collector.
    /// </summary>
    /// <param name="input"> The string representation of the float to be cleaned. </param>
    /// <returns> An tuple of the parsed float and unit. </returns>
    public static (double? Value, string? Unit) CleanFloatValue(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return (null, null);
        }

        Match match = ValueUnitRegex.Match(input); // matches to value-unit pattern

        // If the pattern matches, proceed
        if (match.Success)
        {
            // Harvest from the capturing groups
            string valPart = match.Groups["value"].Value;
            string unitPart = match.Groups["unit"].Value.Trim(); // if not present, this is the empty string

            // Attempt to parse the value as a double
            if (double.TryParse(valPart, NumberStyles.Any, CultureInfo.InvariantCulture, out double result))
            {
                // If it works, append the unit (if it exists)
                return (result, string.IsNullOrEmpty(unitPart) ? null : unitPart);
            }
        }

        // Otherwise, exit immediately. The unit is irrelevant without a value.
        return (null, null);
    }

    [GeneratedRegex(@"^\s*(?<value>[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)\s*(?<unit>[^,]*?)\s*$", RegexOptions.Compiled)]
    private static partial Regex ValueUnitSeparator(); // this generates at compile-time, which was suggested by Intellisense
}
