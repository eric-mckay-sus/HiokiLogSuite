// <copyright file="FluidIO.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>
namespace InterProcessIO;

using System.Data;

/// <summary>
/// Represents a report's metadata.
/// </summary>
public enum ReportLevel
{
    /// <summary>
    /// The default report
    /// </summary>
    INFO,

    /// <summary>
    /// A report with higher importance than INFO, but no special meaning
    /// </summary>
    IMPORTANT,

    /// <summary>
    /// A report representing successful completion
    /// </summary>
    SUCCESS,

    /// <summary>
    /// A report denoting something has failed, but will try again/fall back on a default
    /// </summary>
    WARNING,

    /// <summary>
    /// A report denoting something has failed (without retry)
    /// </summary>
    ERROR,
}

/// <summary>
/// Defines a 'checkpoint' event to be watched by any output source that wishes to track progress.
/// </summary>
public enum ProgressEvent
{
    /// <summary>
    /// The event representing when a new file has started.
    /// </summary>
    FileStarted,

    /// <summary>
    /// The event representing when a file has been skipped.
    /// </summary>
    FileSkipped,

    /// <summary>
    /// The event representing when a file has been repeated.
    /// </summary>
    FileRepeated,

    /// <summary>
    /// The event representing when a file has been completeted (after 'repeat this file?' has been denied).
    /// </summary>
    FileCompleted,

    /// <summary>
    /// The event representing when the entire upload is complete.
    /// </summary>
    UploadComplete,
}

/// <summary>
/// Denotes the status with which an upload finished.
/// </summary>
public enum UploadResult
{
    /// <summary>
    /// The upload finished without any errors.
    /// </summary>
    Complete,

    /// <summary>
    /// The upload ran to completion with errors.
    /// </summary>
    CompleteWithErrors,

    /// <summary>
    /// The upload was canceled by the process because of a fatal error.
    /// </summary>
    ErroredOut,

    /// <summary>
    /// The upload was canceled by the user.
    /// </summary>
    Canceled,
}

/// <summary>
/// The data packet used by I/O classes to track a file with its upload status
/// </summary>
/// <param name="file">The file containing the model.</param>
/// <param name="model">The model name (from C. Core).</param>
/// <param name="alreadyUploaded">Whether these contents were already uploaded under this model name (so the file was detected as a duplicate)</param>
/// <param name="hadDuplicates">Whether the model upload encountered duplicates.</param>
/// <param name="hadErrors">Whether the model upload encountered other errors.</param>
/// <param name="rowsUploaded">The number of rows uploaded for this model.</param>
public record FileResult(string file, string model, bool alreadyUploaded, bool hadDuplicates, bool hadErrors, int rowsUploaded);

/// <summary>
/// Communicates the current state of a batch upload to the Blazor layer.
/// </summary>
public record BatchProgress
{
    /// <summary>
    /// Gets or sets the number of files in this batch.
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// Gets or sets the number of files that have been fully processed in this batch.
    /// </summary>
    public int FilesCompleted { get; set; }

    /// <summary>
    /// Gets or sets the name of the current file.
    /// </summary>
    public string CurrentFileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of times this file has run (including this run, 1-based).
    /// </summary>
    public int CurrentFilePass { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this file pass is a repeat.
    /// </summary>
    public bool IsRepeating { get; set; }
}

/// <summary>
/// The data packet used by I/O classes to associate a report level with the report message.
/// Different input/output providers can format differently based on the report level.
/// </summary>
/// <param name="message">The base message</param>
/// <param name="level">The message's metadata as a report level</param>
public record Report(string message, ReportLevel level = ReportLevel.INFO);

/// <summary>
/// Defines thread-safe asynchronous input redirection.
/// </summary>
public interface IInputProvider
{
    /// <summary>
    /// Prompts and awaits user input.
    /// </summary>
    /// <param name="prompt">The prompt requiring user input.</param>
    /// <param name="previousError">The previous error that prompted this input, if applicable.</param>
    /// <returns>A Task containing the user's input.</returns>
    Task<string> GetInputAsync(Report prompt, string? previousError = null);

    /// <summary>
    /// Prompts and awaits user input for a yes/no question.
    /// Like <see cref="GetInputAsync"/>, but returns a boolean instead of a string.
    /// Recommend calling <see cref="GetInputAsync"/> internally.
    /// </summary>
    /// <param name="prompt">The prompt requiring confirmation.</param>
    /// <returns>A Task containing a boolean representing whether the prompt was confirmed.</returns>
    Task<bool> GetConfirmAsync(Report prompt);
}

/// <summary>
/// Defines thread-safe asynchronous output redirection for Report records (custom IProgress).
/// </summary>
public interface IOutputProvider
{
    /// <summary>
    /// Gets the name of the file currently being processed.
    /// </summary>
    public string? CurrentFileName { get; }

    /// <summary>
    /// Gets or sets the results of this batch.
    /// </summary>
    public IList<FileResult> BatchResults { get; set; }

    /// <summary>
    /// Sets <see cref="CurrentFileName"/> to <paramref name="name"/>.
    /// Distinct from the <see cref="CurrentFileName"/> setter property to allow only setting it privately.
    /// </summary>
    /// <param name="name">The new current file name.</param>
    /// <returns>A Task representing that the file name has been changed.</returns>
    Task SetCurrentFile(string name);

    /// <summary>
    /// Asynchronously displays a report to the output.
    /// </summary>
    /// <param name="report">The <see cref="Report"/> record to be displayed.</param>
    /// <returns>A task signaling that the output has finished reporting.</returns>
    Task ReportAsync(Report report);

    /// <summary>
    /// Asynchronously reports progress to the output.
    /// </summary>
    /// <param name="ev">The <see cref="ProgressEvent"/> to report.</param>
    /// <returns>A Task representing that the output has received the progress report.</returns>
    Task ReportProgress(ProgressEvent ev);

    /// <summary>
    /// Asynchronously displays <paramref name="dt"/> to the output.
    /// </summary>
    /// <param name="dt">The DataTable to display.</param>
    /// <returns>A Task representing the completion of the method.</returns>
    Task ShowPreview(DataTable dt);
}
