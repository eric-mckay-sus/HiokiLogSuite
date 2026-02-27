namespace HiokiNL2SQLMark1.Logic;

public interface IJSService
{
    /// <summary>
    /// Uses IJSRuntime to focus the specified element
    /// </summary>
    /// <param name="elementId">The ID of the HTML to focus</param>
    /// <returns></returns>
    Task FocusElement(string elementId);

    /// <summary>
    /// Trigger a download of the specified content in the browser
    /// </summary>
    /// <param name="fileName">The name for the output file</param>
    /// <param name="csvContent">The data to download as CSV</param>
    /// <returns></returns>
    Task DownloadCsv(string fileName, string csvContent);

    /// <summary>
    /// Scroll to the specified HTML element
    /// </summary>
    /// <param name="elementId">The ID of the HTML to scroll to</param>
    /// <returns></returns>
    Task ScrollToElement(string elementId);

    /// <summary>
    /// Attempts to flush any JS calls that were queued while prerendering.
    /// </summary>
    Task FlushPendingAsync();
}