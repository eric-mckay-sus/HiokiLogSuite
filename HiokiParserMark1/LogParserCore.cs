// <copyright file="LogParserCore.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace HiokiParserMark1;

using System.Data;
using Microsoft.Data.SqlClient;

using static LogParserUtilities;
using InterProcessIO;

/// <summary>
/// Lays out the core log parsing routine: Specify the file/folder, batch as necessary, process individual files, consume each line.
/// When the file format changes, this WILL fail silently. In general, the file shape does not lend itself to detecting when something that 'should be' good is skipped
/// There's simply too many idiosyncrasies (which headers get skipped, good data blocks sometimes being intentionally ignored) for me to flag 'bad' input.
/// </summary>
public class LogParserCore
{
    /// <summary>
    /// Determines where user input comes from.
    /// </summary>
    private readonly IInputProvider input;

    /// <summary>
    /// Determines where/how program output is displayed.
    /// </summary>
    private readonly IOutputProvider output;

    /// <summary>
    /// Initializes a new instance of the <see cref="LogParserCore"/> class.
    /// By default, uses the console for input and output.
    /// </summary>
    public LogParserCore()
    {
        this.input = new ConsoleInputProvider();
        this.output = new ConsoleReporter();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LogParserCore"/> class, using the specified input and output providers.
    /// </summary>
    /// <param name="inputProvider">The instance of IInputProvider to be used to get input regarding model mapping details.</param>
    /// <param name="outputProvider">The instance of IReportOutputProvider to be used for displaying program results.</param>
    public LogParserCore(IInputProvider inputProvider, IOutputProvider outputProvider)
    {
        this.input = inputProvider;
        this.output = outputProvider;
    }

    /// <summary>
    /// Entry point for the program. Delegates to <see cref="ExecuteAsync"/> for actual parsing, then shows the end-of-run message.
    /// This method will only use the console for I/O, so it is not recommended for use in other programs.
    /// </summary>
    /// <param name="args">The directory to search (must only contain files of the correct filetype and format).</param>
    /// <returns>A Task representing that the batch is finished.</returns>
    public static async Task Main(string[] args)
    {
        // If there was an input location argument, pass it along (no validation here)
        string? potentialFile = null;
        if (args.Length > 0)
        {
            potentialFile = args[0];
        }

        // Exit static by creating an uploader
        LogParserCore uploader = new ();

        // Then give it the green light
        UploadResult result = await uploader.ExecuteAsync(potentialFile);

        switch (result)
        {
            case UploadResult.Complete:
                Console.WriteLine("Upload successful.");
                break;
            case UploadResult.CompleteWithErrors:
                Console.WriteLine("Some files failed to upload. Please read the above reports to identify the problem.");
                break;
            case UploadResult.ErroredOut:
                Console.WriteLine("Upload failed. Please see above error to identify the problem.");
                break;
            case UploadResult.Canceled:
                Console.WriteLine("Upload canceled.");
                break;
        }

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    /// <summary>
    /// Router to <see cref="ParseHioki"/> to handle single-file/batch operations.
    /// If calling from an outside program, it is highly recommended to collect filepath beforehand to pass in, as there is NO option to do so internally.
    /// </summary>
    /// <param name="filename">The optional filepath to upload (defaults to <see cref="Config.InputLocation"/>).</param>
    /// <returns>A Task representing the upload status.</returns>
    public async Task<UploadResult> ExecuteAsync(string? filename = null)
    {
        string path = Config.InputLocation;
        if (string.IsNullOrWhiteSpace(filename))
        {
            await this.Report($"No file specified. Defaulting to config file input location ({path})\n");
        }
        else if (!Path.Exists(path))
        {
            await this.Report($"Path '{filename}' is not a valid directory or Excel file. Using Config default ({path}).\n", ReportLevel.WARNING);
        }
        else
        {
            path = filename;
        }

        // Console.ReadLine natively handles spaces, but if the user added them anyway, trim them
        // The Unicode characters 200E and 200F appear when a user uses drag-drop, which is supported by most terminals
        path = path.Trim().Trim('"', '\u200E', '\u200F');
        if (!string.IsNullOrWhiteSpace(path))
        {
            path = Path.GetFullPath(path);
        }

        try
        {
            bool isFolder;
            if (Directory.Exists(path))
            {
                isFolder = true;
            }
            else if (File.Exists(path))
            {
                isFolder = false;
            }

            // Should never reach here unless file is somehow deleted during validation, but handle it for fewer potential errors
            else
            {
                await this.Report($"Could not find {path}. Please verify the path is correct, then try again.\n", ReportLevel.ERROR);
                return UploadResult.ErroredOut;
            }

            await this.Report("Connecting...");
            using SqlConnection conn = new (Config.GetConnectionString());
            await conn.OpenAsync();
            await this.Report("Connected!\n");
            await InitializeCaches(conn);
            await this.Report("Parsing...");
            if (isFolder)
            {
                string[] files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    await this.ParseHioki(file, conn);
                }

                await this.Report($"Complete! {files.Length} files added to database.\n", ReportLevel.SUCCESS);
                await this.output.ReportProgress(ProgressEvent.UploadComplete);
                return UploadResult.Complete;
            }
            else
            {
                await this.ParseHioki(path, conn);
                await this.Report("Complete!", ReportLevel.SUCCESS);
                await this.output.ReportProgress(ProgressEvent.UploadComplete);
                return UploadResult.Complete;
            }
        }
        catch (Exception e)
        {
            await this.Report($"Fatal error: {e.Message}", ReportLevel.ERROR);
            return UploadResult.ErroredOut;
        }
    }

    /// <summary>
    /// Gets the barcode, test datetime, and number of times tested from the header common between step and group result files,
    /// then passes the context to the appropriate handler for the rest of the file to parse one entire Hioki log file and add it to the DB.
    /// </summary>
    /// <param name="file"> the file to parse. </param>
    /// <param name="conn">The <see cref="SqlConnection"/> to use for this file.</param>
    /// <returns>A Task representing that the file has been parsed.</returns>
    private async Task<List<LogType>> ParseHioki(string file, SqlConnection conn)
    {
        List<LogType> toReturn = [];
        await this.output.SetCurrentFile(file);
        await this.output.ReportProgress(ProgressEvent.FileStarted);

        // If there is a file-related error (like the file being open in another process), there's nothing to be done
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
                await this.Report($"Error reading timesTested for {file}\n", ReportLevel.ERROR);
                return [];
            }

            reader.ReadLine(); // cut "Lot No."

            // Parse barcode
            line = reader.ReadLine();
            package.Barcode = line?.Split(',')[1].Trim() ?? "UNKNOWN";

            // If Barcode is null, say which file, and skip it (barcode is primary key)
            if (line == "UNKNOWN")
            {
                await this.Report($"Error reading barcode for {file}\n", ReportLevel.ERROR);
                return [];
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
                    await this.Report($"Error: Could not parse date string '{fullDtStr}' in {file}\n", ReportLevel.ERROR);
                    return [];
                }
            }
            else
            {
                await this.Report($"Error: Date/Time line malformed in {file}", ReportLevel.ERROR);
                return [];
            }

