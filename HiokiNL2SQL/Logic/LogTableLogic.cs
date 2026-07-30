// <copyright file="LogTableLogic.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace HiokiNL2SQL.Logic;

using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;
using System.Reflection;

/// <summary>
/// Holds all the methods necessary to store a table.
/// </summary>
/// <typeparam name="T">An implementation of IHiokiLog (defined in LogDbContext).</typeparam>
public class LogTableLogic<T> : ILogTableLogic
    where T : class, IHiokiLog
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LogTableLogic{T}"/> class.
    /// Builds a new LogTableLogic using DB context and necessary services. Adds all relevant filters to the registry based on subtype.
    /// </summary>
    /// <param name="dbFactory">Generates a new LogDbContext for each thread.</param>
    /// <param name="querySelector">Encapsulates the DB connection and query information.</param>
    /// <param name="js">An implementation of <see cref="IJSService"/> for triggering a browser download of a CSV.</param>
    /// <param name="nav">An implementation of <see cref="INavService"/> for navigation to the power search page in a barcode 'drill-down'.</param>
    /// <param name="extraFilters">The collection of extra filters to create (from the child).</param>
    public LogTableLogic(IDbContextFactory<LogDbContext> dbFactory, Func<LogDbContext, IQueryable<T>> querySelector, IJSService js, INavService nav, IEnumerable<IFilter>? extraFilters = null)
    {
        this.DbFactory = dbFactory;
        this.QuerySelector = querySelector;
        this.JS = js;
        this.Nav = nav;

        this.Filters = new (StringComparer.OrdinalIgnoreCase)
        {
            ["barcode"] = new Filter<string?>("barcode", null) { OnChanged = this.NotifyStateChanged },
            ["after"] = new Filter<DateTime?>("after", null) { OnChanged = this.NotifyStateChanged },
            ["before"] = new Filter<DateTime?>("before", null) { OnChanged = this.NotifyStateChanged },
            ["result"] = new Filter<string?>("result", null) { OnChanged = this.NotifyStateChanged },
            ["group"] = new Filter<int?>("group", null) { OnChanged = this.NotifyStateChanged },
        };

        foreach (IFilter f in extraFilters ?? [])
        {
            this.Filters[f.Key] = f;
        }

        foreach (IFilter f in this.Filters.Values)
        {
            f.OnChanged = this.NotifyStateChanged;
        }
    }

    // For compliance with ILogTable (these particular values should never be seen, overridden by children)

    /// <summary>
    /// Gets this table's internal "type" as it would appear in <see cref="PowerSearchLogic.CurrentType"/>.
    /// </summary>
    public virtual string TableName => "unknown";

    /// <summary>
    /// Gets the label to apply to this table in the view.
    /// </summary>
    public virtual string DisplayName => "Unknown Table";

    // Dependencies

    /// <summary>
    /// Gets a new DbContext on demand (thread-safe).
    /// </summary>
    public required IDbContextFactory<LogDbContext> DbFactory { get; init; }

    /// <summary>
    /// Gets the information about the connection and query.
    /// </summary>
    public required Func<LogDbContext, IQueryable<T>> QuerySelector { get; init; }

    /// <summary>
    /// Gets the service responsible for handling CSV download.
    /// </summary>
    public required IJSService JS { get; init; }

    /// <summary>
    /// Gets the service responsible for navigating to the power search page in a barcode "drill-down".
    /// </summary>
    public required INavService Nav { get; init; }

    // Pagination variables

    /// <summary>
    /// Gets or sets the current page number (clamped between 1 and TotalPages, inclusive).
    /// </summary>
    public int CurrentPage { get; set; } = 1;

    /// <summary>
    /// Gets or sets the number of results per page.
    /// </summary>
    public int PageSize { get; set; } = 50;

    /// <summary>
    /// Gets the total number of query results.
    /// </summary>
    public int TotalCount { get; private set; }

    /// <summary>
    /// Gets the page count, computed as total records divided by records per page.
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)this.TotalCount / this.PageSize);

    // For sorting

    /// <summary>
    /// Gets or sets the name of the column by which the results are currently being sorted.
    /// </summary>
    public string CurrentSortColumn { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the sort direction of the currently sorted column.
    /// </summary>
    public string SortDir { get; set; } = "none";

    // Data storage

    /// <summary>
    /// Gets the filter dictionary, updated as necessary by children.
    /// </summary>
    public Dictionary<string, IFilter> Filters { get; private set; } = new (StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a value indicating whether the query is currently loading the table display.
    /// </summary>
    public bool IsLoading { get; private set; } = true;

    /// <summary>
    /// Gets the list of the current page of query results.
    /// </summary>
    public List<T> DataView { get; private set; } = [];

    /// <summary>
    /// Gets the cache of all test modes to choose from.
    /// </summary>
    public HashSet<string> ModeCache { get; private set; } = [];

    /// <summary>
    /// Gets the list of all test result types to choose from.
    /// </summary>
    public HashSet<string> ResultCache { get; private set; } = [];

    // Channels for communicating with the UI

    /// <summary>
    /// Sets the action to take when <see cref="LogTableLogic{T}"/> calls for a refresh.
    /// </summary>
    public Action? OnNotifyUI { private get; set; }

    /// <summary>
    /// Sets the power search URL manipulation to perform when <see cref="LogTableLogic{T}"/> calls for a refresh.
    /// </summary>
    public Action? UpdatePSUrl { private get; set; }

    /// <summary>
    /// Sets a shortcut to the power search execution for a barcode drill-down.
    /// </summary>
    public Action<string>? TriggerPowerSearch { private get; set; }

    // Staleness (for hydration check, to avoid multi-hitting DB)

    /// <summary>
    /// Gets the hash of the filters, sorts, and pagination information used to get the data in <see cref="DataView"/>.
    /// </summary>
    public int? LastQueryHash { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the 'pending' filter state (the filter values shown in the UI) matches the filter state used to get <see cref="DataView"/>.
    /// This value is final and must not be affected by outside sources as it affects when to skip a refresh and can put the program in an impossible state.
    /// If you wish to override the default staleness for a display, use <see cref="UIIsStaleOverride"/>.
    /// </summary>
    public bool IsStale => this.LastQueryHash != this.GetFilterStateHash(this.Filters);

    /// <summary>
    /// Sets the manual override for the VISUAL staleness. If not used, visual status will match <see cref="IsStale"/>.
    /// </summary>
    public Func<bool>? UIIsStaleOverride { private get; set; }

    /// <summary>
    /// Gets a value indicating whether the UI should display with the stale formatting.
    /// This value may be overridden using <see cref="UIIsStaleOverride"/> to set a different format than the 'true' staleness (<see cref="PowerSearchLogic"/>).
    /// </summary>
    public bool UIIsStale => this.UIIsStaleOverride != null ? this.UIIsStaleOverride() : this.IsStale;

    /// <summary>
    /// Generate a state ID, for checking equality between two filter states
    /// 17 and 31 are primes one off of powers of two, so we get few collisions and the compiler can take shortcuts.
    /// </summary>
    /// <param name="filterDict">A dictionary of keys mapped to filters.</param>
    /// <returns>A value representing the state of the filters for the input dictionary.</returns>
    public int GetFilterStateHash(Dictionary<string, IFilter> filterDict)
    {
        // Tells the compiler to simply truncate the calculation instead of throwing an exception for integer overflow
        unchecked
        {
            int hash = 17;

            // Order by key to ensure dictionary order doesn't change the hash
            foreach (string key in filterDict.Keys.OrderBy(k => k))
            {
                // Ignore 'in' key, it does not affect the search contents within a table
                if (key.Equals("in", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Factor in each aspect of the filter
                IFilter filter = filterDict[key];

                // Key and activity status are part of hash regardless of activity status
                hash *= 31 + key.ToLower().GetHashCode();
                hash *= 31 + filter.IsActive.GetHashCode();

                // Only hash filter details if active
                if (filter.IsActive)
                {
                    hash *= 31 + filter.IsNegated.GetHashCode();
                    string value = filter.GetValue()?.ToString()?.Trim().ToLower() ?? string.Empty;
                    hash *= 31 + value.GetHashCode();
                }
            }

            // Sorts and pagination affect view, so a hash of the view should include them
            hash *= 31 + this.CurrentSortColumn.GetHashCode();
            hash *= 31 + this.SortDir.GetHashCode();
            hash *= 31 + this.CurrentPage.GetHashCode();
            hash *= 31 + this.PageSize.GetHashCode();

            return hash;
        }
    }

    /// <summary>
    /// Applies filters and sorts, then reloads the table based on the query and page number
    /// Persists page number if query doesn't change (i.e. when the refresh is just to get the new page).
    /// </summary>
    /// <param name="keepPage">Whether to keep the current page.</param>
    /// <param name="force">Whether to force a refresh even if the cache thinks it is up-to-date.</param>
    /// <returns>A Task representing that the data in <see cref="DataView"/> is current.</returns>
    public async Task RefreshData(bool keepPage = false, bool force = false)
    {
        if (!keepPage)
        {
            this.CurrentPage = 1;
        }

        // Only short-circuit if we're not forcing and the current view appears fresh
        if (!force && this.DataView.Count > 0 && !this.IsStale)
        {
            return;
        }

        this.IsLoading = true;
        this.OnNotifyUI?.Invoke(); // Show loading state

        try
        {
            using LogDbContext db = await this.DbFactory.CreateDbContextAsync(); // One DbContext per refresh
            IQueryable<T> query = this.QuerySelector(db).AsNoTracking();

            query = this.ApplyFilters(query);
            this.TotalCount = await query.CountAsync();
            query = this.ApplySorting(query);

            this.DataView = await query
                .Skip((this.CurrentPage - 1) * this.PageSize)
                .Take(this.PageSize)
                .ToDynamicListAsync<T>();

            this.LastQueryHash = this.GetFilterStateHash(this.Filters);
        }
        finally
        {
            this.IsLoading = false;
            this.OnNotifyUI?.Invoke();
        }
    }

    /// <summary>
    /// Helper to get the strongly-typed version of a generic-typed filter from the registry.
    /// </summary>
    /// <typeparam name="U">The generic type of a Filter, one of string, int, or DateTime.</typeparam>
    /// <param name="key">The key for which to get the filter.</param>
    /// <returns>The Filter with its appropriate type.</returns>
    public Filter<U> GetFilter<U>(string key)
    {
        // 99% will go here
        if (this.Filters.TryGetValue(key, out IFilter? f) && f is Filter<U> typed)
        {
            return typed;
        }

        // But if something slips through the cracks, this will ensure the hash doesn't break
        var newFilter = new Filter<U>(key, default!) { OnChanged = this.NotifyStateChanged };
        this.Filters[key] = newFilter;
        return newFilter;
    }

    /// <summary>
    /// Saves a mapping of search keys to IFilters to the registry, then calls for a refresh
    /// Table-specific filters are handled because their child class has added their key to the registry in InitializeFilters.
    /// </summary>
    /// <param name="filterDict">The dictionary of search keys mapped to filters.</param>
    /// <param name="keepPage">Whether to keep the page number (or reset it).</param>
    /// <returns>A Task representing that the dictionary contents have been copied to <see cref="Filters"/>.</returns>
    public async Task DictionaryToFilters(Dictionary<string, IFilter> filterDict, bool keepPage = false)
    {
        foreach (IFilter existing in this.Filters.Values)
        {
            // Check to see if there's a new filter
            if (filterDict.TryGetValue(existing.Key, out IFilter? incoming))
            {
                existing.CopyFrom(incoming);
            }

            // Otherwise, reset it, as it's not part of this query
            else
            {
                existing.Reset();
            }
        }

        await this.RefreshData(keepPage);
    }

    /// <summary>
    /// Applies the five filters common between all three pages
    /// Overridden in children to apply table-specific filters.
    /// </summary>
    /// <param name="query">The query to which the filters should be appended.</param>
    /// <returns>An IQueryable object with filters applied.</returns>
    public virtual IQueryable<T> ApplyFilters(IQueryable<T> query)
    {
        Filter<string?> barcode = this.GetFilter<string?>("barcode");
        Filter<DateTime?> startDate = this.GetFilter<DateTime?>("after");
        Filter<DateTime?> endDate = this.GetFilter<DateTime?>("before");
        Filter<int?> groupNum = this.GetFilter<int?>("group");
        Filter<string?> result = this.GetFilter<string?>("result");

        if (barcode.IsActive && !string.IsNullOrWhiteSpace(barcode.Value))
        {
            query = barcode.IsNegated
                ? query.Where(x => x.Barcode != null && !x.Barcode.Contains(barcode.Value))
                : query.Where(x => x.Barcode != null && x.Barcode.Contains(barcode.Value));
        }

        // Can't negate date checks, so ignore negation status
        if (startDate.IsActive)
        {
            query = query.Where(x => x.Time >= startDate.Value);
        }

        // The semantics of the word "before" are tricky and depend on whether a time was specified, but that's handled in the parser now
        if (endDate.IsActive)
        {
            query = query.Where(x => x.Time <= endDate.Value);
        }

        if (groupNum.IsActive)
        {
            query = groupNum.IsNegated
                ? query.Where(x => x.Group != groupNum.Value)
                : query.Where(x => x.Group == groupNum.Value);
        }

        if (result.IsActive)
        {
            query = result.IsNegated
                ? query.Where(x => x.Result != result.Value)
                : query.Where(x => x.Result == result.Value);
        }

        return query;
    }

    /// <summary>
    /// Uses dynamic LINQ to draft a SQL ORDER BY based on the current sort.
    /// </summary>
    /// <param name="query">The query to which the sorts should be appended.</param>
    /// <returns>An IQueryable object with sorts applied.</returns>
    public IQueryable<T> ApplySorting(IQueryable<T> query)
    {
        if (this.SortDir == "none" || string.IsNullOrWhiteSpace(this.CurrentSortColumn))
        {
            return query.OrderBy("Time descending"); // Default
        }

        return query.OrderBy($"{this.CurrentSortColumn} {this.SortDir}"); // Dynamic LINQ is cool
    }

    /// <summary>
    /// Detects the table, then saves the results of the query on that table to a CSV
    /// Uses JSService (indirectly uses JS Runtime) to download directly to browser Downloads location.
    /// </summary>
    /// <returns>A Task representing that the browser download has started.</returns>
    public async Task SaveToCSV()
    {
        Type targetType = typeof(T);
        PropertyInfo[] properties = targetType.GetProperties();
        var csvBuilder = new System.Text.StringBuilder();

        // Header
        csvBuilder.AppendLine(string.Join(",", properties.Select(p => p.Name)));

        // Re run the current query with the current filters and sorts
        using LogDbContext db = await this.DbFactory.CreateDbContextAsync();
        IQueryable<T> query = this.QuerySelector(db).AsNoTracking();
        query = this.ApplyFilters(query);
        query = this.ApplySorting(query);
        List<T> allData = await query.ToListAsync();

        // Loop through each row, parse, then pass to the CSV builder
        foreach (T? item in allData)
        {
            IEnumerable<string> values = properties.Select(p =>
            {
                string val = p.GetValue(item)?.ToString() ?? string.Empty;

                // CSV escaping: wrap in quotes if contains comma, newline, or quotes
                if (val.Contains(',') || val.Contains('"') || val.Contains('\n') || val.Contains('\r'))
                {
                    val = $"\"{val.Replace("\"", "\"\"")}\"";
                }

                return val;
            });
            csvBuilder.AppendLine(string.Join(",", values));
        }

        // Call JS Runtime to perform the download
        string fileName = $"{targetType.Name}s_{DateTime.Now:yyyyMMdd_HHmm}.csv";
        await this.JS.DownloadCsv(fileName, csvBuilder.ToString());
    }

    /// <summary>
    /// Using the NavigationManager embedded in NavService, navigates to the power search page upon selecting a barcode to pursue.
    /// </summary>
    /// <param name="barcode">The barcode to trace.</param>
    public void HandleBarcodeClick(string barcode)
    {
        this.Nav.EnsureSubscribed();
        string query = $"in:all barcode:{barcode}";

        // Redirect to the PowerSearch page to view all results
        if (this.OnNotifyUI != null)
        {
            this.TriggerPowerSearch?.Invoke(query);
        }
        else
        {
            this.Nav.NavigateToBarcodeTrace(barcode);
        }
    }

    /// <summary>
    /// Call to tell the UI to re-render.
    /// </summary>
    public void NotifyStateChanged() => this.OnNotifyUI?.Invoke();

    /// <summary>
    /// Jumps to the specified new page.
    /// </summary>
    /// <param name="newPage">The page number to jump to.</param>
    /// <returns>A Task representing that the page has been changed.</returns>
    public async Task ChangePage(int newPage)
    {
        if (newPage != this.CurrentPage && newPage >= 1 && newPage <= this.TotalPages)
        {
            this.CurrentPage = newPage;
            await this.RefreshData(keepPage: true, force: true);

            // Only push the URL when the user is on the power search page.
            // Otherwise, every page change will redirect to the power search page.
            if (this.Nav.IsOnPowerSearchPage())
            {
                this.UpdatePSUrl?.Invoke();
            }
        }
    }

    /// <summary>
    /// Modifies the page size from PageSize to newSize.
    /// </summary>
    /// <param name="newSize">The desired number of entries per page.</param>
    /// <returns>A Task representing that the page size has been changed.</returns>
    public async Task AlterPageSize(int newSize)
    {
        if (newSize != this.PageSize)
        {
            this.PageSize = newSize;

            // Reset to page 1 because the number of pages has changed (thus the old page number is meaningless)
            this.CurrentPage = 1;
            await this.RefreshData(force: true);
            if (this.Nav.IsOnPowerSearchPage())
            {
                this.UpdatePSUrl?.Invoke();
            }
        }
    }

    /// <summary>
    /// Cycles through sort directions when column is toggled
    /// Cycle order: None -> Asc -> Desc.
    /// </summary>
    /// <param name="columnName">The column to be toggled.</param>
    /// <returns>A Task representing that the sort has been toggled.</returns>
    public async Task ToggleSort(string columnName)
    {
        if (this.CurrentSortColumn != columnName)
        { // If coming from none, save the column name (it's changed) and switch to asc
            this.CurrentSortColumn = columnName;
            this.SortDir = "ascending";
        }
        else if (this.SortDir == "ascending")
        { // If coming from asc, only need to switch to desc
            this.SortDir = "descending";
        }
        else
        { // If coming from desc, switch to none and inform model no column is specified to sort
            this.SortDir = "none";
            this.CurrentSortColumn = string.Empty;
        }

        await this.RefreshData(force: true); // because the sort parameters change we want a guaranteed refresh
        if (this.Nav.IsOnPowerSearchPage())
        {
            this.UpdatePSUrl?.Invoke();
        }
    }

    /// <summary>
    /// Helper to render the arrow.
    /// </summary>
    /// <param name="columnName">The column for which to update the sort icon.</param>
    /// <returns>The Unicode arrow representing the sort direction.</returns>
    public string GetSortIcon(string columnName)
    {
        if (this.CurrentSortColumn != columnName || this.SortDir == "none")
        {
            return "↕";
        }

        return this.SortDir == "ascending" ? "▲" : "▼";
    }

    /// <summary>
    /// Initializes the test mode and result type caches (for step and FCT tables).
    /// </summary>
    /// <returns>A Task representing that the caches have been loaded.</returns>
    public virtual async Task InitializeCaches()
    {
        // If the result cache is full, don't reload it (this destroys the point of a cache)
        if (this.LastQueryHash != null && this.ResultCache.Count != 0)
        {
            return;
        }

        // Fill the final result cache for all tables
        this.ResultCache = await this.GetCacheSet("Result");

        if (typeof(IStepFct).IsAssignableFrom(typeof(T)))
        {
            this.ModeCache = await this.GetCacheSet("Mode");
        }
    }

    /// <summary>
    /// Resets all filters in the registry, and optionally, the page.
    /// </summary>
    /// <param name="keepPage">Whether to keep the current page number.</param>
    public void ResetFilterState(bool keepPage = false)
    {
        foreach (IFilter f in this.Filters.Values)
        {
            f.Reset();
        }

        if (!keepPage)
        {
            this.CurrentPage = 1;
        }
    }

    /// <summary>
    /// Resets filters AND reloads the query
    /// Only bind to the CLEAR button.
    /// </summary>
    /// <returns>A Task representing that the filters have been cleared.</returns>
    public async Task ClearFilters()
    {
        this.ResetFilterState();
        await this.RefreshData();
    }

    /// <summary>
    /// Resets the entire model without refreshing (prepare for new query).
    /// </summary>
    public void ClearData()
    {
        this.DataView = [];
        this.TotalCount = 0;
        this.CurrentPage = 1;
        this.ResetFilterState();
    }

    /// <summary>
    /// Gets a set of all values across a property.
    /// </summary>
    /// <param name="property">The property for which to get unique values.</param>
    /// <returns>A list of uniquer property values.</returns>
    protected async Task<HashSet<string>> GetCacheSet(string property)
    {
        using LogDbContext db = await this.DbFactory.CreateDbContextAsync();
        List<string> list = await this.QuerySelector(db).AsNoTracking()
            .Select(property)
            .Distinct()
            .OrderBy("it")
            .ToDynamicListAsync<string>();
        return [.. list];
    }
}
