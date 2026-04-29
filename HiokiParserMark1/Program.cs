// <copyright file="Program.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace HiokiParserMark1;

using Microsoft.Data.SqlClient;
using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Parses Hioki 1220-50 output files (group and step), and saves it to a remote database
/// The 'barcode' is harvested directly from the file and may not correspond to the actual barcode.
/// </summary>
public partial class Program // must be marked partial to allow compile-time compilation of regex
{
    private static readonly Regex ValueUnitRegex = MyRegex(); // matches scientific notation with an optional unit
    private static ConcurrentDictionary<string, byte> resultTypeCache = new (); // the cache used to store result types with their respective indices
    private static ConcurrentDictionary<string, byte> testModeCache = new (); // the cache used to store test modes with their respective indices

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
    /// Entry point for the program. Delegates to <see cref="ExecuteAsync"/>, then shows the end-of-run message.
    /// </summary>
    /// <param name="args">The directory to search (must only contain files of the correct filetype and format).</param>
    /// <returns>A Task representing that the batch is finished.</returns>
    public static async Task Main(string[] args)
    {
        try
        {
            await ExecuteAsync(args);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error: {e.Message}");
        }
        finally
        {
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }

    /// <summary>
    /// Router to <see cref="ParseHioki"/> to handle single-file/batch operations.
    /// </summary>
    /// <param name="args">The command-line arguments, should hold just a filepath, or nothing.</param>
    /// <returns>A Task representing completion/termination.</returns>
    private static async Task ExecuteAsync(string[] args)
    {
        string path;
        if (args.Length == 0)
        {
            Console.WriteLine("Please enter the path of the file or folder to parse for Hioki logs: ");
            path = Console.ReadLine() ?? string.Empty;
        }
        else
        {
            path = args[0];
        }

        // Console.ReadLine natively handles spaces, but if the user added them anyway, trim them
        // The Unicode characters 200E and 200F appear when a user uses drag-drop, which is supported by most terminals
        path = path.Trim().Trim('"', '\u200E', '\u200F');
        if (!string.IsNullOrWhiteSpace(path))
        {
            path = Path.GetFullPath(path);
        }

        Console.WriteLine(path);

        bool isFolder;
        if (Directory.Exists(path))
        {
            isFolder = true;
        }
        else if (File.Exists(path))
        {
            isFolder = false;
        }
        else
        {
            Console.WriteLine($"The file/folder you specified ({path}) could not be found. Please check your spelling and try again. The path may be relative to this program or absolute.");
            return;
        }

        Console.Write("Connecting...");
        using SqlConnection conn = new (GetConnectionString());
        await conn.OpenAsync();
        Console.WriteLine("Connected!");
        await InitializeCaches(conn);
        Console.Write("Parsing...");
        if (isFolder)
        {
            string[] files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                await ParseHioki(file, conn);
            }

            Console.WriteLine($"Complete! {files.Length} files added to database");
        }
        else
        {
            await ParseHioki(path, conn);
            Console.WriteLine("Complete!");
        }
    }

