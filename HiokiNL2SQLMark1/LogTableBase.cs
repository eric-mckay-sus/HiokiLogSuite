using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

/// <summary>
/// This abstract class compiles the similar methods used between all tables
/// </summary>
/// <typeparam name="T">An implementation of IHiokiLog (defined in LogDbContext)</typeparam>
public abstract class LogTableBase<T> : ComponentBase where T : class, IHiokiLog
{
    protected LogTableLogic<T> Logic { get; set; } = default!; // Where all the logic lives
    [Inject] public IDbContextFactory<LogDbContext> DbFactory { get; set; } = default!; // creates a new LogDbContext when necessary
    protected List<T> DataView => Logic.DataView;

    protected override void OnInitialized()
    {
        Logic = new LogTableLogic<T>(DbFactory, (db) => GetBaseQuery(db));
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
    /// Maps a dictionary of filter key-value pairs to the individual filter properties
    /// </summary>
    /// <param name="filterDict">Dictionary containing filter keys and values</param>
    /// <returns></returns>
    public async Task ApplyFiltersFromDictionary(Dictionary<string, string> filterDict) => await Logic.ApplyFiltersFromDictionary(filterDict);

    /// <summary>
    /// Applies the five filters common between all three pages
    /// </summary>
    /// <param name="query">The query to which the filters should be appended</param>
    /// <returns>An IQueryable object with filters applied</returns>
    protected virtual IQueryable<T> ApplyFilters(IQueryable<T> query) => Logic.ApplyFilters(query);

    /// <summary>
    /// Applies type-specific filters from dictionary. Override in subclasses for table-specific filters.
    /// </summary>
    /// <param name="filterDict">Dictionary of remaining filters to apply</param>
    /// <returns></returns>
    protected virtual Task ApplyTypeSpecificFiltersFromDictionary(Dictionary<string, string> filterDict) => Logic.ApplyTypeSpecificFiltersFromDictionary(filterDict);

    /// <summary>
    /// Uses dynamic LINQ to draft a SQL ORDER BY based on the current sort
    /// </summary>
    /// <param name="query">The query to which the sorts should be appended</param>
    /// <returns>An IQueryable object with sorts applied</returns>
    protected IQueryable<T> ApplySorting(IQueryable<T> query) => Logic.ApplySorting(query);

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    protected virtual async Task ClearFilters() => await Logic.ClearFilters();

    /// <summary>
    /// Gets the context to determine what table and attributes to check against
    /// </summary>
    /// <returns>A queryable object that implements IHiokiLog</returns>
    protected abstract IQueryable<T> GetBaseQuery(LogDbContext db);
}