            // If there is a parse error, roll back the transaction (i.e. file)
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
                        await this.ParseGroupFile(context);
                    }
                    else if (line.Contains("-----  Component  -----"))
                    {
                        await reader.ReadLineAsync(); // Cut the column name row
                        string? groupLine = await reader.ReadLineAsync(); // Get the line containing the group number
                        string[]? groupParts = groupLine?.Split(',');
                        context.Data.Group = (groupParts?.Length > 1 && int.TryParse(groupParts[1].Trim(), out int g)) ? g : 0;
                        await this.ParseStepFile(context); // FCT handled here
                    }

                    // In theory, there could be an FCT-specific callout for the new file format, but I don't know of that header.
                    // As for now, it is silently skipped (as are any other headers that don't change the section).
                    line = await reader.ReadLineAsync();
                }

                transaction.Commit();
                await this.output.ReportProgress(ProgressEvent.FileCompleted);
                return toReturn;
            }
            catch (Exception)
            {
                transaction.Rollback();
                return toReturn;
                throw;
            }
        }
        catch (UnauthorizedAccessException)
        {
            await this.Report("Error: You do not have permission to read this file: " + file + "\n)", ReportLevel.ERROR);
            await this.output.ReportProgress(ProgressEvent.FileSkipped);
            return toReturn;
        }
        catch (IOException ex)
        {
            await this.Report($"I/O Error: {ex.Message}", ReportLevel.ERROR);
            await this.output.ReportProgress(ProgressEvent.FileSkipped);
            return toReturn;
        }
        catch (Exception ex)
        {
            await this.Report($"Unexpected Error: {ex.Message}", ReportLevel.ERROR);
            await this.output.ReportProgress(ProgressEvent.FileSkipped);
            return toReturn;
        }
    }

    /// <summary>
    /// Helper function for ParseHioki (handles group files). Picks up at the beginning of the group-specific content and creates a new entry in the database for every entry in the file.
    /// </summary>
    /// <param name="context"> the context required to parse a group file. </param>
    /// <returns>A Task representing that the group file has been parsed.</returns>
    private async Task ParseGroupFile(ParsingContext context)
    {
        // Create a DataTable to hold the data in memory
        DataTable table = new ();
        table.Columns.Add("barcode", typeof(string));
        table.Columns.Add("testTime", typeof(DateTime));
        table.Columns.Add("groupNumber", typeof(int));
        table.Columns.Add("timesTested", typeof(int));
        table.Columns.Add("allResult", typeof(byte)); // Maps to tinyint
        table.Columns.Add("componentTest", typeof(byte));
        table.Columns.Add("shortTest", typeof(byte));
        table.Columns.Add("openTest", typeof(byte));
        table.Columns.Add("icTest", typeof(byte));
        table.Columns.Add("macroTest", typeof(byte));
        table.Columns.Add("functionTest", typeof(byte));

        string? raw;
        while ((raw = await context.Reader.ReadLineAsync()) != null)
        {
            if (raw.Contains("[EOT]"))
            {
                break; // If we find EOT, that means the group section is complete
            }

            string[] split = raw.Split(",");
            if (split != null)
            {
                // If the row is too short, skip it
                if (split.Length < 8)
                {
                    continue;
                }

                DataRow row = table.NewRow();

                // Load the common parameters first
                row["barcode"] = context.Data.Barcode;
                row["testTime"] = context.Data.TestTime;
                row["groupNumber"] = int.Parse(split[1].Trim()); // Group number requires an explicit cast because it's not yet the correct type
                row["timesTested"] = context.Data.TimesTested;

                // GetCachedId returns a byte, so no cast needed
                row["allResult"] = await GetCachedId(split[0].Trim(), true, context);
                row["componentTest"] = await GetCachedId(split[2].Trim(), true, context);
                row["shortTest"] = await GetCachedId(split[3].Trim(), true, context);
                row["openTest"] = await GetCachedId(split[4].Trim(), true, context);
                row["icTest"] = await GetCachedId(split[5].Trim(), true, context);
                row["macroTest"] = await GetCachedId(split[6].Trim(), true, context);
                row["functionTest"] = await GetCachedId(split[7].Trim(), true, context);

                table.Rows.Add(row);
            }
        }

        // Perform the Bulk Copy with special "using" and "new"
        using SqlBulkCopy bulkCopy = new (context.Connection, SqlBulkCopyOptions.CheckConstraints, context.Transaction);
        bulkCopy.DestinationTableName = "pe3coop.dbo.GroupResults";

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
            await this.Report($"Bulk Copy Error: {ex.Message}", ReportLevel.ERROR);
        }
    }

    /// <summary>
    /// Helper function for ParseHioki (handles step files). Picks up at the beginning of the step-specific content and creates a new entry in the database for every entry in the file.
    /// </summary>
    /// <param name="context"> the context required to parse a step file. </param>
    /// <returns>A Task representing that the step file has been parsed.</returns>
    private async Task ParseStepFile(ParsingContext context)
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
            // If the line is empty, skip it
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            // If we see EOT, the step results are finished
            if (raw.Contains("[EOT]"))
            {
                break;
            }

            string[] line = raw.Split(',');
            if (line[0].StartsWith("Gr"))
            {
                context.Data.Group = int.TryParse(line[1].Trim(), out int tt) ? tt : 0;
                continue;
            }

            if (line[0].Contains("-----  FCT  -----"))
            {
                await this.ParseFctSection(context);
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
            await this.Report($"Bulk Copy Error: {ex.Message}", ReportLevel.ERROR);
        }
    }

    /// <summary>
    /// Helper function for ParseStepResults (handles the FCT section). Picks up at the beginning of the FCT-specific content and creates a new entry in the database for every entry in the file.
    /// </summary>
    /// <param name="context"> the context required to parse the FCT section of a step file. </param>
    /// <returns>A Task representing that the FCT section has been parsed.</returns>
    private async Task ParseFctSection(ParsingContext context)
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
            await this.Report($"Bulk Copy Error: {ex.Message}", ReportLevel.ERROR);
        }
    }

    /// <summary>
    /// Creates a report and passes it to the output provider.
    /// Enclose console-specific information in parentheses for Blazor to hide it.
    /// </summary>
    /// <param name="msg">The message to report.</param>
    /// <param name="level">The message's report level.</param>
    /// <returns>A Task representing that the report has been displayed to the user.</returns>
    private async Task Report(string msg, ReportLevel level = ReportLevel.INFO) => await this.output.ReportAsync(new (msg, level));
}
