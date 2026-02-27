using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

namespace HiokiNL2SQLMark1.Logic;

/// <summary>
/// Holds all the methods necessary to store a table
/// </summary>
/// <typeparam name="T">An implementation of IHiokiLog (defined in LogDbContext)</typeparam>
public class LogTableLogic<T> : ILogTableLogic where T : class, IHiokiLog
{
    // For compliance with ILogTable (these particular values should never be seen, overridden by children)
    public virtual string TableName => "unknown"; // The internal name of this table
    public virtual string DisplayName => "Unknown Table"; // The external name of this table

    // Dependencies
    protected readonly IDbContextFactory<LogDbContext> _dbFactory; // Generates a new DbContext on demand (thread-safe)
    protected readonly Func<LogDbContext, IQueryable<T>> _querySelector; // Encapsulates the connection and query information
    public readonly IJSService JS; // For handling CSV download
    protected readonly INavService Nav; // For navigating to the power search page in a barcode "drill-down"

    // Pagination variables
    public int CurrentPage { get; set; } = 1; // Tracks the current page number (always between 1 and TotalPages, inclusive)
    public int PageSize { get; set; } = 50; // The number of results per page
    public int TotalCount { get; set; } // The total number of results
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize); // Dynamically compute page count whenever totalCount or pageSize update

    // For sorting
    public string CurrentSortColumn { get; set; } = ""; // The name of the column that results are currently being sorted by
    public string SortDir { get; set; } = "none"; // The sort direction of the currently sorted column
    
    // Data storage
    public Dictionary<string, IFilter> Filters { get; set; } = new(StringComparer.OrdinalIgnoreCase); // Filter registry, updated as necessary by children
    public bool IsLoading { get; private set; } = true; // Whether the query is currently loading the table display
    public List<T> DataView { get; private set; } = []; // Stores the query results, only of the current page
    public HashSet<string> ModeCache { get; private set; } = []; // The list of test modes to choose from (technically this should be in the children)
    public HashSet<string> ResultCache { get; private set; } = []; // The list of test result types to choose from

    // Provided to the UI
    public Action? OnNotifyUI { private get; set; } // Prompts the view to refresh (this is not architecturally correct for MVVM)
    public Action? UpdatePSUrl { get; set; } // Prompts the power search engine to update its URL
    public Action<string>? TriggerPowerSearch { get; set; } // Allows UI/tests to request a PowerSearch URL update
    public int? LastQueryHash { get; private set; } // The hash of the filter state of the most recent query on this table
    public bool IsStale => // If there is an override, use it, otherwise just compare the hash of this filter state and the last one
        LastQueryHash != GetFilterStateHash(Filters);

    // UI-only flag used by the MasterTable component.  PowerSearchLogic will set
    // an override so the table can show a stale appearance whenever the tab name
    // doesn't match the search bar text.  This value has no influence on query
    // execution or caching; use IsStale for those semantics.
    public Func<bool>? UIIsStaleOverride { get; set; }
    public bool UIIsStale => UIIsStaleOverride != null ? UIIsStaleOverride() : IsStale;
    
    /// <summary>
    /// Builds a new LogTableLogic using DB context and necessary services. Adds all relevant filters to the registry based on subtype
    /// </summary>
    /// <param name="dbFactory">Generates a new LogDbContext for each thread</param>
    /// <param name="querySelector">Encapsulates the DB connection and query information</param>
    /// <param name="js">An implementation of </param>
    /// <param name="nav"></param>
    public LogTableLogic(IDbContextFactory<LogDbContext> dbFactory, Func<LogDbContext, IQueryable<T>> querySelector, IJSService js, INavService nav)
    {
        _dbFactory = dbFactory;
        _querySelector = querySelector;
        JS = js;
        Nav = nav;

       InitializeFilters(); // Children override this method as necessary 
    }

    /// <summary>
    /// Generate a state ID, for checking equality between two filter states
    /// 17 and 31 are primes one off of powers of two, so we get few collisions and the compiler can take shortcuts
    /// </summary>
    /// <param name="filterDict">A dictionary of keys mapped to filters</param>
    /// <returns>A value representing the state of the filters for the input dictionary</returns>
    public int GetFilterStateHash(Dictionary<string, IFilter> filterDict) {
        unchecked // Tells the compiler to simply truncate the calculation instead of throwing an exception for integer overflow
        {
            int hash = 17;
            // Order by key to ensure dictionary order doesn't change the hash
            foreach (var key in filterDict.Keys.OrderBy(k => k))
            {
                // Ignore 'in' key, it does not affect the search contents within a table
                if (key.Equals("in", StringComparison.OrdinalIgnoreCase)) continue;

                // Factor in each aspect of the filter
                var filter = filterDict[key];

                // Key and activity status are part of hash regardless of activity status
                hash *= 31 + key.ToLower().GetHashCode();
                hash *= 31 + filter.IsActive.GetHashCode();

                // Only hash filter details if active
                if (filter.IsActive)
                {
                    hash *= 31 + filter.IsNegated.GetHashCode();
                    string value = filter.GetValue()?.ToString()?.Trim().ToLower() ?? "";
                    hash *= 31 + value.GetHashCode();
                }
            }
            // Sorts and pagination affect view, so a hash of the view should include them
            hash *= 31 + CurrentSortColumn.GetHashCode();
            hash *= 31 + SortDir.GetHashCode();
            hash *= 31 + CurrentPage.GetHashCode();
            hash *= 31 + PageSize.GetHashCode();
            
            return hash;
        }
    }

    /// <summary>
    /// Creates an entry for each filter in the registry, wiring them to automatically push and pull data from form fields
    /// </summary>
    protected virtual void InitializeFilters()
    {
        Filters["barcode"] = new Filter<string?>("barcode", null) { OnChanged = NotifyStateChanged };
        Filters["after"] = new Filter<DateTime?>("after", null) { OnChanged = NotifyStateChanged };
        Filters["before"] = new Filter<DateTime?>("before", null) { OnChanged = NotifyStateChanged };
        Filters["result"] = new Filter<string?>("result", null) { OnChanged = NotifyStateChanged };
        Filters["group"] = new Filter<int?>("group", null) { OnChanged = NotifyStateChanged };
    }

    /// <summary>
    /// Applies filters and sorts, then reloads the table based on the query and page number
    /// Persists page number if query doesn't change (i.e. when the refresh is just to get the new page)
    /// </summary>
    /// <param name="keepPage">Whether to keep the current page</param>
    /// <param name="force">Whether to force a refresh</param>
    /// <returns></returns>
    public async Task RefreshData(bool keepPage = false)
    {
        if (!keepPage) CurrentPage = 1;
        if (DataView.Count > 0 && !IsStale) return;

        IsLoading = true;
        OnNotifyUI?.Invoke(); // Show loading state
        
        try {
            using var db = await _dbFactory.CreateDbContextAsync(); // One DbContext per refresh
            IQueryable<T> query = _querySelector(db).AsNoTracking();

            query = ApplyFilters(query);
            TotalCount = await query.CountAsync();
            query = ApplySorting(query);
            
            DataView = await query
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            LastQueryHash = GetFilterStateHash(Filters);
        }
        finally
        {
            IsLoading = false;
            OnNotifyUI?.Invoke();
        }
    }

    /// <summary>
    /// Helper to get the strongly-typed version of a generic-typed filter from the registry
    /// </summary>
    /// <typeparam name="U">The generic type of a Filter, one of string, int, or DateTime</typeparam>
    /// <param name="key">The key for which to get the filter</param>
    /// <returns>The Filter with its appropriate type</returns>
    public Filter<U> GetFilter<U>(string key)
    {
        // 99% will go here
        if(Filters.TryGetValue(key, out var f) && f is Filter<U> typed) return typed;

        // But if something slips through the cracks, this will ensure the hash doesn't break
        var newFilter = new Filter<U>(key, default!) { OnChanged = NotifyStateChanged };
        Filters[key] = newFilter;
        return newFilter;
    }

    /// <summary>
    /// Saves a mapping of search keys to IFilters to the registry, then calls for a refresh
    /// Table-specific filters are handled because their child class has added their key to the registry in InitializeFilters
    /// </summary>
    /// <param name="filterDict">The dictionary of search keys mapped to filters</param>
    /// <param name="keepPage">Whether to keep the page number (or reset it)</param>
    /// <returns></returns>
    public async Task DictionaryToFilters(Dictionary<string, IFilter> filterDict, bool keepPage=false)
    {
        foreach (var existing in Filters.Values)
        {
            // Check to see if there's a new filter
            if (filterDict.TryGetValue(existing.Key, out var incoming)) {
                existing.CopyFrom(incoming);
            }
            // Otherwise, reset it, as it's not part of this query 
            else {
                existing.Reset();
            }
        }
        await RefreshData(keepPage);
    }

    /// <summary>
    /// Applies the five filters common between all three pages
    /// Overridden in children to apply table-specific filters
    /// </summary>
    /// <param name="query">The query to which the filters should be appended</param>
    /// <returns>An IQueryable object with filters applied</returns>
    public virtual IQueryable<T> ApplyFilters(IQueryable<T> query)
    {
        var barcode = GetFilter<string?>("barcode");
        var startDate = GetFilter<DateTime?>("after");
        var endDate = GetFilter<DateTime?>("before");
        var groupNum = GetFilter<int?>("group");
        var result = GetFilter<string?>("result");

        if (barcode.IsActive)
            query = barcode.IsNegated
                ? query.Where(x => !x.Barcode.Contains(barcode.Value))
                : query.Where(x => x.Barcode.Contains(barcode.Value));

        if (startDate.IsActive) // Can't negate date checks, so ignore negation status
            query = query.Where(x => x.Time >= startDate.Value);

        if (endDate.IsActive) // The semantics of the word "before" are tricky and depend on whether a time was specified, but that's handled in the parser now
            query = query.Where(x => x.Time <= endDate.Value);

        if (groupNum.IsActive)
            query = groupNum.IsNegated
                ? query.Where(x => x.Group != groupNum.Value)
                : query.Where(x => x.Group == groupNum.Value);

        if (result.IsActive)
            query = result.IsNegated
                ? query.Where(x => x.Result != result.Value)
                : query.Where(x => x.Result == result.Value);

        return query;
    }

    /// <summary>
    /// Uses dynamic LINQ to draft a SQL ORDER BY based on the current sort
    /// </summary>
    /// <param name="query">The query to which the sorts should be appended</param>
    /// <returns>An IQueryable object with sorts applied</returns>
    public IQueryable<T> ApplySorting(IQueryable<T> query)
    {
        if (SortDir == "none" || string.IsNullOrWhiteSpace(CurrentSortColumn))
        {
            return query.OrderBy("Time descending"); // Default
        }

        return query.OrderBy($"{CurrentSortColumn} {SortDir}"); // Dynamic LINQ is cool
    }

    /// <summary>
    /// Detects the table, then saves the results of the query on that table to a CSV
    /// Uses JSService (indirectly uses JS Runtime) to download directly to browser Downloads location
    /// </summary>
    /// <returns></returns>
    public async Task SaveToCSV()
    {
        Type targetType = typeof(T);
        var properties = targetType.GetProperties();
        var csvBuilder = new System.Text.StringBuilder();

        // Header
        csvBuilder.AppendLine(string.Join(",", properties.Select(p => p.Name)));

        // Re run the current query with the current filters and sorts
        using var db = await _dbFactory.CreateDbContextAsync();
        IQueryable<T> query = _querySelector(db).AsNoTracking();
        query = ApplyFilters(query);
        query = ApplySorting(query); 
        var allData = await query.ToListAsync();

        // Loop through each row, parse, then pass to the CSV builder
        foreach (var item in allData)
        {
            var values = properties.Select(p => {
                string val = p.GetValue(item)?.ToString() ?? "";
                // CSV escaping: wrap in quotes if contains comma, newline, or quotes
                if (val.Contains(',') || val.Contains('"') || val.Contains('\n') || val.Contains('\r'))
                    val = $"\"{val.Replace("\"", "\"\"")}\"";
                return val;
            });
            csvBuilder.AppendLine(string.Join(",", values));
        }

        // Call JS Runtime to perform the download
        string fileName = $"{targetType.Name}s_{DateTime.Now:yyyyMMdd_HHmm}.csv";
        await JS.DownloadCsv(fileName, csvBuilder.ToString());
    }

    /// <summary>
    /// Using the NavigationManager embedded in NavService, navigates to the power search page upon selecting a barcode to pursue
    /// </summary>
    /// <param name="barcode">The barcode to trace</param>
    public void HandleBarcodeClick(string barcode)
    {
        Nav.EnsureSubscribed();
        string query = $"in:all barcode:{barcode}";
        // Redirect to the PowerSearch page to view all results
        if (OnNotifyUI != null)
        {
            TriggerPowerSearch?.Invoke(query);
        }
        else
        {
            Nav.NavigateToBarcodeTrace(barcode);
        }
    }

    /// <summary>
    /// Call to tell the UI to re-render
    /// </summary>
    public void NotifyStateChanged() => OnNotifyUI?.Invoke();

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
            UpdatePSUrl?.Invoke();
        }
    }

    /// <summary>
    /// Modifies the page size from PageSize to newSize
    /// </summary>
    /// <param name="newSize">The desired number of entries per page</param>
    /// <returns></returns>
    public async Task AlterPageSize(int newSize)
    {
        if (newSize != PageSize)
        {
            PageSize = newSize;
            // Reset to page 1 because the number of pages has changed
            CurrentPage = 1; 
            await RefreshData();
            UpdatePSUrl?.Invoke();
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
            SortDir = "ascending";
        } else if(SortDir == "ascending") { // If coming from asc, only need to switch to desc
            SortDir = "descending";
        } else { // If coming from desc, switch to none and inform model no column is specified to sort
            SortDir = "none";
            CurrentSortColumn = "";
        }
        await RefreshData(); // because the parameters change, we wish to reset to page 1
        UpdatePSUrl?.Invoke();
    }

    /// <summary>
    /// Helper to render the arrow
    /// </summary>
    /// <param name="columnName">The column for which to update the sort icon</param>
    /// <returns>The Unicode arrow representing the sort direction</returns>
    public string GetSortIcon(string columnName)
    {
        if (CurrentSortColumn != columnName || SortDir == "none") return "↕";
        return SortDir == "ascending" ? "▲" : "▼";
    }

    /// <summary>
    /// Initializes the test mode and result type caches (for step & FCT tables)
    /// </summary>
    /// <returns></returns>
    public virtual async Task InitializeCaches()
    {
        // If the result cache is full, don't reload it (this destroys the point of a cache)
        if (LastQueryHash != null && ResultCache.Count != 0) return;
        
        // Fill the final result cache for all tables
        ResultCache = await GetCacheSet("Result");

        if (typeof(IStepFCT).IsAssignableFrom(typeof(T)))
        {
            ModeCache = await GetCacheSet("Mode");
        }
    }

    /// <summary>
    /// Gets a set of all values across a property
    /// </summary>
    /// <param name="property">The property for which to get unique values</param>
    /// <returns>A list of uniquer property values</returns>
    protected async Task<HashSet<string>> GetCacheSet(string property)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var list = await _querySelector(db).AsNoTracking()
            .Select(property)
            .Distinct()
            .OrderBy("it")
            .ToDynamicListAsync<string>();
        return [.. list];
    }

    /// <summary>
    /// Resets the common filters, and optionally, the page
    /// <param name="keepPage">Whether to keep the current page (or reset it)</param>
    /// </summary>
    public void ResetFilterState(bool keepPage=false)
    {
        foreach(var f in Filters.Values) f.Reset();
        if (!keepPage) CurrentPage = 1;
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