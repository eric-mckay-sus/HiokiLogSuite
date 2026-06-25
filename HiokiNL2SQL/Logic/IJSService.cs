// <copyright file="IJSService.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace HiokiNL2SQL.Logic;

/// <summary>
/// The interface to interact with essential JavaScript interoperability code in App.razor.
/// </summary>
public interface IJSService
{
    /// <summary>
    /// Focuses the specified element.
    /// </summary>
    /// <param name="elementId">The ID of the HTML to focus.</param>
    /// <returns>A Task representing that the target element has been focused.</returns>
    Task FocusElement(string elementId);

    /// <summary>
    /// Trigger a download of the specified content in the browser.
    /// </summary>
    /// <param name="fileName">The name for the output file.</param>
    /// <param name="csvContent">The data to download as CSV.</param>
    /// <returns>A Task representing that the download has started.</returns>
    Task DownloadCsv(string fileName, string csvContent);

    /// <summary>
    /// Scroll to the specified HTML element.
    /// </summary>
    /// <param name="elementId">The ID of the HTML to scroll to.</param>
    /// <returns>A Task representing that the element has been scrolled to.</returns>
    Task ScrollToElement(string elementId);

    /// <summary>
    /// Attempts to flush any JS calls that were queued while prerendering.
    /// </summary>
    /// <returns>A Task representing that the queue has been emptied.</returns>
    Task FlushPendingAsync();
}
