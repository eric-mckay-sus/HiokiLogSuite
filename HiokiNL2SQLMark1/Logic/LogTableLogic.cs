using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;

namespace HiokiNL2SQLMark1.Logic;

/// <summary>
/// Holds all the methods necessary to store a table
/// </summary>
/// <typeparam name="T">An implementation of IHiokiLog (defined in LogDbContext)</typeparam>
public class LogTableLogic<T>(IDbContextFactory<LogDbContext> dbFactory, Func<LogDbContext, IQueryable<T>> querySelector, IJSRuntime js, NavigationManager navManager) : ILogTableLogic where T : class, IHiokiLog
{
    // For compliance with ILogTable (these values should never be seen)
    public virtual string TableName => "unknown";
    public virtual string DisplayName => "Unknown Table";

    // Utilities
    private readonly IDbContextFactory<LogDbContext> _dbFactory = dbFactory; // generates a new DbContext on demand (thread-safe)
    private readonly Func<LogDbContext, IQueryable<T>> _querySelector = querySelector; // denotes the connection and query information
    protected readonly IJSRuntime JS = js; // for handling CSV download
    protected readonly NavigationManager Nav = navManager; // for navigating to the power search page in a barcode "drill-down"

    // Shared filters
    public Filter<string?> FilterBarcode = new("barcode", null); // to filter barcodes (substring containment)
    public Filter<DateTime?> FilterStartDate = new("after", null); // to filter start date (inclusive)
    public Filter<DateTime?> FilterEndDate = new("before", null); // to filter end date (inclusive of whole day unless time given)
    public Filter<string?> FilterResult = new("result", null); // to filter entire entry's result
    public Filter<int?> FilterGroup = new("group", null); // to filter group number

    // Pagination variables
    public int CurrentPage { get; set; } = 1; // Tracks the current page number (always between 1 and TotalPages, inclusive)
    public int PageSize = 50; // The number of results per page
    public int TotalCount { get; set; } // the total number of results
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize); // dynamically computes page count whenever totalCount or pageSize update

    // For sorting
    public string CurrentSortColumn = ""; // The name of the column that results are currently being sorted by
    private enum SortDirection { None, Asc, Desc } // Enumerates the sorting states of a column
    private SortDirection SortDir = SortDirection.None; // The sort direction of the currently sorted column
    
    // Data storage
    public bool IsLoading { get; set; } // Whether the query is currently loading the table display
    public List<T> DataView = []; // Stores the query results, only of the current page
    public List<string> modeCache = []; // The list of test modes to choose from
    public List<string> resultCache = []; // The list of test result types to choose from

    // Provided to the UI
    public Action? OnNotifyUI { get; set; } // Trigger so this method can tell the view to update (this is not architecturally correct for MVVM)
    public Action<string>? TriggerPowerSearch { get; set; } // Directly executes a power search with the input string
    private int? LastQueryHash;
    public bool IsOld => LastQueryHash != GetFilterStateHash();
    public virtual int GetFilterStateHash() => HashCode.Combine(FilterBarcode.Value, FilterStartDate.Value, FilterEndDate.Value, FilterResult.Value, FilterGroup.Value);

    /// <summary>
    /// Renders the table representing this query and its results
    /// </summary>
    /// <returns>A RenderFragment that can be used elsewhere</returns>
    public virtual RenderFragment RenderTable() => builder =>
    {
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
        
        // Wire up Actions
        builder.AddAttribute(9, "OnSaveToCsv", EventCallback.Factory.Create(this, SaveToCSV));
        builder.AddAttribute(10, "OnBarcodeClick", EventCallback.Factory.Create<string>(this, HandleBarcodeClick));

        // State for table display
        builder.AddAttribute(11, "IsOld", IsOld);
        builder.AddAttribute(12, "IsLoading", IsLoading);

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
        LastQueryHash = GetFilterStateHash();
        OnNotifyUI?.Invoke();
    }

    /// <summary>
    /// Applies the five filters common between all three pages
    /// </summary>
    /// <param name="query">The query to which the filters should be appended</param>
    /// <returns>An IQueryable object with filters applied</returns>
    public virtual IQueryable<T> ApplyFilters(IQueryable<T> query)
    {
        if (FilterBarcode.IsActive)
            query = FilterBarcode.IsNegated
                ? query.Where(x => !x.Barcode.Contains(FilterBarcode.Value))
                : query.Where(x => x.Barcode.Contains(FilterBarcode.Value));

        if (FilterStartDate.IsActive)
            query = query.Where(x => x.Time >= FilterStartDate.Value);

        if (FilterEndDate.IsActive) // The semantics of the word "before" are tricky and depend on whether a time was specified
            if (FilterEndDate.Value!.Value.TimeOfDay == TimeSpan.Zero) // If the datetime has midnight as the time part, that means only the date part was provided by the user
            {
                query = query.Where(x => x.Time < FilterEndDate.Value!.Value.AddDays(1)); // this is inclusive of all times on the end date
            }
            else // Otherwise, use the time provided by the user as a hard stop
            {
                query = query.Where(x => x.Time <= FilterEndDate.Value); // this stops exactly at the time specified
            }

        if (FilterGroup.IsActive)
            query = FilterGroup.IsNegated
                ? query.Where(x => x.Group != FilterGroup.Value)
                : query.Where(x => x.Group == FilterGroup.Value);

        if (FilterResult.IsActive)
            query = FilterResult.IsNegated
                ? query.Where(x => x.Result != FilterResult.Value)
                : query.Where(x => x.Result == FilterResult.Value);

        return query;
    }

    /// <summary>
    /// Maps a dictionary of filter key-value pairs to the individual filter properties, then calls for a refresh
    /// First checks if the filter is table-specific (inferred from LogTableLogic instantiation)
    /// </summary>
    /// <param name="filterDict">The dictionary of search tags mapped to values</param>
    /// <returns></returns>
    public async Task DictionaryToFilters(Dictionary<string, IFilter> filterDict)
    {
        // Ensure no old filters persist
        ResetFilterState();
        
        foreach (var filter in filterDict.Values)
        {
            // If the tag is applicable to the child, ignore it here
            if (AssignTableSpecific(filter)) continue; // yes, this is a side-effect

            switch (filter)
            {
                case Filter<string?> f when f.Key == "barcode": 
                    FilterBarcode = f; break;
                case Filter<string?> f when f.Key == "result": 
                    FilterResult = f; break;
                case Filter<int?> f when f.Key == "group": 
                    FilterGroup = f; break;
                case Filter<DateTime?> f when f.Key == "after": 
                    FilterStartDate = f; break;
                case Filter<DateTime?> f when f.Key == "before": 
                    FilterEndDate = f; break;
            }
        }
        await RefreshData();
    }

    /// <summary>
    /// Assigns responsibility to the children to identify their table-specific tags
    /// The generic table has no table-specific tags, so returns false by default
    /// </summary>
    /// <param name="filter">The filter to check for</param>
    /// <returns>Whether the table accepted this tag</returns>
    protected virtual bool AssignTableSpecific(IFilter filter) => false;

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
        FilterBarcode.Value = null;
        FilterStartDate.Value = null;
        FilterEndDate.Value = null;
        FilterResult.Value = null;
        FilterGroup.Value = null;
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