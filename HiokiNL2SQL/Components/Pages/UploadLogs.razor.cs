// <copyright file="UploadLogs.razor.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace HiokiNL2SQL.Components.Pages;

using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components;

using HiokiParser;
using InterProcessIO;

/// <summary>
/// Code-behind for the CMMS mapping import page.
/// </summary>
public partial class UploadLogs : IDisposable
{
    private IList<IBrowserFile> selectedFiles = [];
    private bool isDragging = false;
    private bool isUploading = false;
    private string? validationError;

    /// <summary>
    /// Gets or sets this upload page's input provider.
    /// </summary>
    [Inject]
    public BlazorInputProvider InputProvider { get; set; } = default!;

    /// <summary>
    /// Gets or sets this upload page's output provider.
    /// </summary>
    [Inject]
    public BlazorReporter Reporter { get; set; } = default!;

    private bool ConfirmationOpen => this.selectedFiles.Count != 0 && this.validationError == null && !this.isUploading;

    /// <summary>
    /// Gets the path of the uploads folder for this session.
    /// </summary>
    private string UploadsFolderPath { get; } = Path.Combine(Path.GetTempPath(), "uploads", Guid.NewGuid().ToString());

    /// <summary>
    /// Signature and pattern in order to implement IDisposable.
    /// Note: GC stands for garbage collector, which internally calls Dispose(false). By calling Dispose(true) here, we effectively circumvent that with the manual disposal.
    /// </summary>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// When this component unloads, unsubscribe from the I/O provider events.
    /// </summary>
    /// <param name="disposing">Whether to actually dispose. This is a help for the garbage collector.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.InputProvider.OnInputRequested -= this.HandleInputRequested;
            this.InputProvider.OnFileRequested -= this.HandleFileRequested;
            this.InputProvider.OnConfirmationRequested -= this.HandleConfirmationRequested;
            this.Reporter.OnNotify -= this.HandleReporterChanged;
        }
    }

    /// <summary>
    /// When this page loads, set the sort information, bind to the input provider, and get the last upload date.
    /// </summary>
    /// <returns>A Task representing that the page has initialized.</returns>
    protected override async Task OnInitializedAsync()
    {
        this.InputProvider.OnInputRequested -= this.HandleInputRequested;
        this.InputProvider.OnInputRequested += this.HandleInputRequested;
        this.InputProvider.OnFileRequested -= this.HandleFileRequested;
        this.InputProvider.OnFileRequested += this.HandleFileRequested;
        this.InputProvider.OnConfirmationRequested -= this.HandleConfirmationRequested;
        this.InputProvider.OnConfirmationRequested += this.HandleConfirmationRequested;

        this.Reporter.OnNotify -= this.HandleReporterChanged;
        this.Reporter.OnNotify += this.HandleReporterChanged;
    }

    /// <summary>
    /// Validates the selected file before showing the confirmation panel.
    /// Sets _validationError and clears _selectedFile on failure so only valid files reach confirmation.
    /// </summary>
    private void HandleFileChanged(InputFileChangeEventArgs e)
    {
        this.validationError = null;
        this.selectedFiles = [];

        // Support multiple files securely
        IReadOnlyList<IBrowserFile> files = e.GetMultipleFiles(maximumFileCount: 2000);

        foreach (IBrowserFile file in files)
        {
            string extension = Path.GetExtension(file.Name);

            if (string.IsNullOrWhiteSpace(extension) || !extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                this.validationError = $"\"{file.Name}\" is not a CSV file. Please select files with a .csv extension only.";
                this.selectedFiles.Clear(); // Invalidate the whole batch if one is bad
                return;
            }

            this.selectedFiles.Add(file);
        }
    }

    /// <summary>
    /// Cancels selection by setting the file and error message to null.
    /// </summary>
    private void CancelSelection()
    {
        this.selectedFiles = [];
        this.validationError = null;
    }

    /// <summary>
    /// When the page requests string input, show the prompt (input area shown page-side).
    /// </summary>
    /// <param name="prompt">The prompt requiring string input.</param>
    /// <param name="previousError">The error from the previous prompt, if applicable.</param>
    private void HandleInputRequested(Report prompt, string? previousError)
    {
        this.validationError = string.IsNullOrWhiteSpace(previousError) ? prompt.message : $"{previousError}\n{prompt.message}";
        this.InvokeAsync(this.StateHasChanged);
    }

    /// <summary>
    /// When the page requests file input, show the prompt and error message (file passing handled separately).
    /// </summary>
    /// <param name="prompt">The prompt requiring file input.</param>
    /// <param name="previousError">The error message that caused this file prompt, if applicable.</param>
    private void HandleFileRequested(Report prompt, string? previousError)
    {
        this.validationError = string.IsNullOrWhiteSpace(previousError)
            ? prompt.message
            : $"{previousError}\n{prompt.message}";

        this.InvokeAsync(this.StateHasChanged);
    }

    /// <summary>
    /// When the page requests confirmation, approve automatically when already uploading (page already confirmed).
    /// </summary>
    /// <param name="prompt">The prompt to be confirmed.</param>
    private void HandleConfirmationRequested(Report prompt)
    {
        _ = prompt;

        if (this.isUploading)
        {
            this.InputProvider.SetConfirmResult(true);
            return;
        }

        this.InvokeAsync(this.StateHasChanged);
    }

    /// <summary>
    /// Forces the component to re-render when the reporter signals a progress or state update.
    /// </summary>
    private void HandleReporterChanged()
    {
        this.InvokeAsync(this.StateHasChanged);
    }

    /// <summary>
    /// Downloads <see cref="selectedFiles"/> at <see cref="UploadsFolderPath"/> and sets it as the file task completion source using <see cref="BlazorInputProvider.SetFileResult"/>.
    /// </summary>
    /// <returns>A Task representing that the file was downloaded and passed off successfully.</returns>
    private async Task<bool> SaveSelectedFiles()
    {
        Directory.CreateDirectory(this.UploadsFolderPath);
        this.Reporter.BatchResults.Clear(); // if this is the second upload in the run, clear the last batch

        foreach (IBrowserFile file in this.selectedFiles)
        {
            string trustedFileName = $"{Path.GetFileNameWithoutExtension(file.Name)}_{DateTime.Now:yyyy-MM-dd}{Path.GetExtension(file.Name)}";
            string filePath = Path.Combine(this.UploadsFolderPath, trustedFileName);

            // Stream the file data from the element to the server (must use block using statement to close stream before the uploader tries to create a new one)
            using (FileStream stream = new (filePath, FileMode.Create))
            {
                await file.OpenReadStream(1024 * 100).CopyToAsync(stream); // max 100 KB per file
            }
        }

        if (!Directory.Exists(this.UploadsFolderPath) || Directory.GetFiles(this.UploadsFolderPath, "*.csv").Length == 0)
        {
            this.validationError = "Server Error: Staging folder was created, but no valid CSV files were written to disk.";
            return false;
        }

        this.InputProvider.SetFileResult(this.UploadsFolderPath);
        this.validationError = null;
        return true;
    }

    /// <summary>
    /// When a file is confirmed, download it to the server (from the browser), then upload to the DB.
    /// </summary>
    /// <returns>A Task representing that the file was confirmed and an upload was attempted.</returns>
    private async Task HandleFileConfirmation()
    {
        this.isUploading = true;
        this.validationError = null;

        try
        {
            this.Reporter.ClearLogs();
            this.Reporter.InitializeProgress(this.selectedFiles.Count);

            bool filesReady = await this.SaveSelectedFiles(); // actually get the file (and pass to uploader via input provider)
            if (!filesReady)
            {
                return;
            }

            LogParserCore uploader = new (this.InputProvider, this.Reporter);
            await uploader.ExecuteAsync(this.UploadsFolderPath);
        }
        finally
        {
            if (Directory.Exists(this.UploadsFolderPath))
            {
                Directory.Delete(this.UploadsFolderPath, recursive: true);
            }

            this.selectedFiles = [];
            this.isUploading = false;
        }
    }

    private void HandleDragEnter() => this.isDragging = true;

    private void HandleDragOver() => this.isDragging = true;

    private void HandleDragLeave() => this.isDragging = false;

    private void HandleDrop() => this.isDragging = false;
}
