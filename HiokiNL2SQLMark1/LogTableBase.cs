using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// This abstract class compiles the similar methods used between all tables
/// </summary>
/// <typeparam name="T">An implementation of IHiokiLog (defined in LogDbContext)</typeparam>
public abstract class LogTableBase<T> : ComponentBase where T : class, IHiokiLog
{
    protected LogTableLogic<T> Logic { get; set; } = default!; // Where all the logic lives
    [Inject] public IDbContextFactory<LogDbContext> DbFactory { get; set; } = default!; // creates a new LogDbContext when necessary
    protected List<T> DataView => Logic.DataView; // Pass through the table representation from LogTableLogic

    protected override void OnInitialized()
    {
        Logic = new LogTableLogic<T>(DbFactory, GetBaseQuery);
    }

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

    /// <summary>
    /// Gets the context to determine what table and attributes to check against
    /// </summary>
    /// <returns>A queryable object that implements IHiokiLog</returns>
    protected abstract IQueryable<T> GetBaseQuery(LogDbContext db);
}