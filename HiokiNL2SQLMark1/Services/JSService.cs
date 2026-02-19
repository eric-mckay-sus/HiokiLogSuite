using HiokiNL2SQLMark1.Logic;
using Microsoft.JSInterop;

namespace HiokiNL2SQLMark1.Services;

public class JSService(IJSRuntime js) : IJSService
{
    /// <summary>
    /// Uses IJSRuntime to focus the specified element
    /// </summary>
    /// <param name="elementId">The ID of the HTML to focus</param>
    /// <returns></returns>
    public async Task FocusElement(string elementId)
    {
        await js.InvokeVoidAsync("focusElement", elementId);
    }

    /// <summary>
    /// Trigger a download of the specified content in the browser
    /// </summary>
    /// <param name="fileName">The name for the output file</param>
    /// <param name="csvContent">The data to download as CSV</param>
    /// <returns></returns>
    public async Task DownloadCsv(string fileName, string csvContent)
    {
        await js.InvokeVoidAsync("downloadFileFromStream", fileName, csvContent);
    }
}