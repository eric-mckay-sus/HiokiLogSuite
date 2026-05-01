// <copyright file="BlazorIO.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace InterProcessIO;

using System.Data;

/// <summary>
/// An implementation of IInputProvider to get input from Blazor using a TaskCompletionSource.
/// Register this as a service within SampleManagement/Program.cs.
/// </summary>
public class BlazorInputProvider : IInputProvider
{
    /// <summary>
    /// Controls the completion state of an input request.
    /// Blazor may control this as it sees fit without using a blocking call (thereby freezing itself bc Blazor is single-thread).
    /// </summary>
    private TaskCompletionSource<string>? inputTcs;

    /// <summary>
    /// Controls the completion state of a confirmation request.
    /// Blazor may control this as it sees fit without using a blocking call (thereby freezing itself bc Blazor is single-thread).
    /// </summary>
    private TaskCompletionSource<bool>? confirmTcs;

    /// <summary>
    /// The Blazor action to perform when GetInputAsync is called.
    /// </summary>
    public event Action<Report, string?>? OnInputRequested;

    /// <summary>
    /// The Blazor action to perform when a simple yes/no confirmation is requested
    /// </summary>
    public event Action<Report>? OnConfirmationRequested;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="prompt">The prompt to show the user.</param>
    /// <param name="previousError">The error prompting this input, if applicable.</param>
    /// <returns>A Task containing the string collected from Blazor.</returns>
    public Task<string> GetInputAsync(Report prompt, string? previousError = null)
    {
        this.inputTcs = new TaskCompletionSource<string>();
        this.OnInputRequested?.Invoke(prompt, previousError);
        return this.inputTcs.Task;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="prompt">The prompt to show the user.</param>
    /// <returns>A Task containing the confirmation (or cancellation) from Blazor.</returns>
    public Task<bool> GetConfirmAsync(Report prompt)
    {
        this.confirmTcs = new TaskCompletionSource<bool>();
        this.OnConfirmationRequested?.Invoke(prompt);
        return this.confirmTcs.Task;
    }

    /// <summary>
    /// Fills <see cref="inputTcs"/> with <paramref name="result"/>.
    /// </summary>
    /// <param name="result">The desired contents of <see cref="inputTcs"/>. </param>
    public void SetInputResult(string result) => this.inputTcs?.TrySetResult(result);

    /// <summary>
    /// Fills <see cref="confirmTcs"/> with <paramref name="result"/>.
    /// </summary>
    /// <param name="result">The desired contents of <see cref="confirmTcs"/>. </param>
    public void SetConfirmResult(bool result) => this.confirmTcs?.TrySetResult(result);
}

/// <summary>
/// An implementation of <see cref="IOutputProvider"/> that informs a Blazor page to show the new data.
/// </summary>
public class BlazorReporter : IOutputProvider
{
    /// <summary>
    /// Notify the UI to re-render. The Blazor page must bind its StateHasChanged method to this Action.
    /// </summary>
    public event Action? OnNotify;

    /// <summary>
    /// Gets the name of the file currently being processed.
    /// </summary>
    public string CurrentFileName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets or sets the results for each file in this batch.
    /// </summary>
    public IList<FileResult> BatchResults { get; set; } = [];

    /// <summary>
    /// Gets the list of <see cref="Report"/> objects that document a warning or error.
    /// </summary>
    public IList<Report> Logs { get; private set; } = [];

    /// <summary>
    /// Gets or sets the underlying DataTable object that stores the preview information.
    /// It is important that Blazor persists this so it has concrete data during another upload.
    /// </summary>
    public DataTable? CurrentPreview { get; set; }

    /// <summary>
    /// Gets the <see cref="BatchProgress"/> object representing the progress of this batch.
    /// </summary>
    public BatchProgress? Progress { get; private set; }

    /// <summary>
    /// Sets <see cref="CurrentFileName"/> to <paramref name="name"/>, then notifies Blazor.
    /// </summary>
    /// <param name="name">The name to assign to <see cref="CurrentFileName"/>.</param>
    /// <returns><inheritdoc/></returns>
    public Task SetCurrentFile(string name)
    {
        this.CurrentFileName = name;
        this.OnNotify?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Initializes the <see cref="Progress"/> property of this batch.
    /// </summary>
    /// <param name="totalFiles">The number of files in this batch.</param>
    public void InitializeProgress(int totalFiles)
{
    this.Progress = new BatchProgress { TotalFiles = totalFiles };
    this.OnNotify?.Invoke();
}

    /// <summary>
    /// Clears the logs so old data does not persist.
    /// </summary>
    public void ClearLogs()
    {
        this.Logs = [];
        this.CurrentPreview = null;
        this.OnNotify?.Invoke();
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="report">The <see cref="Report"/> object to pass to Blazor.</param>
    /// <returns>A Task denoting that Blazor has received and displayed the message.</returns>
    public async Task ReportAsync(Report report)
    {
        // Only log errors to the Blazor interface
        if ((report.level == ReportLevel.WARNING || report.level == ReportLevel.ERROR) && !report.message.Contains("Please try again"))
        {
            this.Logs.Add(report);
        }

        this.OnNotify?.Invoke();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Reports a progress update adding to the <see cref="Progress"/> object, then firing <see cref="OnNotify"/>, to which the Blazor page has subscribed.
    /// </summary>
    /// <param name="ev"><inheritdoc/></param>
    /// <returns>A Task representing that the Blazor page has received the message.</returns>
    public Task ReportProgress(ProgressEvent ev)
    {
        if (this.Progress == null)
        {
            return Task.CompletedTask;
        }

        switch (ev)
        {
            case ProgressEvent.FileStarted:
                this.Progress.CurrentFileName = this.CurrentFileName ?? string.Empty;
                this.Progress.CurrentFilePass = 1;
                this.Progress.IsRepeating = false;
                break;
            case ProgressEvent.FileRepeated:
                this.Progress.CurrentFilePass++;
                this.Progress.IsRepeating = true;
                break;
            case ProgressEvent.FileSkipped: // by falling through the FileSkipped case, we assign it the same progress effect as FileCompleted.
            case ProgressEvent.FileCompleted:
                this.Progress.FilesCompleted++;
                this.Progress.IsRepeating = false;
                break;
            case ProgressEvent.UploadComplete:
                this.Progress.FilesCompleted = this.Progress.TotalFiles;
                break;
        }

        this.OnNotify?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="dt">The DataTable to display.</param>
    /// <returns>A Task denoting that Blazor has displayed the preview.</returns>
    public async Task ShowPreview(DataTable dt)
    {
        this.CurrentPreview = dt;
        this.OnNotify?.Invoke();
        await Task.CompletedTask;
    }
}
