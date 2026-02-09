using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using System.Linq.Dynamic.Core;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;

namespace HiokiNL2SQLMark1.Logic;

/// <summary>
/// Holds all the methods necessary to store a table
/// </summary>
/// <typeparam name="T">An implementation of IHiokiLog (defined in LogDbContext)</typeparam>
public class LogTableLogic<T>(IDbContextFactory<LogDbContext> dbFactory, Func<LogDbContext, IQueryable<T>> querySelector, IJSRuntime js, NavigationManager navManager) where T : class, IHiokiLog
{
    private readonly IDbContextFactory<LogDbContext> _dbFactory = dbFactory; // generates a new DbContext on demand (thread-safe)
    private readonly Func<LogDbContext, IQueryable<T>> _querySelector = querySelector; // denotes the connection and query information
    protected readonly IJSRuntime JS = js; // for handling CSV download
    protected readonly NavigationManager Nav = navManager;

    // Shared filters
    public Filter<string?> FilterBarcode = new(null);
    public Filter<DateTime?> FilterStartDate = new(null);
    public Filter<DateTime?> FilterEndDate = new(null);
    public Filter<string?> FilterResult = new(null);
    public Filter<int?> FilterGroup = new(null);

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
    public bool _isLoading;
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

        _isLoading = true;
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
        _isLoading = false;
    }

    /// <summary>
    /// Applies the five filters common between all three pages
    /// </summary>
    /// <param name="query">The query to which the filters should be appended</param>
    /// <returns>An IQueryable object with filters applied</returns>
    public virtual IQueryable<T> ApplyFilters(IQueryable<T> query)
    {
        if (FilterBarcode.Value != null)
            query = FilterBarcode.IsNegated
                ? query.Where(x => !x.Barcode.Contains(FilterBarcode.Value))
                : query.Where(x => x.Barcode.Contains(FilterBarcode.Value));

        if (FilterStartDate != null && FilterStartDate.Value.HasValue)
            query = query.Where(x => x.Time >= FilterStartDate.Value.Value); // .Value accesses the filter, then converts from DateTime? (nullable) to DateTime (not null)

        if (FilterEndDate != null && FilterEndDate.Value.HasValue) // The semantics of the word "before" are tricky and depend on whether a time was specified
            if (FilterEndDate.Value.Value.TimeOfDay == TimeSpan.Zero) // If the datetime has midnight as the time part, that means only the date part was provided by the user
            {
                query = query.Where(x => x.Time < FilterEndDate.Value.Value.AddDays(1)); // this is inclusive of all times on the end date
            }
            else // Otherwise, use the time provided by the user as a hard stop
            {
                query = query.Where(x => x.Time <= FilterEndDate.Value.Value); // this stops exactly at the time specified
            }

        if (FilterGroup.Value != null)
            query = FilterGroup.IsNegated
                ? query = query.Where(x => x.Group != FilterGroup.Value)
                : query = query.Where(x => x.Group == FilterGroup.Value);

        if (FilterResult.Value != null)
            query = FilterResult.IsNegated
                ? query = query.Where(x => x.Result != FilterResult.Value)
                : query = query.Where(x => x.Result == FilterResult.Value);

        return query;
    }

    /// <summary>
    /// Maps a dictionary of filter key-value pairs to the individual filter properties
    /// </summary>
    /// <param name="filterDict">The dictionary containing filter keys and values</param>
    /// <returns></returns>
    public virtual async Task ApplyFiltersFromDictionary(Dictionary<string, string> filterDict)
    {
        // Ensure no old filters persist
        ResetFilterState();
        
        AssignBaseFilters(filterDict);

        await RefreshData();
    }

    /// <summary>
    /// Assigns the base filters without triggering a refresh so individual tables can call for common values
    /// </summary>
    /// <param name="filterDict">The dictionary of search tags mapped to values</param>
    protected void AssignBaseFilters(Dictionary<string, string> filterDict)
    {
        foreach (var (key, value) in filterDict)
        {
            bool isNegated = key.StartsWith('-');
            string cleanKey = isNegated ? key[1..] : key;
            switch (cleanKey.ToLower())
            {
                case "barcode":
                    FilterBarcode.Value = value;
                    FilterBarcode.IsNegated = isNegated; break;
                case "result":
                    FilterResult.Value = value;
                    FilterResult.IsNegated = isNegated; break;
                case "group" when int.TryParse(value, out var i): 
                    FilterGroup.Value = i;
                    FilterGroup.IsNegated = isNegated; break;

                // No need to assign IsNegated value for DateTimes because they don't support it
                case "after":
                    FilterStartDate.Value = LogTableLogic<T>.ResolveFullDateTime(value);
                    break;
                case "before":
                    FilterEndDate.Value = LogTableLogic<T>.ResolveFullDateTime(value);
                    break;
            }
        }
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
        Nav.NavigateTo($"/?q={Uri.EscapeDataString(query)}");
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
        FilterBarcode = new(null);
        FilterStartDate = new(null);
        FilterEndDate = new(null);
        FilterGroup = new(null);
        FilterResult = new(null);
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