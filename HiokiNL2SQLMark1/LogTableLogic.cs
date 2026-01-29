using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

/// <summary>
/// This abstract class compiles the similar methods used between all tables
/// </summary>
/// <typeparam name="T">An implementation of IHiokiLog (defined in LogDbContext)</typeparam>
public class LogTableLogic<T>(IDbContextFactory<LogDbContext> dbFactory, Func<LogDbContext, IQueryable<T>> querySelector) where T : class, IHiokiLog
{
    private readonly IDbContextFactory<LogDbContext> _dbFactory = dbFactory;
    private readonly Func<LogDbContext, IQueryable<T>> _querySelector = querySelector;

    // Shared filters
    public string? FilterBarcode;
    public DateTime? FilterStartDate;
    public DateTime? FilterEndDate;
    public string? FilterResult;
    public int? FilterGroup;

    // Pagination variables
    public int CurrentPage = 1;
    public int PageSize = 100;
    public int TotalCount;
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize); // dynamically computes page count whenever totalCount or pageSize update

    // For sorting
    public string CurrentSortColumn = "";
    private enum SortDirection { None, Asc, Desc }
    private SortDirection SortDir = SortDirection.None;
    
    // Data storage
    public List<T> DataView = [];
    public List<string> modeCache = [];
    public List<string> resultCache = [];

    /// <summary>
    /// Applies filters and sorts, then refreshes the table based on the query and page number
    /// Persists page number if query doesn't change (i.e. when the refresh is just to get the new page)
    /// </summary>
    /// <param name="keepPage">Whether to keep the current page</param>
    /// <returns></returns>
    public async Task RefreshData(bool keepPage = false)
    {
        if (!keepPage) CurrentPage = 1;

        // One DbContext per refresh
        using var db = await _dbFactory.CreateDbContextAsync();

        IQueryable<T> query = _querySelector(db).AsNoTracking();
        query = ApplyFilters(query);
        TotalCount = await query.CountAsync();
        query = ApplySorting(query);
        
        DataView = await query
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }

    /// <summary>
    /// Applies the five filters common between all three pages
    /// </summary>
    /// <param name="query">The query to which the filters should be appended</param>
    /// <returns>An IQueryable object with filters applied</returns>
    public virtual IQueryable<T> ApplyFilters(IQueryable<T> query)
    {
        if (!string.IsNullOrWhiteSpace(FilterBarcode))
            query = query.Where(x => x.Barcode.Contains(FilterBarcode));

        if (FilterStartDate.HasValue)
            query = query.Where(x => x.Time >= FilterStartDate.Value);

        if (FilterEndDate.HasValue)
            // Add a day to encompass all times on day of end date
            query = query.Where(x => x.Time < FilterEndDate.Value.AddDays(1));

        if (FilterGroup != null)
            query = query.Where(s => s.Group == FilterGroup);

        if (!string.IsNullOrWhiteSpace(FilterResult))
            query = query.Where(x => x.Result == FilterResult);

        return query;
    }

    /// <summary>
    /// Uses dynamic LINQ to draft a SQL ORDER BY based on the current sort
    /// </summary>
    /// <param name="query">The query to which the sorts should be appended</param>
    /// <returns>An IQueryable object with sorts applied</returns>
    public IQueryable<T> ApplySorting(IQueryable<T> query)
    {
        if (SortDir == SortDirection.None || string.IsNullOrWhiteSpace(CurrentSortColumn))
        {
            return query.OrderBy("Time descending"); // Default
        }

        string direction = SortDir == SortDirection.Asc ? "ascending" : "descending";
        return query.OrderBy($"{CurrentSortColumn} {direction}"); // Dynamic LINQ is cool
    }

    /// <summary>
    /// Jumps to the specified new page
    /// </summary>
    /// <param name="newPage">The page number to jump to</param>
    /// <returns></returns>
    public async Task ChangePage(int newPage)
    {
        if (newPage != CurrentPage && newPage >= 1 && newPage <= TotalPages)
        {
            CurrentPage = newPage;
            await RefreshData(keepPage: true);
        }
    }

    /// <summary>
    /// Cycles through sort directions when column is toggled
    /// Cycle order: None -> Asc -> Desc
    /// </summary>
    /// <param name="columnName">The column to be toggled</param>
    /// <returns></returns>
    public async Task ToggleSort(string columnName)
    {
        if (CurrentSortColumn != columnName) { // If coming from none, save the column name (it's changed) and switch to asc
            CurrentSortColumn = columnName;
            SortDir = SortDirection.Asc;
        } else if(SortDir == SortDirection.Asc) { // If coming from asc, only need to switch to desc
            SortDir = SortDirection.Desc;
        } else { // If coming from desc, switch to none and inform model no column is specified to sort
            SortDir = SortDirection.None;
            CurrentSortColumn = "";
        }
        await RefreshData(); // because the parameters change, we wish to reset to page 1
    }

    /// <summary>
    /// Sets filters to null and reloads the query
    /// </summary>
    /// <returns></returns>
    public virtual async Task ClearFilters()
    {
        FilterBarcode = null;
        FilterStartDate = null;
        FilterEndDate = null;
        FilterGroup = null;
        FilterResult = null;
        await RefreshData();
    }

    /// <summary>
    /// Helper to render the arrow
    /// </summary>
    /// <param name="columnName">The column for which to update the sort icon</param>
    /// <returns>The Unicode arrow representing the sort direction</returns>
    public string GetSortIcon(string columnName)
    {
        if (CurrentSortColumn != columnName || SortDir == SortDirection.None) return "↕";
        return SortDir == SortDirection.Asc ? "▲" : "▼";
    }

    /// <summary>
    /// Maps a dictionary of filter key-value pairs to the individual filter properties
    /// </summary>
    /// <param name="filterDict">Dictionary containing filter keys and values</param>
    /// <returns></returns>
    public async Task ApplyFiltersFromDictionary(Dictionary<string, string> filterDict)
    {
        foreach (KeyValuePair<string, string> filter in filterDict)
        {
            switch (filter.Key.ToLower())
            {
                case "barcode":
                    FilterBarcode = filter.Value;
                    break;
                case "after":
                    if (DateTime.TryParse(filter.Value, out DateTime afterDate))
                        FilterStartDate = afterDate;
                    break;
                case "before":
                    if (DateTime.TryParse(filter.Value, out DateTime beforeDate))
                        FilterEndDate = beforeDate;
                    break;
                case "group":
                    if (int.TryParse(filter.Value, out int groupNum))
                        FilterGroup = groupNum;
                    break;
                case "result":
                    FilterResult = filter.Value;
                    break;
                // All other filters are handled by subclasses
                default:
                    break;
            }
        }
        // Allow subclasses to handle type-specific filters
        await ApplyTypeSpecificFiltersFromDictionary(filterDict);
        await RefreshData();
    }

    /// <summary>
    /// Initializes the test mode and result type caches for step & FCT tables
    /// </summary>
    /// <param name="isStep">Whether to load caches for step table (versus FCT table)</param>
    /// <returns></returns>
    public async Task InitializeCaches()
    {
        if (typeof(IStepFCT).IsAssignableFrom(typeof(T))) // verifies that there is a mode and result column in the target table
        {
            List<Task>? tasks = [];
            using var db = await _dbFactory.CreateDbContextAsync();
            // Re-create the base query using this local context instance
            var query = _querySelector(db).AsNoTracking();

            var modeTask = query
                .Select("Mode")
                .Distinct()
                .OrderBy("it")
                .ToDynamicListAsync<string>();
            var resultTask = query
                .Select("Result")
                .Distinct()
                .OrderBy("it")
                .ToDynamicListAsync<string>();;
            
            var results = await Task.WhenAll(modeTask, resultTask);
            modeCache = results[0];
            resultCache = results[1];
        }
    }

    /// <summary>
    /// Applies type-specific filters from dictionary. Override in subclasses for table-specific filters.
    /// </summary>
    /// <param name="filterDict">Dictionary of remaining filters to apply</param>
    /// <returns></returns>
    public virtual Task ApplyTypeSpecificFiltersFromDictionary(Dictionary<string, string> filterDict)
    {
        // Base implementation does nothing; override in subclasses for specific behavior
        return Task.CompletedTask;
    }

    public void ClearData()
    {
        DataView = [];
        TotalCount = 0;
        CurrentPage = 1;
    }
}