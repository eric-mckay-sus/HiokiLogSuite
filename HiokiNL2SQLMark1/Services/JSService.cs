using HiokiNL2SQLMark1.Logic;
using Microsoft.JSInterop;

namespace HiokiNL2SQLMark1.Services;

using System.Collections.Concurrent;

public class JSService(IJSRuntime js) : IJSService
{
    private readonly IJSRuntime _js = js;
    private readonly ConcurrentQueue<Func<Task>> _pending = new();

    public async Task FocusElement(string elementId)
    {
        try
        {
            await _js.InvokeVoidAsync("focusElement", elementId);
        }
        catch (InvalidOperationException)
        {
            // Prerendering: queue the call for later
            _pending.Enqueue(() => _js.InvokeVoidAsync("focusElement", elementId).AsTask());
        }
    }

    public async Task DownloadCsv(string fileName, string csvContent)
    {
        try
        {
            await _js.InvokeVoidAsync("downloadFileFromStream", fileName, csvContent);
        }
        catch (InvalidOperationException)
        {
            // Prerendering: queue the call for later
            _pending.Enqueue(() => _js.InvokeVoidAsync("downloadFileFromStream", fileName, csvContent).AsTask());
        }
    }

    public async Task ScrollToElement(string elementId)
    {
        try
        {
            await _js.InvokeVoidAsync("scrollToElement", elementId);
        }
        catch (InvalidOperationException)
        {
            // Prerendering: queue the call for later
            _pending.Enqueue(() => _js.InvokeVoidAsync("scrollToElement", elementId).AsTask());
        }
    }

    public async Task FlushPendingAsync()
    {
        while (_pending.TryDequeue(out var work))
        {
            try
            {
                await work();
            }
            catch (InvalidOperationException)
            {
                // If still not ready, re-enqueue and stop flushing
                _pending.Enqueue(work);
                break;
            }
            catch
            {
                // Swallow other JS errors to avoid disrupting rendering
            }
        }
    }
}