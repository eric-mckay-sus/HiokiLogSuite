using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;

namespace HiokiNL2SQLMark1.Logic;

/// <summary>
/// Holds all the methods necessary to store a table
/// </summary>
/// <typeparam name="T">An implementation of IHiokiLog (defined in LogDbContext)</typeparam>
public class LogTableLogic<T> : ILogTableLogic where T : class, IHiokiLog
{
    // For compliance with ILogTable (these particular values should never be seen)
    public virtual string TableName => "unknown";
    public virtual string DisplayName => "Unknown Table";

    // Utilities
    private readonly IDbContextFactory<LogDbContext> _dbFactory; // generates a new DbContext on demand (thread-safe)
    private readonly Func<LogDbContext, IQueryable<T>> _querySelector; // denotes the connection and query information
    protected readonly IJSRuntime JS; // for handling CSV download
    protected readonly NavigationManager Nav; // for navigating to the power search page in a barcode "drill-down"

    // Shared filters
    public Dictionary<string, IFilter> Filters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Pagination variables
    public int CurrentPage { get; set; } = 1; // Tracks the current page number (always between 1 and TotalPages, inclusive)
    public int PageSize { get; set; } = 50; // The number of results per page
    public int TotalCount { get; set; } // the total number of results
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize); // dynamically computes page count whenever totalCount or pageSize update

    // For sorting
    public string CurrentSortColumn { get; set; } = ""; // The name of the column that results are currently being sorted by
    public string SortDir { get; set; } = "none"; // The sort direction of the currently sorted column
    
    // Data storage
    public bool IsLoading { get; set; } // Whether the query is currently loading the table display
    public List<T> DataView = []; // Stores the query results, only of the current page
    public List<string> modeCache = []; // The list of test modes to choose from
    public List<string> resultCache = []; // The list of test result types to choose from

    // Provided to the UI
    public Action? OnNotifyUI { get; set; } // Trigger so this method can tell the view to update (this is not architecturally correct for MVVM)
    public Action<string>? TriggerPowerSearch { get; set; } // Directly executes a power search with the input string, jumping to the power search page
    public Action? UpdatePSUrl { get; set; }
    public int? LastQueryHash { get; private set; }
    public Func<bool>? IsStaleOverride { get; set; }
    public virtual bool IsStale => IsStaleOverride != null 
        ? IsStaleOverride() 
        : LastQueryHash != GetFilterStateHash(Filters);
    
    /// <summary>
    /// Generate a state ID, for checking equality between two filter states
    /// 17 and 31 are primes one off of powers of two, so we get few collisions and the compiler can take shortcuts
    /// </summary>
    /// <param name="filterDict">A dictionary of keys mapped to filters</param>
    /// <returns>A value representing the state of the filters for the input dictionary</returns>
    public int GetFilterStateHash(Dictionary<string, IFilter> filterDict) {
        unchecked
        {
            int hash = 17;
            // Order by key to ensure dictionary order doesn't change the hash
            foreach (var key in filterDict.Keys.OrderBy(k => k))
            {
                // Ignore 'in' key, it does not affect the search contents within a table
                if (key.Equals("in", StringComparison.OrdinalIgnoreCase)) continue;

                // Factor in each aspect of the filter
                var filter = filterDict[key];
                
                var val = filter.GetValue();
                if (val != null)
                {
                    hash *= 31 + key.ToLower().GetHashCode();
                    hash += 31 + filter.IsNegated.GetHashCode();
                    string valStr = val.ToString()?.Trim().ToLower() ?? "";
                    hash *= 31 + valStr.GetHashCode();
                }
            }
            return hash;
        }
    }

    public LogTableLogic(IDbContextFactory<LogDbContext> dbFactory, Func<LogDbContext, IQueryable<T>> querySelector, IJSRuntime js, NavigationManager navManager)
    {
        _dbFactory = dbFactory;
        _querySelector = querySelector;
        JS = js;
        Nav = navManager;

       InitializeFilters(); // Children override this method as necessary 
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
    /// Renders the table representing this query and its results
    /// </summary>
    /// <returns>A RenderFragment that can be used elsewhere to display this table</returns>
    public virtual RenderFragment RenderTable() => builder =>
    {
        if (DataView.Count == 0 && !IsLoading)
        {
            builder.OpenElement(0, "h4");
            builder.AddAttribute(1, "style", "text-align: center;");
            builder.AddContent(2, $"No {TableName} records found matching these criteria.");
            builder.CloseElement();
            return;
        }
        builder.OpenComponent<Components.Pages.MasterTable<T>>(0);

        // Pass all necessary parameters from this Logic instance to the MasterTable
        builder.AddAttribute(1, "Items", DataView);
        builder.AddAttribute(2, "CurrentPage", CurrentPage);
        builder.AddAttribute(3, "TotalPages", TotalPages);
        builder.AddAttribute(4, "TotalCount", TotalCount);
        builder.AddAttribute(5, "PageSize", PageSize);
        
        // Wire up pagination and sorting
        builder.AddAttribute(6, "OnPageChange", EventCallback.Factory.Create<int>(this, ChangePage));
        builder.AddAttribute(7, "OnSort", EventCallback.Factory.Create<string>(this, ToggleSort));
        builder.AddAttribute(8, "GetSortIcon", GetSortIcon);
        builder.AddAttribute(9, "OnPageSizeChange", EventCallback.Factory.Create<int>(this, AlterPageSize));
        
        // Wire up Actions
        builder.AddAttribute(10, "OnSaveToCsv", EventCallback.Factory.Create(this, SaveToCSV));
        builder.AddAttribute(11, "OnBarcodeClick", EventCallback.Factory.Create<string>(this, HandleBarcodeClick));

        // Display modifiers for non-interactable states
        builder.AddAttribute(12, "IsStale", IsStale);
        builder.AddAttribute(13, "IsLoading", IsLoading);

        builder.CloseComponent();
    };

    /// <summary>
    /// Applies filters and sorts, then reloads the table based on the query and page number
    /// Persists page number if query doesn't change (i.e. when the refresh is just to get the new page)
    /// </summary>
    /// <param name="keepPage">Whether to keep the current page</param>
    /// <returns></returns>
    public async Task RefreshData(bool keepPage = false)
    {
        if (!keepPage) CurrentPage = 1;

        IsLoading = true;
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

        IsLoading = false;
        LastQueryHash = GetFilterStateHash(Filters);
        OnNotifyUI?.Invoke();
    }

    /// <summary>
    /// Helper to get the strongly-typed version of a generic-typed filter
    /// </summary>
    /// <typeparam name="U">The generic type of a Filter, one of string, int, or DateTime</typeparam>
    /// <param name="key">The key for which to get the filter</param>
    /// <returns>The Filter with its appropriate type</returns>
    public Filter<U> GetFilter<U>(string key) => 
        Filters.TryGetValue(key, out var f) && f is Filter<U> typed 
        ? typed 
        : new Filter<U>(key, default!);

    /// <summary>
    /// Applies the five filters common between all three pages
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

        if (startDate.IsActive)
            query = query.Where(x => x.Time >= startDate.Value);

        if (endDate.IsActive) // The semantics of the word "before" are tricky and depend on whether a time was specified
            if (endDate.Value!.Value.TimeOfDay == TimeSpan.Zero) // If the datetime has midnight as the time part, that means only the date part was provided by the user
            {
                query = query.Where(x => x.Time < endDate.Value!.Value.AddDays(1)); // this is inclusive of all times on the end date
            }
            else // Otherwise, use the time provided by the user as a hard stop
            {
                query = query.Where(x => x.Time <= endDate.Value); // this stops exactly at the time specified
            }

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
    /// Saves a dictionary of filter key-value pairs to the registry, then calls for a refresh
    /// Table-specific filters are handled because their child class has added their key to the registry
    /// </summary>
    /// <param name="filterDict">The dictionary of search keys mapped to filters</param>
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
    /// Uses JS Runtime to download directly to browser Downloads location
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
        await JS.InvokeVoidAsync("downloadFileFromStream", fileName, csvBuilder.ToString());
    }

    /// <summary>
    /// Navigates to the power search page upon selecting a barcode to pursue
    /// </summary>
    /// <param name="barcode">The barcode to trace</param>
    public void HandleBarcodeClick(string barcode)
    {
        string query = $"in:all barcode:{barcode}";
        // Redirect to the PowerSearch page to view all results
        if (OnNotifyUI != null)
        {
            TriggerPowerSearch?.Invoke(query);
        }
        else
        {
            Nav.NavigateTo($"/?q={Uri.EscapeDataString(query)}");
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