using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using HiokiNL2SQLMark1.Logic;
using JS = Microsoft.JSInterop.IJSRuntime;

namespace HiokiNL2SQLMark1;
/// <summary>
/// This abstract class forms the interface between a page and LogTableLogic
/// </summary>
/// <typeparam name="T">An implementation of IHiokiLog (defined in LogDbContext)</typeparam>
public abstract class LogTableBase<T> : ComponentBase where T : class, IHiokiLog
{
    protected LogTableLogic<T> Logic { get; set; } = default!; // Where all the logic lives. The particular instance of LogTableLogic is determined by the page
    protected List<T> DataView => Logic.DataView; // Pass through the table representation from LogTableLogic

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
    /// Jumps to the specified new page
    /// </summary>
    /// <param name="newPage">The page number to jump to</param>
    /// <returns></returns>
    public async Task ChangePage(int newPage) => await Logic.ChangePage(newPage);

    /// <summary>
    /// Helper to render the arrow
    /// </summary>
    /// <param name="column">The column for which to update the sort icon</param>
    /// <returns>The Unicode arrow representing the sort direction</returns>
    public string GetSortIcon(string column) => Logic.GetSortIcon(column);

    /// <summary>
    /// Cycles through sort directions when column is toggled
    /// Cycle order: None -> Asc -> Desc
    /// </summary>
    /// <param name="column">The column to be toggled</param>
    /// <returns></returns>
    public async Task ToggleSort(string column) => await Logic.ToggleSort(column);

    /// <summary>
    /// Clears all filters on a query
    /// </summary>
    /// <returns></returns>
    public virtual async Task ClearFilters() => await Logic.ClearFilters();
}