    /// <summary>
    /// Initializes caches for ResultType and TestMode to reduce DB queries.
    /// </summary>
    /// <returns>A Task representing that the caches are ready for use.</returns>
    private static async Task InitializeCaches(SqlConnection connection)
    {
        // Fill ResultTypeCache
        using (SqlCommand cmd = new ("SELECT id, resultType FROM pe3coop.dbo.ResultTypes", connection))
        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                resultTypeCache.TryAdd(reader.GetString(1).Trim(), reader.GetByte(0));
            }
        }

        // Fill TestModeCache
        using (SqlCommand cmd = new ("SELECT id, testMode FROM pe3coop.dbo.TestModes", connection))
        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                testModeCache.TryAdd(reader.GetString(1).Trim(), reader.GetByte(0));
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
    private static async Task<byte> GetCachedId(string toCheck, bool isResultType, ParsingContext context)
    {
        if (string.IsNullOrWhiteSpace(toCheck))
        {
            return 0;
        }

        ConcurrentDictionary<string, byte> cache = isResultType ? resultTypeCache : testModeCache;

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
    private static double HexOrSciToDouble(string input)
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
    private static (double? Value, string? Unit) CleanFloatValue(string input)
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

    /// <summary>
    /// Gets the barcode, test datetime, and number of times tested from the header common between step and group result files,
    /// then passes the context to the appropriate handler for the rest of the file to parse one entire Hioki log file and add it to the DB.
    /// </summary>
    /// <param name="file"> the file to parse. </param>
    /// <param name="conn">The <see cref="SqlConnection"/> to use for this file.</param>
    /// <returns>A Task representing that the file has been parsed.</returns>
    private static async Task ParseHioki(string file, SqlConnection conn)
    {
        try
        {
            // reader closes when ParseHioki() returns (at the end of the run)
            using StreamReader reader = new (file);

            // new() is a really cool constructor that uses the implied class from the declaration (can pass arguments just the same)
            CommonPackage package = new (); // in this case, package already knows it will be a CommonPackage from the left hand side, so new() can figure it out
            reader.ReadLine(); // cut "[Test Results]"
            reader.ReadLine(); // cut "File: filename"

            // Parse timesTested as an int
            string? line = reader.ReadLine();
            package.TimesTested = int.TryParse(line?.Split(',')[1], out int tt) ? tt : 0;

            // if TimesTested is 0, the parse was null, so say which file, and skip it (timesTested is primary key)
            if (package.TimesTested == 0)
            {
                Console.Error.WriteLine($"Error reading timesTested for {file}");
                return;
            }

            reader.ReadLine(); // cut "Lot No."

            // Parse barcode
            line = reader.ReadLine();
            package.Barcode = line?.Split(',')[1].Trim() ?? "UNKNOWN";

            // If Barcode is null, say which file, and skip it (barcode is primary key)
            if (line == "UNKNOWN")
            {
                Console.Error.WriteLine($"Error reading barcode for {file}");
                return;
            }

            // Parse testTime
            line = reader.ReadLine();
            string[]? dateParts = line?.Split(',');

            if (dateParts != null && dateParts.Length >= 3)
            {
                // concatenate date & time
                string fullDtStr = dateParts[1].Trim() + " " + dateParts[2].Trim();

                if (DateTime.TryParse(fullDtStr, out DateTime dt))
                {
                    package.TestTime = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second);
                }
                else
                {
                    Console.Error.WriteLine($"Error: Could not parse date string '{fullDtStr}' in {file}");
                    return;
                }
            }
            else
            {
                Console.Error.WriteLine($"Error: Date/Time line malformed in {file}");
                return;
            }

            using SqlTransaction transaction = conn.BeginTransaction(); // Create the transaction to be used for this file
            try
            {
                ParsingContext context = new (reader, conn, transaction, package); // Compile everything the parser needs to know into a context object
                line = await reader.ReadLineAsync(); // This will tell us whether we're dealing with group or step section
                while (line != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        line = await reader.ReadLineAsync();
                        continue;
                    }

                    if (line.Contains("-----  Group  -----"))
                    {
                        await reader.ReadLineAsync(); // Cut the column name row
                        await ParseGroupFile(context);
                    }
                    else if (line.Contains("-----  Component  -----"))
                    {
                        await reader.ReadLineAsync(); // Cut the column name row
                        string? groupLine = await reader.ReadLineAsync(); // Get the line containing the group number
                        string[]? groupParts = groupLine?.Split(',');
                        context.Data.Group = (groupParts?.Length > 1 && int.TryParse(groupParts[1].Trim(), out int g)) ? g : 0;
                        await ParseStepFile(context);
                    }

                    line = await reader.ReadLineAsync();
                }

                transaction.Commit();
            }
            catch (Exception)
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (UnauthorizedAccessException)
        {
            Console.WriteLine("Error: You do not have permission to read this file: " + file + "\n)");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"I/O Error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Helper function for ParseHioki (handles group files). Picks up at the beginning of the group-specific content and creates a new entry in the database for every entry in the file.
    /// </summary>
    /// <param name="context"> the context required to parse a group file. </param>
    /// <returns>A Task representing that the group file has been parsed.</returns>
    private static async Task ParseGroupFile(ParsingContext context)
    {
        string sql = @"INSERT INTO pe3coop.dbo.GroupResults (barcode, testTime, groupNumber, timesTested, allResult,
                       componentTest, shortTest, openTest, icTest, macroTest, functionTest)
                       VALUES (@barcode, @testTime, @groupNum, @timesTested, @result,
                       @comp, @short, @open, @ic, @macro, @function)"; // Reflects order in DB
        string[] paramNames = ["@result", "@comp", "@short", "@open", "@ic", "@macro", "@function"]; // to map the line values to their parameters in SQL, reflects order in CSV

        string? raw;
        while ((raw = await context.Reader.ReadLineAsync()) != null)
        {
            if (raw != null && raw.Contains("[EOT]"))
            {
                return; // If we find EOT, that means the group section is complete
            }

            string[]? split = raw?.Split(",");
            if (split != null)
            {
                // The reference ID of all result columns in group table
                int[] ids =
                [
                    await GetCachedId(split[0].Trim(), true, context),
                    await GetCachedId(split[2].Trim(), true, context),
                    await GetCachedId(split[3].Trim(), true, context),
                    await GetCachedId(split[4].Trim(), true, context),
                    await GetCachedId(split[5].Trim(), true, context),
                    await GetCachedId(split[6].Trim(), true, context),
                    await GetCachedId(split[7].Trim(), true, context)
                ];

                using SqlCommand command = new (sql, context.Connection);
                command.Transaction = context.Transaction;

                // Load the common parameters manually
                command.Parameters.AddWithValue("@barcode", context.Data.Barcode);
                command.Parameters.AddWithValue("@testTime", context.Data.TestTime);
                command.Parameters.AddWithValue("@groupNum", split[1].Trim());
                command.Parameters.AddWithValue("@timesTested", context.Data.TimesTested);

                // Then loop through the reference parameters
                for (int i = 0; i < ids.Length; i++)
                {
                    command.Parameters.AddWithValue(paramNames[i], SqlDbType.TinyInt).Value = (byte)ids[i];
                }

                await command.ExecuteNonQueryAsync();
            }
        }
    }

    /// <summary>
    /// Helper function for ParseHioki (handles step files). Picks up at the beginning of the step-specific content and creates a new entry in the database for every entry in the file.
    /// </summary>
    /// <param name="context"> the context required to parse a step file. </param>
    /// <returns>A Task representing that the step file has been parsed.</returns>
    private static async Task ParseStepFile(ParsingContext context)
    {
        // Create a DataTable to hold the data in memory
        DataTable table = new ();
        table.Columns.Add("barcode", typeof(string));
        table.Columns.Add("testTime", typeof(DateTime));
        table.Columns.Add("groupNum", typeof(int));
        table.Columns.Add("stepNum", typeof(int));
        table.Columns.Add("timesTested", typeof(int));
        table.Columns.Add("allResult", typeof(byte)); // Maps to tinyint
        table.Columns.Add("partName", typeof(string));
        table.Columns.Add("hPin", typeof(string));
        table.Columns.Add("lPin", typeof(string));
        table.Columns.Add("pos", typeof(string));
        table.Columns.Add("mode", typeof(byte)); // Maps to tinyint
        table.Columns.Add("rangeNum", typeof(int));
        table.Columns.Add("hLim", typeof(double));
        table.Columns.Add("lLim", typeof(double));
        table.Columns.Add("measurementUnit", typeof(char));
        table.Columns.Add("act", typeof(double));
        table.Columns.Add("ref", typeof(double));
        table.Columns.Add("meas", typeof(double));

        string? raw;
        while ((raw = await context.Reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue; // If the line is empty, skip it
            }

            if (raw.Contains("[EOT]"))
            {
                break; // If we see EOT, the step results are finished
            }

            string[] line = raw.Split(',');
            if (line[0].StartsWith("Gr"))
            {
                context.Data.Group = int.TryParse(line[1].Trim(), out int tt) ? tt : 0;
                continue;
            }

            if (line[0].Contains("-----  FCT  -----"))
            {
                await ParseFctSection(context);
                break;
            }

            if (line.Length < 13)
            {
                continue;
            }

            // Get result ID from cache
            byte resultId = await GetCachedId(line[0].Trim(), true, context);
            byte modeId = await GetCachedId(line[6].Trim(), false, context);

            // The discard operator "_" says to ignore the unit (because we already have it)
            (double? hLim, string? unit) = CleanFloatValue(line[8]);
            (double? lLim, string? _) = CleanFloatValue(line[9]);
            (double? act, string? _) = CleanFloatValue(line[10]);
            (double? refVal, string? _) = CleanFloatValue(line[11]);
            (double? meas, string? _) = CleanFloatValue(line[12]);

            // Add a row to the DataTable
            DataRow row = table.NewRow();
            row["barcode"] = context.Data.Barcode;
            row["testTime"] = context.Data.TestTime;
            row["groupNum"] = context.Data.Group;
            row["stepNum"] = int.Parse(line[1].Trim());
            row["timesTested"] = context.Data.TimesTested;
            row["allResult"] = resultId;
            row["partName"] = line[2].Trim();
            row["hPin"] = line[3].Trim();
            row["lPin"] = line[4].Trim();
            row["pos"] = line[5].Trim();
            row["mode"] = modeId;
            row["rangeNum"] = int.Parse(line[7].Trim());
            row["hLim"] = hLim.HasValue ? hLim.Value : DBNull.Value;
            row["lLim"] = lLim.HasValue ? lLim.Value : DBNull.Value;
            row["measurementUnit"] = unit ?? (object)DBNull.Value;
            row["act"] = act.HasValue ? act.Value : DBNull.Value;
            row["ref"] = refVal.HasValue ? refVal.Value : DBNull.Value;
            row["meas"] = meas.HasValue ? meas.Value : DBNull.Value;

            table.Rows.Add(row);
        }

        // Perform the Bulk Copy with special "using" and "new"
        using SqlBulkCopy bulkCopy = new (context.Connection, SqlBulkCopyOptions.CheckConstraints, context.Transaction);
        bulkCopy.DestinationTableName = "pe3coop.dbo.StepResults";

        // Map the DataTable columns to the Database columns
        foreach (DataColumn column in table.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        try
        {
            await bulkCopy.WriteToServerAsync(table);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Bulk Copy Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Helper function for ParseStepResults (handles the FCT section). Picks up at the beginning of the FCT-specific content and creates a new entry in the database for every entry in the file.
    /// </summary>
    /// <param name="context"> the context required to parse the FCT section of a step file. </param>
    /// <returns>A Task representing that the FCT section has been parsed.</returns>
    private static async Task ParseFctSection(ParsingContext context)
    {
        DataTable table = new ();
        table.Columns.Add("barcode", typeof(string));
        table.Columns.Add("testTime", typeof(DateTime));
        table.Columns.Add("groupNum", typeof(int));
        table.Columns.Add("stepNum", typeof(int));
        table.Columns.Add("allResult", typeof(byte)); // Maps to tinyint
        table.Columns.Add("measGrp", typeof(int));
        table.Columns.Add("comment", typeof(string));
        table.Columns.Add("pos", typeof(string));
        table.Columns.Add("mode", typeof(byte)); // Maps to tinyint
        table.Columns.Add("hPin", typeof(string));
        table.Columns.Add("lPin", typeof(string));
        table.Columns.Add("ref", typeof(double));
        table.Columns.Add("meas", typeof(double));
        table.Columns.Add("hLim", typeof(double));
        table.Columns.Add("lLim", typeof(double));
        table.Columns.Add("measurementUnit", typeof(char));
        table.Columns.Add("id1", typeof(string));
        table.Columns.Add("id2", typeof(string));
        table.Columns.Add("id3", typeof(string));
        table.Columns.Add("id4", typeof(string));
        table.Columns.Add("inputVol", typeof(double));
        table.Columns.Add("commStd", typeof(string));
        table.Columns.Add("executeMode", typeof(string));
        table.Columns.Add("devAddress", typeof(string));
        table.Columns.Add("sendAddress", typeof(string));
        table.Columns.Add("writeRefData", typeof(string));
        table.Columns.Add("receiveData", typeof(string));
        table.Columns.Add("ifResponse", typeof(string));

        string? raw;
        while ((raw = await context.Reader.ReadLineAsync()) != null)
        {
            if (raw.Contains("[EOT]"))
            {
                break; // If we see EOT, the FCT section is finished
            }

            string[] line = raw.Split(',');

            // Found new group
            if (line[0].StartsWith("Gr"))
            {
                context.Data.Group = int.TryParse(line[1].Trim(), out int tt) ? tt : 0;
                continue;
            }

            if (line[0].Contains("Rslt"))
            {
                continue; // found header
            }

            // Check the caches
            byte resultId = await GetCachedId(line[0].Trim(), true, context);
            byte modeId = await GetCachedId(line[5].Trim(), false, context);

            // The discard operator "_" says to ignore the unit (because we already have it)
            (double? hLim, string? unit) = CleanFloatValue(line[10]);
            (double? lLim, string? _) = CleanFloatValue(line[11]);
            (double? inputVol, string? _) = CleanFloatValue(line[16]);

            // Create a new row in the internal DataTable
            DataRow row = table.NewRow();
            row["barcode"] = context.Data.Barcode;
            row["testTime"] = context.Data.TestTime;
            row["groupNum"] = context.Data.Group;
            row["stepNum"] = int.Parse(line[1].Trim());
            row["allResult"] = resultId;
            row["measGrp"] = int.Parse(line[2].Trim());
            row["comment"] = line[3].Trim();
            row["pos"] = line[4].Trim();
            row["mode"] = modeId;
            row["hPin"] = line[6].Trim();
            row["lPin"] = line[7].Trim();
            row["ref"] = HexOrSciToDouble(line[8]);
            row["meas"] = HexOrSciToDouble(line[9]);
            row["hLim"] = hLim.HasValue ? hLim.Value : DBNull.Value;
            row["lLim"] = lLim.HasValue ? lLim.Value : DBNull.Value;
            row["measurementUnit"] = unit ?? (object)DBNull.Value;
            row["id1"] = line[12].Trim();
            row["id2"] = line[13].Trim();
            row["id3"] = line[14].Trim();
            row["id4"] = line[15].Trim();
            row["inputVol"] = inputVol.HasValue ? inputVol.Value : DBNull.Value;
            row["commStd"] = line[17].Trim();
            row["executeMode"] = line[18].Trim();
            row["devAddress"] = line[19].Trim();
            row["sendAddress"] = line[20].Trim();
            row["writeRefData"] = line[21].Trim();
            row["receiveData"] = line[22].Trim();
            row["ifResponse"] = line[23].Trim();

            table.Rows.Add(row);
        }

        // Once the table's full, prepare the DB insert
        using SqlBulkCopy bulkCopy = new (context.Connection, SqlBulkCopyOptions.CheckConstraints, context.Transaction);
        bulkCopy.DestinationTableName = "pe3coop.dbo.FCTResults";

        // Map the DataTable columns to the DB columns in case there's a discrepancy (technically unnecessary but this protects the code in case DB structure changes)
        foreach (DataColumn column in table.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        try
        {
            await bulkCopy.WriteToServerAsync(table);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Bulk Copy Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the connection string for the database whose credentials are stored in environment variables.
    /// </summary>
    /// <returns>A SQL Server connection string for access to the database.</returns>
    /// <throws>InvalidOperationException when there are missing environment variable(s).</throws>
    private static string GetConnectionString()
    {
        static string GetRequired(string key)
        {
            string? value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Required environment variable '{key}' is missing for database connection.");
            }

            return value;
        }

        SqlConnectionStringBuilder builder = new ()
        {
            DataSource = GetRequired("DB_SERVER"),
            UserID = GetRequired("DB_USER"),
            Password = GetRequired("DB_PASS"),
            InitialCatalog = GetRequired("HIOKI_DB_NAME"),
            TrustServerCertificate = true,
        };
        return builder.ConnectionString;
    }

    [GeneratedRegex(@"^\s*(?<value>[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)\s*(?<unit>[^,]*?)\s*$", RegexOptions.Compiled)]
    private static partial Regex MyRegex(); // this generates at compile-time, which was suggested by Intellisense
}
