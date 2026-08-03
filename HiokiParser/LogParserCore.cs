// <copyright file="LogParserCore.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace HiokiParser;

using System.Data;
using Microsoft.Data.SqlClient;

using static LogParserUtilities;
using InterProcessIO;
using System.Globalization;

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
        this.output.ClearLogs(); // If this is another run on the same object, ensure the output provider is clean
        string filePath = await this.PromptForFile(filename);

        try
        {
            bool isFolder;
            if (Directory.Exists(filePath))
            {
                isFolder = true;
            }
            else if (File.Exists(filePath))
            {
                isFolder = false;
            }

            // Should never reach here unless file is somehow deleted during validation, but handle it for fewer potential errors
            else
            {
                await this.Report($"Could not find {filePath}. Please verify the path is correct, then try again.\n", ReportLevel.ERROR);
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
                string[] files = Directory.GetFiles(filePath, "*.csv", SearchOption.AllDirectories);
                this.output.InitializeProgress(files.Length);
                foreach (string file in files)
                {
                    await this.ParseHioki(file, conn);
                }

                await this.Report($"Complete! {files.Length} files added to database.\n", ReportLevel.SUCCESS);
            }
            else
            {
                this.output.InitializeProgress(1); // In the single-file case, there is one file (trivial)
                await this.ParseHioki(filePath, conn);
                await this.Report("Complete!", ReportLevel.SUCCESS);
            }

            await this.output.ReportProgress(ProgressEvent.UploadComplete);
            return UploadResult.Complete;
        }
        catch (Exception e)
        {
            await this.Report($"Fatal error: {e.Message}", ReportLevel.ERROR);
            return UploadResult.ErroredOut;
        }
    }

    /// <summary>
    /// Processes one row of the current file that has been identified as an group result.
    /// </summary>
    /// <param name="raw">The current row.</param>
    /// <param name="context">The context required to parse an group row.</param>
    /// <param name="table">The DataTable to insert the parsed data into.</param>
    /// <returns>A value indicating whether parsing should continue.</returns>
    private static async Task<bool> ProcessGroupRow(string raw, ParsingContext context, DataTable table)
    {
        if (raw.Contains("[EOT]"))
        {
            context.CurrentSection = null;
            return false; // If we find EOT, that means the group section is complete
        }

        if (raw.Contains(StepHeader))
        {
            context.CurrentSection = SectionType.Step;
            return false; // If we find EOT, that means the group section is complete
        }

        string[] split = raw.Split(",");
        if (split != null)
        {
            // If the row is too short, skip it
            if (split.Length < 8)
            {
                return true;
            }

            DataRow row = table.NewRow();

            // Load the common parameters first
            row[BarcodeColName] = context.Data.Barcode;
            row[TestTimeColName] = context.Data.TestTime;
            row[GroupNumColName] = int.Parse(split[1].Trim()); // Group number requires an explicit cast because it's not yet the correct type
            row[TimesTestedColName] = context.Data.TimesTested;

            // GetCachedId returns a byte, so no cast needed
            row[AllResultColName] = await GetCachedId(split[0].Trim(), true, context);
            row["componentTest"] = await GetCachedId(split[2].Trim(), true, context);
            row["shortTest"] = await GetCachedId(split[3].Trim(), true, context);
            row["openTest"] = await GetCachedId(split[4].Trim(), true, context);
            row["icTest"] = await GetCachedId(split[5].Trim(), true, context);
            row["macroTest"] = await GetCachedId(split[6].Trim(), true, context);
            row["functionTest"] = await GetCachedId(split[7].Trim(), true, context);

            table.Rows.Add(row);
        }

        return true;
    }

    /// <summary>
    /// Processes one row of the current file that has been identified as an step result.
    /// </summary>
    /// <param name="raw">The current row.</param>
    /// <param name="context">The context required to parse an step row.</param>
    /// <param name="table">The DataTable to insert the parsed data into.</param>
    /// <returns>A value indicating whether parsing should continue.</returns>
    private static async Task<bool> ProcessStepRow(string raw, ParsingContext context, DataTable table)
    {
        // If the line is empty, skip it
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        // If we see EOT or FCT, the step results are finished
        if (raw.Contains("[EOT]"))
        {
            context.CurrentSection = null;
            return false;
        }

        if (raw.Contains(FctHeader))
        {
            context.CurrentSection = SectionType.Fct;
            return false;
        }

        string[] line = raw.Split(',');
        if (line[0].StartsWith("Gr"))
        {
            context.Data.Group = int.TryParse(line[1].Trim(), out int tt) ? tt : 0;
            return true;
        }

        if (line.Length < 13)
        {
            return true;
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
        row[BarcodeColName] = context.Data.Barcode;
        row[TestTimeColName] = context.Data.TestTime;
        row[GroupNumColName] = context.Data.Group;
        row[StepNumColName] = int.Parse(line[1].Trim());
        row[TimesTestedColName] = context.Data.TimesTested;
        row[AllResultColName] = resultId;
        row["partName"] = line[2].Trim();
        row["hPin"] = line[3].Trim();
        row["lPin"] = line[4].Trim();
        row["pos"] = line[5].Trim();
        row["mode"] = modeId;
        row["rangeNum"] = int.Parse(line[7].Trim());
        row["hLim"] = hLim.HasValue ? hLim.Value : DBNull.Value;
        row["lLim"] = lLim.HasValue ? lLim.Value : DBNull.Value;
        row[MeasUnitColName] = unit ?? (object)DBNull.Value;
        row["act"] = act.HasValue ? act.Value : DBNull.Value;
        row["ref"] = refVal.HasValue ? refVal.Value : DBNull.Value;
        row["meas"] = meas.HasValue ? meas.Value : DBNull.Value;

        table.Rows.Add(row);
        return true;
    }

    /// <summary>
    /// Processes one row of the current file that has been identified as an FCT result.
    /// </summary>
    /// <param name="raw">The current row.</param>
    /// <param name="context">The context required to parse an FCT row.</param>
    /// <param name="table">The DataTable to insert the parsed data into.</param>
    /// <returns>A value indicating whether parsing should continue.</returns>
    private static async Task<bool> ProcessFctRow(string raw, ParsingContext context, DataTable table)
    {
        if (raw.Contains("[EOT]"))
        {
            return false; // If we see EOT, the FCT section is finished
        }

        string[] line = raw.Split(',');

        // Found new group
        if (line[0].StartsWith("Gr"))
        {
            context.Data.Group = int.TryParse(line[1].Trim(), out int tt) ? tt : 0;
            return true;
        }

        if (line[0].Contains("Rslt"))
        {
            return true; // found header
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
        row[BarcodeColName] = context.Data.Barcode;
        row[TestTimeColName] = context.Data.TestTime;
        row[GroupNumColName] = context.Data.Group;
        row[StepNumColName] = int.Parse(line[1].Trim());
        row[AllResultColName] = resultId;
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
        row[MeasUnitColName] = unit ?? (object)DBNull.Value;
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
        return true;
    }

    /// <summary>
    /// Validates <paramref name="potentialFilePath"/> and (re-)prompts as necessary for the path to the target file or directory.
    /// </summary>
    /// <param name="potentialFilePath">The filename specified on the command line, if applicable.</param>
    /// <returns>A Task containing the validated file path.</returns>
    private async Task<string> PromptForFile(string? potentialFilePath)
    {
        string filePath = string.Empty;
        string? validationError = null;

        while (string.IsNullOrEmpty(filePath))
        {
            if (string.IsNullOrEmpty(potentialFilePath))
            {
                potentialFilePath = await this.input.GetFilepathAsync(new ("Please select the file(s) you wish to upload."), validationError);
            }

            validationError = null;
            if (potentialFilePath == null)
            {
                validationError = $"No file specified. Please try again.";
            }
            else if (!Path.Exists(potentialFilePath))
            {
                validationError = $"Path '{potentialFilePath}' is not a valid directory or CSV file. Please try again.";
            }
            else if (File.Exists(potentialFilePath) && !Path.GetExtension(potentialFilePath).Equals(".csv"))
            {
                validationError = $"Path '{potentialFilePath} is not a CSV. Please provide a directory or CSV file.";
            }
            else
            {
                filePath = potentialFilePath;
            }

            potentialFilePath = null;
        }

        return filePath;
    }

    /// <summary>
    /// Parses one entire Hioki log file and adds it to the DB.
    /// Gets the barcode, test datetime, and number of times tested from the header common between step and group result files,
    /// then passes the context to the appropriate handler for the rest of the file.
    /// </summary>
    /// <param name="filePath">The file to parse.</param>
    /// <param name="conn">The <see cref="SqlConnection"/> to use for this file.</param>
    /// <returns>A Task representing that the file has been parsed.</returns>
    private async Task ParseHioki(string filePath, SqlConnection conn)
    {
        await this.output.SetCurrentFile(filePath);
        await this.output.ReportProgress(ProgressEvent.FileStarted);
        int rowsUploaded = 0;

        CommonPackage package = new ();
        LogParseResult parseResult = default;

        // If there is a file-related error (like the file being open in another process), there's nothing to be done
        try
        {
            using StreamReader reader = new (filePath);

            parseResult = await this.ParseCommonHeader(reader, package, filePath);

            // If there is a parse error, roll back the transaction (i.e. file)
            using SqlTransaction transaction = (SqlTransaction)await conn.BeginTransactionAsync(); // Create the transaction to be used for this file
            try
            {
                ParsingContext context = new (reader, conn, transaction, package); // Compile everything the parser needs to know into a context object
                rowsUploaded = await this.ParseHiokiDispatchLoop(context);

                await (parseResult.Flagged ? transaction.RollbackAsync() : transaction.CommitAsync());
                await this.output.ReportProgress(ProgressEvent.FileCompleted);
            }

            // Because of the time-bound nature of test logs, when a duplicate row is encountered, the entire file is almost guaranteed to be a duplicate
            // This theoretically could be verified before attempting insertion, but the SQL server is optimized for duplicate checking, so we let it generate an exception
            catch (SqlException sqlEx) when (sqlEx.Number == 2627 || sqlEx.Number == 2601)
            {
                await transaction.RollbackAsync();
                parseResult |= new LogParseResult { alreadyUploaded = true };
                throw;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                parseResult |= new LogParseResult { hasMiscError = true };
                throw;
            }
        }
        catch (UnauthorizedAccessException)
        {
            await this.Report("Error: You do not have permission to read this file: " + filePath + "\n)", ReportLevel.ERROR);
            await this.output.ReportProgress(ProgressEvent.FileSkipped);
        }
        catch (IOException ex)
        {
            await this.Report($"I/O Error: {ex.Message}", ReportLevel.ERROR);
            await this.output.ReportProgress(ProgressEvent.FileSkipped);
            parseResult |= new LogParseResult { hasMiscError = true };
        }
        catch (Exception ex)
        {
            await this.Report($"Unexpected Error: {ex.Message}", ReportLevel.ERROR);
            await this.output.ReportProgress(ProgressEvent.FileSkipped);
            parseResult |= new LogParseResult { hasMiscError = true };
        }
        finally
        {
            this.output.BatchResults.Add(new FileResult(
                file: filePath,
                barcode: package.Barcode,
                alreadyUploaded: parseResult.AlreadyUploaded,
                hadErrors: parseResult.HasMiscError,
                rowsUploaded: rowsUploaded));
        }
    }

    /// <summary>
    /// Parses the header at the top of the file. Its data will be applied to every row.
    /// </summary>
    /// <param name="reader">The StreamReader to harvest the header information.</param>
    /// <param name="package">The CommonPackage in which to record the common data.</param>
    /// <param name="filePath">The file name (for more specific reporting).</param>
    /// <returns>A LogParseResult describing the success of the header parse.</returns>
    private async Task<LogParseResult> ParseCommonHeader(StreamReader reader, CommonPackage package, string filePath)
    {
        LogParseResult parseResult = default;

        await reader.ReadLineAsync(); // cut "[Test Results]"
        await reader.ReadLineAsync(); // cut "File: filename"

        // Parse timesTested as an int
        string? line = await reader.ReadLineAsync();
        package.TimesTested = int.TryParse(line?.Split(',')[1], out int tt) ? tt : 0;

        // if TimesTested is 0, the value found wasn't an integer, so say which file and skip it (timesTested is primary key)
        if (package.TimesTested == 0)
        {
            await this.Report($"Error reading timesTested for {filePath}\n", ReportLevel.ERROR);
            parseResult |= new LogParseResult { hasFormatError = true };
            return parseResult;
        }

        await reader.ReadLineAsync(); // cut "Lot No."

        // Parse barcode
        line = await reader.ReadLineAsync();
        package.Barcode = line?.Split(',')[1].Trim() ?? "UNKNOWN";

        // If Barcode is null, say which file, and skip it (barcode is primary key)
        if (package.Barcode.Equals("UNKNOWN"))
        {
            await this.Report($"Error reading barcode for {filePath}\n", ReportLevel.ERROR);
            parseResult |= new LogParseResult { hasFormatError = true };
            return parseResult;
        }

        // Parse testTime
        line = await reader.ReadLineAsync();
        string[]? dateParts = line?.Split(',');

        if (dateParts != null && dateParts.Length >= 3)
        {
            // concatenate date & time
            string fullDtStr = dateParts[1].Trim() + " " + dateParts[2].Trim();

            if (DateTime.TryParse(fullDtStr, CultureInfo.CurrentCulture, out DateTime dt))
            {
                package.TestTime = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, DateTimeKind.Local);
            }
            else
            {
                await this.Report($"Error: Could not parse date string '{fullDtStr}' in {filePath}\n", ReportLevel.ERROR);
                parseResult |= new LogParseResult { hasFormatError = true };
            }
        }
        else
        {
            await this.Report($"Error: Date/Time line malformed in {filePath}", ReportLevel.ERROR);
            parseResult |= new LogParseResult { hasFormatError = true };
        }

        return parseResult;
    }

    /// <summary>
    /// Handles the dispatch loop for delegating parsing to the specialized methods.
    /// </summary>
    /// <param name="context">The ParsingContext object to pass to the dispatched method.</param>
    /// <returns>The number of rows uploaded by the dispatched method.</returns>
    private async Task<int> ParseHiokiDispatchLoop(ParsingContext context)
    {
        StreamReader reader = context.Reader;
        int rowsUploaded = 0;
        string? line = await reader.ReadLineAsync(); // This will tell us whether we're dealing with group or step section
        while (line != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                line = await reader.ReadLineAsync();
                continue;
            }

            if (context.CurrentSection.Equals(SectionType.Group) || line.Contains(GroupHeader))
            {
                await reader.ReadLineAsync(); // Cut the column name row
                rowsUploaded += await this.ParseGroupSection(context);
            }
            else if (context.CurrentSection.Equals(SectionType.Step) || line.Contains(StepHeader))
            {
                await reader.ReadLineAsync(); // Cut the column name row
                rowsUploaded += await this.ParseStepSection(context);
            }
            else if (context.CurrentSection.Equals(SectionType.Fct) || line.Contains(FctHeader))
            {
                await reader.ReadLineAsync(); // Cut the column name row
                rowsUploaded += await this.ParseFctSection(context);
            }

            // In theory, there could be an FCT-specific callout for the new file format, but I don't know of that header.
            // As for now, it is silently skipped (as are any other headers that don't change the section).
            line = await reader.ReadLineAsync();
        }

        return rowsUploaded;
    }

    /// <summary>
    /// Helper function for ParseHioki (handles group files). Picks up at the beginning of the group-specific content and creates a new entry in the database for every entry in the file.
    /// </summary>
    /// <param name="context">The context required to parse a group file.</param>
    /// <returns>A Task representing that the group file has been parsed.</returns>
    private Task<int> ParseGroupSection(ParsingContext context)
        => this.ParseSectionInternal(context, SectionType.Group, ProcessGroupRow);

    /// <summary>
    /// Helper function for ParseHioki (handles step files). Picks up at the beginning of the step-specific content and creates a new entry in the database for every entry in the file.
    /// </summary>
    /// <param name="context">The context required to parse a step file.</param>
    /// <returns>A Task representing that the step file has been parsed.</returns>
    private Task<int> ParseStepSection(ParsingContext context)
        => this.ParseSectionInternal(context, SectionType.Step, ProcessStepRow);

    /// <summary>
    /// Helper function for ParseHioki (handles the FCT section). Picks up at the beginning of the FCT-specific content and creates a new entry in the database for every entry in the file.
    /// </summary>
    /// <param name="context">The context required to parse the FCT section of a step file.</param>
    /// <returns>A Task representing that the FCT section has been parsed.</returns>
    private Task<int> ParseFctSection(ParsingContext context)
        => this.ParseSectionInternal(context, SectionType.Fct, ProcessFctRow);

    /// <summary>
    /// Section parse structure and router. All three sections call this method with their individual section type and row processing delegate.
    /// This method executes <paramref name="processRow"/> on each row until it returns false, then inserts the contents of the produced DataTable to the DB table for this section.
    /// </summary>
    /// <param name="context">The context required to parse the specified <paramref name="section"/> of this file.</param>
    /// <param name="section">The section of this file to parse.</param>
    /// <param name="processRow">The method to apply to each relevant row. Returns a value indicating whether to continue parsing.</param>
    /// <returns>The number of rows successfully uploaded to the DB.</returns>
    private async Task<int> ParseSectionInternal(ParsingContext context, SectionType section, Func<string, ParsingContext, DataTable, Task<bool>> processRow)
    {
        DataTable table = CreateSectionDataTable(section);

        string? raw;
        while ((raw = await context.Reader.ReadLineAsync()) != null)
        {
            bool shouldContinue = await processRow(raw, context, table);
            if (!shouldContinue)
            {
                break;
            }
        }

        try
        {
            await BulkInsertDataTable(table, section, context);
            return table.Rows.Count;
        }
        catch (Exception ex)
        {
            await this.Report(ex.Message, ReportLevel.ERROR);
            throw; // Pass off to caller for higher handling
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
