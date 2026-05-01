// <copyright file="ConsoleExtensions.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>
namespace InterProcessIO;

/// <summary>
/// Holds all extension methods applicable to reports to the console.
/// </summary>
public static class ReportConsoleExtensions
{
    /// <summary>
    /// Extension method to provide ANSI string formatting for Report objects specifically for Console-based providers.
    /// Creates a new message string which integrates report level by appending an ANSI escape code to change the color of the console text.
    /// </summary>
    /// <param name="report">The Report record for which to generate a string.</param>
    /// <returns>A new string enclosed in the appropriate ANSI codes for terminal rendering.</returns>
    public static string ToAnsiString(this Report report)
    {
        string colorCode = report.level switch
        {
            ReportLevel.ERROR => "\u001b[31m", // Red
            ReportLevel.SUCCESS => "\u001b[32m", // Green
            ReportLevel.WARNING => "\u001b[33m", // Yellow
            ReportLevel.IMPORTANT => "\u001b[36m", // Cyan
            _ => "\u001b[37m" // White
        };
        const string resetCode = "\u001b[0m";
        return $"{colorCode}{report.message}{resetCode}";
    }
}
