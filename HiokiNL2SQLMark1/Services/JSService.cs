using HiokiNL2SQLMark1.Logic;
using Microsoft.JSInterop;

namespace HiokiNL2SQLMark1.Services;

public class JSService(IJSRuntime js) : IJSService
{
    public async Task FocusElement(string elementId)
    {
        await js.InvokeVoidAsync("focusElement", elementId);
    }

    public async Task DownloadCsv(string fileName, string csvContent)
    {
        await js.InvokeVoidAsync("downloadFileFromStream", fileName, csvContent);
    }
}