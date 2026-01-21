using Microsoft.Data.SqlClient;
using Microsoft.Identity.Client;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using System.Text;

/// <summary>
/// Parses Hioki 1220-50 output files (group & step), and saves it to a remote database
/// Needs details on how barcodes change within one file
/// Consider adding more control for what table to access (use consts that will eventually be environment variables)
/// </summary>
class Program
{
    private static string connectionString = ""; // string of the information necessary to open a connection (insecure?)

    private static ConcurrentDictionary<string, byte> ResultTypeCache = new ConcurrentDictionary<string, byte>();
    private static ConcurrentDictionary<string, byte> TestModeCache = new ConcurrentDictionary<string, byte>();

    /// <summary>
    /// Abstracts the four fields common between group files and step files to reduce the arguments passed through
    /// </summary>
    public class CommonPackage
    {
        public string barcode = ""; // the base product ID from which all sub-barcodes can be computed
        public DateTime testTime; // the timestamp of when this product was tested
        public int timesTested; // the number of times this product has been tested
        public int? group; // the group number for this product, ignored for group file parsing
    }
    /// <summary>
    /// Entry point for the program. Parses every file in the specified folder and adds it to the database.
    /// </summary>
    /// <param name="args"> The directory to search (must only contain files of the correct filetype and format)</param>
    /// <returns></returns>
    static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("No folder argument detected. Please retry and supply path for folder to check.");
            return;
        }
        Console.Write("Connecting...");
        var builder = new SqlConnectionStringBuilder //TODO refactor as part of the environment
        {
            DataSource = "SUS-SQL-02",
            UserID = "pe3coop",
            Password = "pe3coop",
            InitialCatalog = "pe3Coop",
            TrustServerCertificate = true // technically insecure but can be swapped for adding the certificate to the client environment
        };
        connectionString = builder.ConnectionString;

        await InitializeCaches();
        string[] files = Directory.GetFiles(args[0], "*.*", SearchOption.AllDirectories);
        foreach (string file in files)
        {
            await ParseHioki(file);
        }
    }

    /// <summary>
    /// Initializes caches for ResultType and TestMode to reduce DB queries
    /// </summary>
    /// <returns></returns>
    private static async Task InitializeCaches()
    {
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            Console.WriteLine("Connected!");

            // Fill ResultTypeCache
            using (SqlCommand cmd = new SqlCommand("SELECT id, resultType FROM pe3coop.dbo.ResultTypes", connection))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    ResultTypeCache.TryAdd(reader.GetString(1).Trim(), reader.GetByte(0));
            }

            // Fill TestModeCache
            using (SqlCommand cmd = new SqlCommand("SELECT id, testMode FROM pe3coop.dbo.TestModes", connection))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    TestModeCache.TryAdd(reader.GetString(1).Trim(), reader.GetByte(0));
            }
        }
    }

    /// <summary>
    /// Gets the barcode, test datetime, and number of times tested from the header common between step and group result files,
    /// then passes the reader to the appropriate handler for the rest of the file to parse one entire Hioki log file and add it to the DB
    /// </summary>
    /// <param name="file"> the file to parse </param>
    /// <returns></returns>
    private static async Task ParseHioki(string file)
    {
        try
        {
            using (StreamReader reader = new StreamReader(file))
            {
                CommonPackage package = new CommonPackage();
                reader.ReadLine(); // cut "[Test Results]"
                reader.ReadLine(); // cut "File: filename"

                // Parse timesTested as an int
                string? line = reader.ReadLine();
                package.timesTested = int.TryParse(line?.Split(',')[1], out int tt) ? tt : 0;
                if (package.timesTested == 0) // if timesTested is null, say which file, and skip it (timesTested is primary key)
                {
                    Console.Error.WriteLine($"Error reading timesTested for {file}");
                    return;
                }
                reader.ReadLine(); // cut "Lot No."

                // Parse barcode
                line = reader.ReadLine();
                package.barcode = line?.Split(',')[1].Trim() ?? "UNKNOWN";
                if (line == null) // if the barcode is null, say which file, and skip it (barcode is primary key)
                { 
                    Console.Error.WriteLine($"Error reading barcode for {file}");
                    return;
                }

                // Parse testTime
                line = reader.ReadLine();
                string[]? dateParts = line?.Split(',');

                if (dateParts != null && dateParts.Length >= 3)
                {
                    // Combine Date [1] and Time [2] with a space
                    string fullDtStr = dateParts[1].Trim() + " " + dateParts[2].Trim();

                    if (DateTime.TryParse(fullDtStr, out DateTime dt))
                    {
                        package.testTime = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second);
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

                // Determine if this is a group or step file
                string groupOrStep = reader.ReadLine()?.Split(',')[0].Trim() ?? "";
                reader.ReadLine(); // Cut the column name row

                using (SqlConnection connection = new SqlConnection(connectionString)) // Create the connection to be used until this row is complete
                {
                    await connection.OpenAsync();
                    using (SqlTransaction transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            if (groupOrStep.Contains("-----  Group  -----"))
                            {
                                await ParseGroupFile(reader, package, connection, transaction); // pass in reader and connection to maintain the current line and connection status
                            }
                            else
                            {
                                string? groupLine = reader.ReadLine();
                                string[]? groupParts = groupLine?.Split(',');
                                package.group = (groupParts?.Length > 1 && int.TryParse(groupParts[1].Trim(), out int g)) ? g : 0;
                                await ParseStepFile(reader, package, connection, transaction);
                            }
                            transaction.Commit();
                        }
                        catch (Exception)
                        {
                            transaction.Rollback();
                            throw;
                        }
                    } // transaction auto-closes                   
                } // connection auto-closes
            } // reader auto-closes
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
    /// Helper function for ParseHioki (handles group files). Picks up at the beginning of the group-specific content and creates a new entry in the database for every entry in the file
    /// </summary>
    /// <param name="reader"> The StreamReader to scan the file with, primed to the first group entry </param>
    /// <param name="package"> The CommonPackage containing the barcode, time of testing, and number of times the group was tested </param>
    /// <param name="connection"> The connection to the SQL database </param>
    /// <returns></returns>
    private static async Task ParseGroupFile(StreamReader reader, CommonPackage package, SqlConnection connection, SqlTransaction transaction) //TODO needs to handle multiple items on same barcode
    {
        string sql = @"INSERT INTO pe3coop.dbo.GroupResults (barcode, testTime, groupNumber, timesTested, allResult,
                       componentTest, shortTest, openTest, icTest, macroTest, functionTest)
                       VALUES (@barcode, @testTime, @groupNum, @timesTested, @result,
                       @comp, @short, @open, @ic, @macro, @function)"; // Reflects order in DB
        string[] paramNames = { "@comp", "@short", "@open", "@ic", "@macro", "@function" }; // to map the line values to their parameters in SQL, reflects order in CSV
        while (!reader.EndOfStream) // The rest of the file are group test results to parse
        {
            string? line = reader.ReadLine();
            string[]? split = line?.Split(",");
            if (split != null)
            {
                int resultId = await GetCachedId(split[0].Trim(), connection, true, transaction);
                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    command.Transaction = transaction;
                    command.Parameters.AddWithValue("@barcode", package.barcode);
                    command.Parameters.AddWithValue("@testTime", package.testTime);
                    command.Parameters.AddWithValue("@groupNum", split[1].Trim());
                    command.Parameters.AddWithValue("@timesTested", package.timesTested);
                    command.Parameters.AddWithValue("@result", resultId);
                    for (int i = 0; i < paramNames.Length; i++)
                    {
                        string value = (split.Length > i + 2) ? split[i + 2].Trim() : "";
                        command.Parameters.AddWithValue(paramNames[i], value);
                    }
                    await command.ExecuteNonQueryAsync();
                }
            }
        }
    }

    /// <summary>
    /// Helper function for ParseHioki (handles step files). Picks up at the beginning of the step-specific content and creates a new entry in the database for every entry in the file
    /// </summary>
    /// <param name="reader"> The StreamReader to scan the file with, primed to the first step entry </param>
    /// <param name="package"> The CommonPackage containing the barcode, time of testing, group number, and number of times the group was tested </param>
    /// <param name="connection"> The connection to the SQL database </param>
    /// <returns></returns>
    private static async Task ParseStepFile(StreamReader reader, CommonPackage package, SqlConnection connection, SqlTransaction transaction) // needs barcode extension
    {
        // 1. Create a DataTable to hold the data in memory
        DataTable table = new DataTable();
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
        table.Columns.Add("mode", typeof(string));
        table.Columns.Add("rangeNum", typeof(int));
        table.Columns.Add("hLim", typeof(double));
        table.Columns.Add("lLim", typeof(double));
        table.Columns.Add("act", typeof(double));
        table.Columns.Add("ref", typeof(double));
        table.Columns.Add("meas", typeof(double));

        string? raw;
        while ((raw = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            string[] line = raw.Split(',');
            if (line[0].StartsWith("Gr"))
            {
                package.group = int.TryParse(line[1].Trim(), out int tt) ? tt : 0;
                continue;
            }
            if (line[0].Contains("-----  FCT  -----"))
            {
                //await ParseFctSection(reader, package, connection, transaction, table);
                return;
            }
            if (line.Length < 13) continue;

            // 2. Parse the result ID (This remains a separate call)
            int resultId = await GetCachedId(line[0].Trim(), connection, true, transaction);

            // 3. Add a row to the DataTable
            DataRow row = table.NewRow();
            row["barcode"] = package.barcode;
            row["testTime"] = package.testTime;
            row["groupNum"] = package.group;
            row["stepNum"] = int.Parse(line[1].Trim());
            row["timesTested"] = package.timesTested;
            row["allResult"] = (byte)resultId;
            row["partName"] = line[2].Trim();
            row["hPin"] = line[3].Trim();
            row["lPin"] = line[4].Trim();
            row["pos"] = line[5].Trim();
            row["mode"] = line[6].Trim();
            row["rangeNum"] = int.Parse(line[7].Trim());

            row["hLim"] = CleanFloatValue(line[8]);
            row["lLim"] = CleanFloatValue(line[9]);
            row["act"] = CleanFloatValue(line[10]);
            row["ref"] = CleanFloatValue(line[11]);
            row["meas"] = CleanFloatValue(line[12]);

            table.Rows.Add(row);
        }

        // 4. Perform the Bulk Copy
        using (SqlBulkCopy bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, transaction))
        {
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
    }

    /// <summary>
    /// To ensure SQL has a readable float
    /// </summary>
    /// <param name="input"> The string representation of the float to be cleaned </param>
    /// <returns> An object representing the float </returns>
    private static object CleanFloatValue(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return DBNull.Value;
        string cleaned = input.Replace("%", "").Trim();
        return double.TryParse(cleaned, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double result)
               ? result : DBNull.Value;
    }

    /// <summary>
    /// Helper function for ParseStepResults (handles the FCT section). Picks up at the beginning of the FCT-specific content and creates a new entry in the database for every entry in the file
    /// </summary>
    /// <param name="reader"> The StreamReader to scan the file with, primed to the first FCT entry </param>
    /// <param name="package"> The CommonPackage containing the barcode, time of testing, group number, and number of times the group was tested </param>
    /// <param name="connection"> The connection to the SQL database </param>
    /// <returns></returns>
    private static async Task ParseFctSection(StreamReader reader, CommonPackage package, SqlConnection connection, SqlTransaction transaction, DataTable stepTable)
    {
        DataTable table = new DataTable();
        table.Columns.Add("barcode", typeof(string));
        table.Columns.Add("testTime", typeof(DateTime));
        table.Columns.Add("groupNum", typeof(int));
        table.Columns.Add("stepNum", typeof(int));
        table.Columns.Add("allResult", typeof(byte)); // Maps to tinyint
        table.Columns.Add("measGrp", typeof(string));
        table.Columns.Add("comment", typeof(string));
        table.Columns.Add("pos", typeof(string));
        table.Columns.Add("mode", typeof(string));
        table.Columns.Add("hPin", typeof(string));
        table.Columns.Add("lPin", typeof(string));
        table.Columns.Add("ref", typeof(double));
        table.Columns.Add("meas", typeof(double));
        table.Columns.Add("hLim", typeof(double));
        table.Columns.Add("lLim", typeof(double));
        table.Columns.Add("id1", typeof(string));
        table.Columns.Add("id2", typeof(string));
        table.Columns.Add("id3", typeof(string));
        table.Columns.Add("id4", typeof(string));
        table.Columns.Add("inputVol", typeof(string));
        table.Columns.Add("commStd", typeof(string));
        table.Columns.Add("executeMode", typeof(string));
        table.Columns.Add("devAddress", typeof(string));
        table.Columns.Add("sendAddress", typeof(string));
        table.Columns.Add("writeRefData", typeof(string));
        table.Columns.Add("receiveData", typeof(string));
        table.Columns.Add("ifResponse", typeof(string));

        string? raw;
        while ((raw = await reader.ReadLineAsync()) != null)
        {
            string[] line = raw.Split(',');
            //Skips
            if (line[0].StartsWith("Gr"))
            {
                package.group = int.TryParse(line[1].Trim(), out int tt) ? tt : 0;
                continue;
            }
            if (line[0].Contains("Rslt")) continue;

            int resultId = await GetCachedId(line[0].Trim(), connection, true, transaction);
            int modeId = await GetCachedId(line[5].Trim(), connection, false, transaction);

            DataRow row = table.NewRow();
            row["barcode"] = package.barcode;
            row["testTime"] = package.testTime;
            row["groupNum"] = package.group;
            row["stepNum"] = int.Parse(line[1].Trim());
            row["allResult"] = (byte)resultId;
            row["measGrp"] = int.Parse(line[2].Trim());
            row["comment"] = line[3].Trim();
            row["pos"] = line[4].Trim();
            row["mode"] = (byte)modeId;
            row["hPin"] = line[6].Trim();
            row["lPin"] = line[7].Trim();
            row["ref"] = CleanFloatValue(line[8]);
            row["meas"] = CleanFloatValue(line[9]);

            row["hLim"] = CleanFloatValue(line[10]);
            row["lLim"] = CleanFloatValue(line[11]);
            row["id1"] = line[12].Trim();
            row["id2"] = line[13].Trim();
            row["id3"] = line[14].Trim();
            row["id4"] = line[15].Trim();
            row["inputVol"] = line[16].Trim();
            row["commStd"] = line[17].Trim();
            row["executeMode"] = line[18].Trim();
            row["devAddress"] = line[19].Trim();
            row["sendAddress"] = line[20].Trim();
            row["writeRefData"] = line[21].Trim();
            row["receiveData"] = line[22].Trim();
            row["ifResponse"] = line[23].Trim();

            table.Rows.Add(row);
        }
        using (SqlBulkCopy bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, transaction))
        {
            bulkCopy.DestinationTableName = "pe3coop.dbo.FCTResults";

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
    }

    /// <summary>
    /// Runs a SELECT statement to see if a certain result type or mode is already in the DB.
    /// If it isn't, this method creates a new row for it
    /// </summary>
    /// <param name="toCheck"> A string representing the result or mode to check for </param>
    /// <param name="connection"> The connection to the SQL database </param>
    /// <param name="typeMode"> true to indicate type, false to indicate mode</param>
    /// <returns> the id of the type or mode, either existing or new </returns>
    static public async Task<int> GetCachedId(string toCheck, SqlConnection connection, bool isResultType, SqlTransaction transaction)
    {
        if (string.IsNullOrWhiteSpace(toCheck)) return 0;

        var cache = isResultType ? ResultTypeCache : TestModeCache;

        // 1. Try to get from local memory
        if (cache.TryGetValue(toCheck, out byte existingId))
        {
            return existingId;
        }

        // 2. If not in memory, insert into DB (Locked to prevent duplicates in high-concurrency)
        string tableName = isResultType ? "pe3coop.dbo.ResultTypes" : "pe3coop.dbo.TestModes";
        string columnName = isResultType ? "resultType" : "testMode";

        string insertSql = $@"
        IF NOT EXISTS (SELECT 1 FROM {tableName} WHERE {columnName} = @val)
        BEGIN
            INSERT INTO {tableName} ({columnName}) VALUES (@val);
        END
        SELECT id FROM {tableName} WHERE {columnName} = @val;";

        using (SqlCommand command = new SqlCommand(insertSql, connection))
        {
            command.Transaction = transaction;
            command.Parameters.AddWithValue("@val", toCheck);
            object? result = await command.ExecuteScalarAsync();
            byte newId = Convert.ToByte(result);

            // Update cache so we don't hit the DB for this string again
            cache.TryAdd(toCheck, newId);
            return newId;
        }
    }
}