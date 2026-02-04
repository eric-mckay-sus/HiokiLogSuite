using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

namespace HiokiNL2SQLMark1.Logic;
/// <summary>
/// Holds all the methods necessary to store a table
/// </summary>
/// <typeparam name="T">An implementation of IHiokiLog (defined in LogDbContext)</typeparam>
public class LogTableLogic<T>(IDbContextFactory<LogDbContext> dbFactory, Func<LogDbContext, IQueryable<T>> querySelector) where T : class, IHiokiLog
{
    private readonly IDbContextFactory<LogDbContext> _dbFactory = dbFactory; // generates a new DbContext on demand (thread-safe)
    private readonly Func<LogDbContext, IQueryable<T>> _querySelector = querySelector; // denotes the connection and query information

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
    /// Applies filters and sorts, then reloads the table based on the query and page number
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

        if (FilterEndDate.HasValue) // The semantics of the word "before" are tricky and depend on whether a time was specified
            if (FilterEndDate.Value.TimeOfDay == TimeSpan.Zero) // If the datetime has midnight as the time part, that means only the date part was provided by the user
            {
                query = query.Where(x => x.Time < FilterEndDate.Value.AddDays(1)); // this is inclusive of all times on the end date
            }
            else // Otherwise, use the time provided by the user as a hard stop
            {
                query = query.Where(x => x.Time <= FilterEndDate.Value); // this stops exactly at the time specified
            }

        if (FilterGroup != null)
            query = query.Where(s => s.Group == FilterGroup);

        if (!string.IsNullOrWhiteSpace(FilterResult))
            query = query.Where(x => x.Result == FilterResult);

        return query;
    }

    /// <summary>
    /// Maps a dictionary of filter key-value pairs to the individual filter properties
    /// </summary>
    /// <param name="filterDict">Dictionary containing filter keys and values</param>
    /// <returns></returns>
    public virtual async Task ApplyFiltersFromDictionary(Dictionary<string, string> filterDict)
    {
        foreach (var (key, value) in filterDict)
        {
            switch (key.ToLower())
            {
                case "barcode": FilterBarcode = value; break;
                case "result":  FilterResult = value; break;
                case "group" when int.TryParse(value, out var i): FilterGroup = i; break;

                case "after":
                    FilterStartDate = LogTableLogic<T>.ResolveFullDateTime(value);
                    break;

                case "before":
                    FilterEndDate = LogTableLogic<T>.ResolveFullDateTime(value);
                    break;
            }
        }
        await RefreshData();
    }

    /// <summary>
    /// Helper to handle the "Date + Time/Shift" alias logic for backend execution
    /// </summary>
    private static DateTime? ResolveFullDateTime(string rawValue)
    {
        var parts = rawValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        // Use your Service to get the date part
        // Note: You may need to inject ParserService or make TranslateDateAlias accessible here
        DateTime? finalDate = SearchParserService.ParseAliasToDateTime(parts[0], isTimePart: false);

        if (finalDate.HasValue && parts.Length > 1)
        {
            // Try to get a time/shift from the second part
            DateTime? timePart = SearchParserService.ParseAliasToDateTime(parts[1], isTimePart: true);
            if (timePart.HasValue)
            {
                // Combine the date from the first part with the time from the second
                finalDate = finalDate.Value.Date.Add(timePart.Value.TimeOfDay);
            }
        }

        return finalDate;
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
    /// Initializes the test mode and result type caches (for step & FCT tables)
    /// </summary>
    /// <param name="isStep">Whether to load caches for step table (versus FCT table)</param>
    /// <returns></returns>
    public async Task InitializeCaches()
{
    if (typeof(IStepFCT).IsAssignableFrom(typeof(T)))
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var query = _querySelector(db).AsNoTracking();

        // Run sequentially to avoid context collisions
        modeCache = await query
            .Select("Mode")
            .Distinct()
            .OrderBy("it")
            .ToDynamicListAsync<string>();

        resultCache = await query
            .Select("Result")
            .Distinct()
            .OrderBy("it")
            .ToDynamicListAsync<string>();
    }
}

    /// <summary>
    /// Resets the common filters
    /// Override to reset table-specific filters
    /// </summary>
    public virtual void ResetFilterState()
    {
        FilterBarcode = null;
        FilterStartDate = null;
        FilterEndDate = null;
        FilterGroup = null;
        FilterResult = null;
        CurrentPage = 1;
    }

    /// <summary>
    /// Resets filters AND reloads the query
    /// Only bind to the CLEAR button
    /// </summary>
    /// <returns></returns>
    public async Task ClearFilters()
    {
        ResetFilterState();
        await RefreshData();
    }

    /// <summary>
    /// Resets the entire model without refreshing (prepare for new query)
    /// </summary>
    public void ClearData()
    {
        DataView = [];
        TotalCount = 0;
        CurrentPage = 1;
        ResetFilterState();
    }
}