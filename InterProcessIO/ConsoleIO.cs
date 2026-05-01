// <copyright file="ConsoleIO.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace InterProcessIO;

using System.Data;
using StringBuilder = System.Text.StringBuilder;

/// <summary>
/// An implementation of IInputProvider to get input from the console (works kind of like Python's native input() method).
/// </summary>
public class ConsoleInputProvider : IInputProvider
{
    /// <summary>
    /// <inheritdoc/> Uses standard console methods suitable for a CLI.
    /// </summary>
    /// <param name="prompt"><inheritdoc/></param>
    /// <param name="previousError">Unused in this implementation.</param>
    /// <returns>A Task containing the command line input.</returns>
    public async Task<string> GetInputAsync(Report prompt, string? previousError = null)
    {
        Console.WriteLine(prompt.ToAnsiString());
        Console.Write('\t');
        return await Task.Run(() => Console.ReadLine() ?? string.Empty);
    }

    /// <summary>
    /// <inheritdoc/>
    /// Uses standard console methods suitable for a CLI.
    /// Always appends (y/n) and checks for 'yes' or 'y'.
    /// </summary>
    /// <param name="prompt"><inheritdoc/></param>
    /// <returns>A Task-wrapped boolean representing the whether the user confirmed.</returns>
    public async Task<bool> GetConfirmAsync(Report prompt)
    {
        Console.Write($"{prompt.ToAnsiString()} (y/n)");
        string response = (await this.GetInputAsync(new (string.Empty))).Trim().ToLower();
        return response == "y" || response == "yes";
    }
}

/// <summary>
/// An implementation of IReportOutputProvider that prints to the console.
/// </summary>
public class ConsoleReporter : IOutputProvider
{
    /// <summary>
    /// Gets the name of the file currently being processed.
    /// </summary>
    public string CurrentFileName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets or sets the results for each file in this batch.
    /// </summary>
    public IList<FileResult> BatchResults { get; set; } = [];

    /// <summary>
    /// Sets <see cref="CurrentFileName"/> to <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The name to assign to <see cref="CurrentFileName"/>.</param>
    /// <returns><inheritdoc/></returns>
    public async Task SetCurrentFile(string name)
    {
        this.CurrentFileName = name;
        await Task.CompletedTask;
    }

    /// <summary>
    /// <inheritdoc/>
    /// In this case, the output is the console, so we just use Console.Write.
    /// </summary>
    /// <param name="report"><inheritdoc/></param>
    /// <returns>A Task representing that the console has finished printing.</returns>
    public async Task ReportAsync(Report report)
    {
        Console.Write(report.ToAnsiString());
        await Task.CompletedTask;
    }

    /// <summary>
    /// Asynchronously reports progress to the console.
    /// At the moment, this is not used, it simply implements the interface.
    /// </summary>
    /// <param name="ev"><inheritdoc/></param>
    /// <returns>A Task representing that the console has reported progress.</returns>
    public async Task ReportProgress(ProgressEvent ev) => await Task.CompletedTask;

    /// <summary>
    /// Prints the contents of <paramref name="dt"/> to the console.
    /// </summary>
    /// <param name="dt"><inheritdoc/></param>
    /// <returns>A Task representing that the preview has been shown.</returns>
    public async Task ShowPreview(DataTable dt)
    {
        // If there's no content to print, tell the user and exit
        if (dt.Rows.Count == 0)
        {
            await this.ReportAsync(new ("\tNo rows with valid data (under current filters).\n", ReportLevel.WARNING));
            return;
        }

        StringBuilder sb = new ();

        sb.Append(new Report($"\t--- UPLOAD SUMMARY: {dt.Rows.Count} ROWS PROCESSED ---\n\t", ReportLevel.SUCCESS).ToAnsiString());

        // Define column widths for the ASCII table
        int modelWidth = 15;
        int modeWidth = 45;
        int locWidth = 15;
        int dummyWidth = 5;

        // Print table header
        string header = $"| {"Model".PadRight(modelWidth)} | {"Failure Mode".PadRight(modeWidth)} | {"Loc".PadRight(locWidth)} | {"Dummy #".PadRight(dummyWidth)} |\n\t";
        string divider = $"{new ('-', header.Length)}\n\t";

        sb.Append(divider);
        sb.Append(header);
        sb.Append(divider);

        // Print each row from the DataTable
        foreach (DataRow row in dt.Rows)
        {
            string modelStr = row["model"]?.ToString()?.Length > modelWidth
                ? string.Concat(row["model"].ToString().AsSpan(0, modelWidth - 3), "...")
                : row["model"]?.ToString() ?? string.Empty;

            string modeStr = row["failureMode"]?.ToString()?.Length > modeWidth
                ? string.Concat(row["failureMode"].ToString().AsSpan(0, modeWidth - 3), "...")
                : row["failureMode"]?.ToString() ?? string.Empty;

            string locStr = row["location"]?.ToString()?.Length > locWidth
                ? string.Concat(row["location"].ToString().AsSpan(0, locWidth - 3), "...")
                : row["location"]?.ToString() ?? string.Empty;

            string dummyStr = row["dummySampleNum"]?.ToString() ?? string.Empty;

            string line = $"| {modelStr.PadRight(modelWidth)} | {modeStr.PadRight(modeWidth)} | {locStr.PadRight(locWidth)} | {dummyStr.PadRight(dummyWidth)} |\n\t";
            sb.Append(new Report(line).ToAnsiString());
        }

        sb.Append(divider.TrimEnd('\t'));

        await this.ReportAsync(new (sb.ToString()));
    }
}
