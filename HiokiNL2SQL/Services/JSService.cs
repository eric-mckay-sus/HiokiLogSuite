// <copyright file="JSService.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace HiokiNL2SQL.Services;

using Microsoft.JSInterop;
using System.Collections.Concurrent;

using HiokiNL2SQL.Logic;

/// <summary>
/// An implementation of <see cref="IJSService"/> using IJSRuntime to access App.razor JS methods for element focus, browser downloads, and auto-scrolling.
/// Uses a ConcurrentQueue for processing JS operations when the element they access is not available (e.g. prerendering).
/// </summary>
/// <param name="ijsr">The IJSRuntime to use for this <see cref="JSService"/>.</param>
public class JSService(IJSRuntime ijsr) : IJSService
{
    private readonly IJSRuntime js = ijsr;
    private readonly ConcurrentQueue<Func<Task>> pending = new ();

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="elementId">The ID of the element to focus.</param>
    /// <returns>A Task representing that the element has been focused.</returns>
    public async Task FocusElement(string elementId)
    {
        try
        {
            await this.js.InvokeVoidAsync("focusElement", elementId);
        }
        catch (InvalidOperationException)
        {
            // Prerendering: queue the call for later
            this.pending.Enqueue(() => this.js.InvokeVoidAsync("focusElement", elementId).AsTask());
        }
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="fileName">The name for the new CSV.</param>
    /// <param name="csvContent">The contents of the CSV to be downloaded.</param>
    /// <returns>A Task representing that the browser download has started.</returns>
    public async Task DownloadCsv(string fileName, string csvContent)
    {
        try
        {
            await this.js.InvokeVoidAsync("downloadFileFromStream", fileName, csvContent);
        }
        catch (InvalidOperationException)
        {
            // Prerendering: queue the call for later
            this.pending.Enqueue(() => this.js.InvokeVoidAsync("downloadFileFromStream", fileName, csvContent).AsTask());
        }
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="elementId">The ID of the element to scroll to.</param>
    /// <returns>A Task representing that the scroll is complete.</returns>
    public async Task ScrollToElement(string elementId)
    {
        try
        {
            await this.js.InvokeVoidAsync("scrollToElement", elementId);
        }
        catch (InvalidOperationException)
        {
            // Prerendering: queue the call for later
            this.pending.Enqueue(() => this.js.InvokeVoidAsync("scrollToElement", elementId).AsTask());
        }
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <returns>A Task representing that the JS calls have been flushed.</returns>
    public async Task FlushPendingAsync()
    {
        while (this.pending.TryDequeue(out Func<Task>? work))
        {
            try
            {
                await work();
            }
            catch (InvalidOperationException)
            {
                // If still not ready, re-enqueue and stop flushing
                this.pending.Enqueue(work);
                break;
            }
            catch
            {
                // Swallow other JS errors to avoid disrupting rendering
            }
        }
    }
}
