using Microsoft.AspNetCore.Components;
using HiokiNL2SQLMark1.Logic;

namespace HiokiNL2SQLMark1;
/// <summary>
/// This abstract class forms the interface between a page and LogTableLogic
/// </summary>
/// <typeparam name="T">An implementation of IHiokiLog (defined in LogDbContext)</typeparam>
public abstract class LogTableBase<T> : ComponentBase where T : class, IHiokiLog
{
    protected LogTableLogic<T> Logic { get; set; } = default!; // Where all the logic lives. The particular instance of LogTableLogic is determined by the page

    protected override async Task OnInitializedAsync()
    {
        // Logic is instantiated in the synchronous OnInitialized()
        // Now we run the async setup
        await Logic.InitializeCaches();
        await RefreshData(); 
    }

    /// <summary>
    /// Calls Logic.RefreshData to update table, then tells the child the state has changed
    /// </summary>
    /// <param name="keepPage">Whether to keep the current page</param>
    /// <returns></returns>
    protected async Task RefreshData(bool keepPage = false)
    {
        await Logic.RefreshData(keepPage);
        StateHasChanged();
    } 

    /// <summary>
    /// Clears all filters on a query
    /// </summary>
    /// <returns></returns>
    public virtual async Task ClearFilters() {
        await Logic.ClearFilters();
        StateHasChanged();
    }
}