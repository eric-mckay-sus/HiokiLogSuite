using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

/// <summary>
/// This abstract class compiles the similar methods used between all tables
/// </summary>
/// <typeparam name="T">An implementation of IHiokiLog (defined in LogDbContext)</typeparam>
public abstract class LogTableBase<T> : ComponentBase where T : class, IHiokiLog
{
    [Inject] protected LogDbContext Db { get; set; } = default!;

    // Shared filters
    protected string? filterBarcode;
    protected DateTime? filterStartDate;
    protected DateTime? filterEndDate;
    protected string? filterResult;
    protected int? filterGroup;

    // Pagination variables
    protected int currentPage = 1;
    protected int pageSize = 100;
    protected int totalCount;
    protected int TotalPages => (int)Math.Ceiling((double)totalCount / pageSize); // dynamically computes page count whenever totalCount or pageSize update

    // For sorting
    protected string currentSortColumn = "";
    protected enum SortDirection { None, Asc, Desc }
    protected SortDirection sortDir = SortDirection.None;
    
    // Data storage
    protected List<T> DataView = [];
    protected List<string> modeCache = [];
    protected List<string> resultCache = [];

    /// <summary>
    /// Jumps to the specified new page
    /// </summary>
    /// <param name="newPage">The page number to jump to</param>
    /// <returns></returns>
    protected async Task ChangePage(int newPage)
    {
        if (newPage != currentPage && newPage >= 1 && newPage <= TotalPages)
        {
            currentPage = newPage;
            await RefreshData(keepPage: true);
        }
    }

    /// <summary>
    /// Helper to render the arrow
    /// </summary>
    /// <param name="columnName">The column for which to update the sort icon</param>
    /// <returns>The Unicode arrow representing the sort direction</returns>
    protected string GetSortIcon(string columnName)
    {
        if (currentSortColumn != columnName || sortDir == SortDirection.None) return "↕";
        return sortDir == SortDirection.Asc ? "▲" : "▼";
    }

    /// <summary>
    /// Cycles through sort directions when column is toggled
    /// Cycle order: None -> Asc -> Desc
    /// </summary>
    /// <param name="columnName">The column to be toggled</param>
    /// <returns></returns>
    protected async Task ToggleSort(string columnName)
    {
        if (currentSortColumn != columnName) { // If coming from none, save the column name (it's changed) and switch to asc
            currentSortColumn = columnName;
            sortDir = SortDirection.Asc;
        } else if(sortDir == SortDirection.Asc) { // If coming from asc, only need to switch to desc
            sortDir = SortDirection.Desc;
        } else { // If coming from desc, switch to none and inform model no column is specified to sort
            sortDir = SortDirection.None;
            currentSortColumn = "";
        }
        await RefreshData(); // because the parameters change, we wish to reset to page 1
    }

    /// <summary>
    /// Initializes the test mode and result type caches for step & FCT tables
    /// </summary>
    /// <param name="isStep">Whether to load caches for step table (versus FCT table)</param>
    /// <returns></returns>
    protected async Task InitializeCaches()
    {
        if (typeof(IStepFCT).IsAssignableFrom(typeof(T))) // verifies that there is a mode and result column in the target table
        {
            var query = GetBaseQuery();
            
            modeCache = await query
                .Select("Mode")
                .Distinct()
                .OrderBy("it")
                .ToDynamicListAsync<string>();
            resultCache = await query
                .Select("Result")
                .Distinct()
                .OrderBy("it")
                .ToDynamicListAsync<string>();;
        }
    }

    /// <summary>
    /// Applies filters and sorts, then refreshes the table based on the query and page number
    /// Persists page number if query doesn't change (i.e. when the refresh is just to get the new page)
    /// </summary>
    /// <param name="keepPage">Whether to keep the current page</param>
    /// <returns></returns>
    protected async Task RefreshData(bool keepPage = false)
    {
        if (!keepPage) currentPage = 1;

        IQueryable<T> query = GetBaseQuery();
        query = ApplyFilters(query);
        
        totalCount = await query.CountAsync();
        
        query = ApplySorting(query);
        
        DataView = await query
            .Skip((currentPage - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        
        StateHasChanged();
    }

    /// <summary>
    /// Applies the five filters common between all three pages
    /// </summary>
    /// <param name="query">The query to which the filters should be appended</param>
    /// <returns>An IQueryable object with filters applied</returns>
    protected virtual IQueryable<T> ApplyFilters(IQueryable<T> query)
    {
        if (!string.IsNullOrWhiteSpace(filterBarcode))
            query = query.Where(x => x.Barcode.Contains(filterBarcode));

        if (filterStartDate.HasValue)
            query = query.Where(x => x.Time >= filterStartDate.Value);

        if (filterEndDate.HasValue)
            // Add a day to encompass all times on day of end date
            query = query.Where(x => x.Time < filterEndDate.Value.AddDays(1));

        if (filterGroup != null)
            query = query.Where(s => s.Group == filterGroup);

        if (!string.IsNullOrWhiteSpace(filterResult))
            query = query.Where(x => x.Result == filterResult);

        return query;
    }

    /// <summary>
    /// Uses dynamic LINQ to draft a SQL ORDER BY based on the current sort
    /// </summary>
    /// <param name="query">The query to which the sorts should be appended</param>
    /// <returns>An IQueryable object with sorts applied</returns>
    protected IQueryable<T> ApplySorting(IQueryable<T> query)
    {
        if (sortDir == SortDirection.None || string.IsNullOrWhiteSpace(currentSortColumn))
        {
            return query.OrderBy("Time descending"); // Default
        }

        string direction = sortDir == SortDirection.Asc ? "ascending" : "descending";
        return query.OrderBy($"{currentSortColumn} {direction}"); // Dynamic LINQ is cool
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    protected virtual async Task ClearFilters()
    {
        filterBarcode = null;
        filterStartDate = null;
        filterEndDate = null;
        filterGroup = null;
        filterResult = null;
        await RefreshData();
    }

    /// <summary>
    /// Gets the context to determine what table and attributes to check against
    /// </summary>
    /// <returns>A queryable object that implements IHiokiLog</returns>
    protected abstract IQueryable<T> GetBaseQuery();
}