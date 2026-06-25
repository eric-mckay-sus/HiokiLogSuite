// <copyright file="PowerSearchLogic.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace HiokiNL2SQL.Logic;

using System.Text.RegularExpressions;

using HiokiNL2SQL.Services;

/// <summary>
/// The methods and state necessary to run and display a power search.
/// </summary>
public partial class PowerSearchLogic()
{
    /// <summary>
    /// The key-value pairs parsed from <see cref="CommandInput"/>.
    /// </summary>
    private Dictionary<string, IFilter> filters = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PowerSearchLogic"/> class.
    /// Constructs a new power search engine from the necessary services, and wires the table engines to this display.
    /// </summary>
    /// <param name="tableLogics">The list of table engines to use.</param>
    /// <param name="parserService">The service to parse command input.</param>
    /// <param name="navService">The navigation manager for URL use and manipulation.</param>
    /// <param name="jsService">The JS runtime to control cursor focus.</param>
    public PowerSearchLogic(IEnumerable<ILogTableLogic> tableLogics, SearchParserService parserService, INavService navService, IJSService jsService)
        : this()
    {
        this.TableLogics = tableLogics;
        this.ParserService = parserService;
        this.NavService = navService;
        this.JSService = jsService;

        // Wire each table's notification to this class
        foreach (ILogTableLogic table in this.TableLogics)
        {
            table.OnNotifyUI = this.NotifyStateChanged;
            table.UpdatePSUrl = this.SyncUrl;

            // UI stale state for the table comes from a simple comparison
            // between the current search bar text and the tab name (minus the
            // "Search: " prefix).  This boolean is purely for visual feedback.
            table.UIIsStaleOverride = () =>
                this.CommandInput.Trim() != this.LastExecutedQuery.Replace("Search: ", string.Empty);

            // When a table requests a power-search (e.g., barcode drill-down),
            // update the URL and also execute the search locally so behavior
            // matches clicking the Power Search tab.
            table.TriggerPowerSearch = (query) =>
            {
                try
                {
                    this.NavService.UpdateSearchState(query);
                }
                catch
                {
                }

                _ = this.ExecutePowerSearch(skipUrlUpdate: true);
            };
        }
    }

    /// <summary>
    /// The action to perform when the view requests a refresh.
    /// </summary>
    public event Action? OnRefreshRequested;

    /// <summary>
    /// Gets or sets the table to check.
    /// </summary>
    public string CurrentType { get; set; } = "all";

    /// <summary>
    /// Gets or sets the input from the search bar.
    /// </summary>
    public string CommandInput { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable preview of the query to be executed.
    /// </summary>
    public string Preview { get; set; } = string.Empty;

    /// <summary>
    /// Gets the text of the error message, if applicable.
    /// </summary>
    public List<string> ErrorMessages { get; private set; } = [];

    /// <summary>
    /// Gets the list of available date aliases.
    /// </summary>
    public string[] DateAliases { get; } = ["today", "yesterday", "last24h", "shift1", "shift2", "shift3",];

    /// <summary>
    /// Gets the timer to debounce <see cref="CommandInput"/> and smooth the preview rendering.
    /// </summary>
    public System.Timers.Timer? DebounceTimer { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the system is using NavService within this class or from the razor page.
    /// </summary>
    public bool IsInternalNavigation { get; set; } = false;

    /// <summary>
    /// Gets a value indicating whether the system is currently getting query results.
    /// </summary>
    public bool IsSearching { get; private set; } = false;

    /// <summary>
    /// Gets a value indicating whether date filters are inclusive (or exclusive).
    /// </summary>
    public bool IsInclusive { get; private set; } = true;

    /// <summary>
    /// Gets the count of all results, across all three tables.
    /// </summary>
    public int AllCount => this.TableLogics.Sum(t => t.TotalCount);

    /// <summary>
    /// Gets the details of the last executed query, for display in the tab name.
    /// </summary>
    public string LastExecutedQuery { get; private set; } = "Hioki ICT Power Search";

    /// <summary>
    /// Gets the list of logic engines used to perform the searches.
    /// </summary>
    public required IEnumerable<ILogTableLogic> TableLogics { get; init; }

    /// <summary>
    /// Gets the service to which command input will be passed to compile the filter dictionary.
    /// </summary>
    public required SearchParserService ParserService { get; init; }

    /// <summary>
    /// Gets the service responsible for controlling the navigation between filter state URLs.
    /// </summary>
    public required INavService NavService { get; init; }

    /// <summary>
    /// Gets the service responsible for controlling cursor focus when applying quick select tags.
    /// </summary>
    public required IJSService JSService { get; init; }

    /// <summary>
    /// Performant helper to replace multiple spaces with one.
    /// </summary>
    /// <param name="input">The string for which to normalize whitespace.</param>
    /// <returns>The input string with spaces normalized.</returns>
    public static string NormalizeWhiteSpace(string input)
    {
        int len = input.Length,
            index = 0,
            i = 0;
        char[] src = input.ToCharArray();
        bool skip = false;
        char ch;
        for (; i < len; i++)
        {
            ch = src[i];
            switch (ch)
            {
                case '\u0020':
                case '\u00A0':
                case '\u1680':
                case '\u2000':
                case '\u2001':
                case '\u2002':
                case '\u2003':
                case '\u2004':
                case '\u2005':
                case '\u2006':
                case '\u2007':
                case '\u2008':
                case '\u2009':
                case '\u200A':
                case '\u202F':
                case '\u205F':
                case '\u3000':
                case '\u2028':
                case '\u2029':
                case '\u0009':
                case '\u000A':
                case '\u000B':
                case '\u000C':
                case '\u000D':
                case '\u0085':
                    if (skip)
                    {
                        continue;
                    }

                    src[index++] = ch;
                    skip = true;
                    continue;
                default:
                    skip = false;
                    src[index++] = ch;
                    continue;
            }
        }

        return new string(src, 0, index);
    }

    /// <summary>
    /// When the state changes, tell the view to do what it usually does for an update.
    /// </summary>
    public void NotifyStateChanged() => this.OnRefreshRequested?.Invoke();

    /// <summary>
    /// Parse search bar input, update the model, then tell the view.
    /// </summary>
    /// <param name="skipUrlUpdate">Whether to apply the URL state to the table (or the table state to the URL).</param>
    /// <param name="keepPage">Whether to keep the current page through the refresh (pass to <see cref="ILogTableLogic.RefreshData(bool, bool)"/>).</param>
    /// <returns>A Task representing that the search has been executed.</returns>
    public async Task ExecutePowerSearch(bool skipUrlUpdate = false, bool keepPage = false)
    {
        this.ErrorMessages = []; // Clear errors, they shouldn't persist through searches

        // Identify tables targeted by this query based on CurrentType
        IEnumerable<ILogTableLogic> targets = this.TableLogics
            .Where(t => this.CurrentType == "all" || t.TableName.Equals(this.CurrentType, StringComparison.OrdinalIgnoreCase));

        SearchParseResult parseResult = this.ParserService.ParseQuery(this.CommandInput, this.CurrentType, this.IsInclusive);

        this.filters = parseResult.Filters;
        this.ErrorMessages = parseResult.ErrorMessages;
        this.CurrentType = parseResult.CurrentType;
        this.Preview = parseResult.Preview;

        // If the user hasn't entered any search text (and parser returned no filters),
        // we want to preserve the splash screen and avoid querying the database at all.
        // This is the situation that occurs on first render of power search.
        if (string.IsNullOrWhiteSpace(this.CommandInput) && this.filters.Count == 0)
        {
            foreach (ILogTableLogic table in this.TableLogics)
            {
                table.ClearData();
            }

            this.LastExecutedQuery = "Hioki ICT Power Search";
            this.IsSearching = false;
            this.NotifyStateChanged();
            return; // short-circuit before any DB hits
        }

        // Detect fatal versus non-fatal errors
        bool hasFatalErrors = this.ErrorMessages.Any(m => !m.Contains("This search", StringComparison.OrdinalIgnoreCase));

        // If there was an fatal error, don't execute the search (warnings ok)
        if (hasFatalErrors)
        {
            foreach (ILogTableLogic table in this.TableLogics)
            {
                table.ClearData();
            }

            this.IsSearching = false;
            this.NotifyStateChanged();
            return;
        }

        if (parseResult.HasDateFilter)
        {
            // Replace date/shift aliases in the raw command input with their resolved datetimes
            // The filters already have resolved DateTime values from ProcessDateValue, so use those directly
            if (parseResult.Filters.TryGetValue("before", out IFilter? beforeFilter) && beforeFilter is Filter<DateTime?> bf && bf.Value.HasValue)
            {
                string replacement = bf.Value.Value.ToString("yyyy-MM-dd HH:mm:ss");
                this.CommandInput = Regex.Replace(this.CommandInput, SearchParserService.BeforePattern, $"before:\"{replacement}\"", RegexOptions.IgnoreCase);
            }

            if (parseResult.Filters.TryGetValue("after", out IFilter? afterFilter) && afterFilter is Filter<DateTime?> af && af.Value.HasValue)
            {
                string replacement = af.Value.Value.ToString("yyyy-MM-dd HH:mm:ss");
                this.CommandInput = Regex.Replace(this.CommandInput, SearchParserService.AfterPattern, $"after:\"{replacement}\"", RegexOptions.IgnoreCase);
            }
        }

        try
        {
            // Parallelize search
            this.IsSearching = true;
            await Task.WhenAll(targets.Select(t => t.DictionaryToFilters(this.filters, keepPage)));
        }
        catch (Exception ex)
        {
            this.ErrorMessages.Add($"Search failed: {ex.Message}");
        }
        finally
        {
            this.IsSearching = false;
            if (!skipUrlUpdate)
            {
                this.SyncUrl();
            }

            this.LastExecutedQuery = string.IsNullOrWhiteSpace(this.CommandInput) ? "Hioki ICT Power Search" : $"Search: {this.CommandInput}";
            this.NotifyStateChanged();
        }
    }

    /// <summary>
    /// Updates the URL query string based on the current state of the active table
    /// without triggering a database refresh.
    /// </summary>
    public void SyncUrl()
    {
        ILogTableLogic? activeTable = this.TableLogics.FirstOrDefault(t =>
            t.TableName.Equals(this.CurrentType, StringComparison.OrdinalIgnoreCase));

        this.IsInternalNavigation = true;

        if (activeTable != null && this.CurrentType != "all")
        {
            this.NavService.UpdateSearchState(
            this.CommandInput,
            activeTable.CurrentPage,
            activeTable.PageSize,
            activeTable.CurrentSortColumn,
            activeTable.SortDir,
            replaceHistory: true);
        }
        else
        {
            // If we are in "all" mode, null out optional keys (null by default) to demonstrate to the user that they are ignored.
            // NavManager.GetUriWithQueryParameters will strip them from the existing URL.
            this.NavService.UpdateSearchState(this.CommandInput);
        }
    }

    /// <summary>
    /// Helper for quick select date aliases.
    /// </summary>
    /// <param name="key">The key to append.</param>
    /// <param name="shortcut">The shortcut to append.</param>
    /// <returns>A Task representing that the shortcut has been appended.</returns>
    public async Task AppendShortcut(string key, string shortcut)
    {
        string toAppend = $"{key}:{shortcut} ";

        // Short-circuit if search bar is empty
        if (string.IsNullOrEmpty(this.CommandInput))
        {
            this.CommandInput = toAppend;
            await this.JSService.FocusElement("searchBar");
            this.NotifyStateChanged();
            return;
        }

        // If the shortcut is already touching a tag or the key/shortcut is already present in the search bar, only add the shortcut
        if (this.CommandInput.Contains(shortcut) || this.CommandInput.Contains($"{key}:") || this.CommandInput.TrimEnd()[^1] == ':')
        {
            toAppend = $"{shortcut} ";
        }

        // technically unnecessary to auto-space here bc the parser is smart enough, but it's easier for the user to read
        else
        {
            toAppend = ' ' + toAppend;
        }

        // Append the shortcut
        this.CommandInput = this.CommandInput.TrimEnd() + toAppend;
        this.SyncLivePreview();
        await this.JSService.FocusElement("searchBar");
    }

    /// <summary>
    /// Helper for quick select table tabs. Automatically runs a search for the target table.
    /// </summary>
    /// <param name="toType">The target table.</param>
    /// <returns>A Task representing that the type has been set.</returns>
    public async Task SetType(string toType)
    {
        if (toType == this.CurrentType)
        {
            return; // Don't waste time performing an action that does nothing
        }

        // If the search already has an "in" tag, replace it
        if (SearchParserService.ApplyInPattern().IsMatch(this.CommandInput))
        {
            this.CommandInput = SearchParserService.ApplyInPattern().Replace(this.CommandInput, $"in:{toType}");
        }
        else
        { // otherwise, just append it
            await this.AppendKey("in", true);
            this.CommandInput += toType;
        }

        this.CurrentType = toType;
        this.SyncUrl(); // technically called inside ExecutePowerSearch but we do it here to avoid the wait
        await this.ExecutePowerSearch();
    }

    /// <summary>
    /// Sets the inclusivity of the date filters to <paramref name="newVal"/>.
    /// </summary>
    /// <param name="newVal">The new value for <see cref="IsInclusive"/>.</param>
    /// <returns>A Task representing that the search has been re-executed with the new date inclusivity.</returns>
    public async Task SetInclusivity(bool newVal)
    {
        this.IsInclusive = newVal;
        await this.ExecutePowerSearch();
    }

    /// <summary>
    /// Helper for search bar X button.
    /// </summary>
    public void ClearSearchBar()
    {
        this.CommandInput = string.Empty;
        this.filters = [];
        this.ErrorMessages = [];
        this.SyncLivePreview(); // calls NotifyStateChanged internally
    }

    /// <summary>
    /// Restarts the debounce timer (for when new input is received).
    /// </summary>
    public void RestartDebounceTimer()
    {
        // Ensure the preview updates immediately on input
        this.SyncLivePreview();

        // Reset the timer
        this.DebounceTimer?.Stop();
        this.DebounceTimer?.Start();
    }

    /// <summary>
    /// Updates the live preview based on current search bar contents.
    /// </summary>
    public void SyncLivePreview()
    {
        SearchParseResult liveResult = this.ParserService.ParseQuery(this.CommandInput, this.CurrentType, this.IsInclusive);
        this.Preview = liveResult.Preview;

        // Update the CurrentType if the user entered the "in" tag
        if (!string.IsNullOrEmpty(liveResult.CurrentType))
        {
            this.CurrentType = liveResult.CurrentType;
        }

        this.NotifyStateChanged();
    }

    /// <summary>
    /// Toggles color depending on whether the input key is present in the search bar
    /// If multiple of this key are present, color matches the last one to reflect duplicates resolving to last instance.
    /// </summary>
    /// <param name="key">The tag to check against the search bar contents.</param>
    /// <param name="thisMode">Whether the tag is active in this mode (lighter styling).</param>
    /// <returns>The badge style reflecting its activation state.</returns>
    public string GetBadgeClass(string key, bool thisMode)
    {
        // Get the last occurrence of the target key proceeded by a colon (to avoid false triggers for values)
        int lastIndex = this.CommandInput.LastIndexOf($"{key}:", StringComparison.OrdinalIgnoreCase);

        // Verify that key was found
        if (lastIndex != -1)
        {
            // Highlight red for negative presence (-key:) (skip index 0 to avoid out of bounds)
            if (lastIndex != 0 && this.CommandInput[lastIndex - 1] == '-')
            {
                return thisMode ? "search-tag is-active negative" : "search-tag is-active-tertiary negative";
            }

            // Highlight blue for positive presence (key:)
            else
            {
                return thisMode ? "search-tag is-active" : "search-tag is-active-tertiary";
            }
        }

        // If key not found in the search bar, revert to respective default (inactive) states
        else
        {
            return thisMode ? "search-tag secondary" : "search-tag tertiary";
        }
    }

    /// <summary>
    /// Removes a key and its associated value from the query.
    /// </summary>
    /// <param name="key">The key to remove the tag for.</param>
    public void RemoveTagFromQuery(string key)
    {
        if (string.IsNullOrWhiteSpace(this.CommandInput))
        {
            return; // in the case this method is somehow triggered without any filters
        }

        // Pattern handles: -?key: followed by ("quoted value" OR unquotedValue)
        string pattern = $@"-?{key}:(""[^""]*""|[^\s]*)";

        this.CommandInput = Regex.Replace(this.CommandInput, pattern, string.Empty, RegexOptions.IgnoreCase).Trim();

        // Clean up double spaces
        this.CommandInput = NormalizeWhiteSpace(this.CommandInput);

        // If removing the 'in' tag, release the type it had set
        if (key.Contains("in"))
        {
            this.CurrentType = "all";
        }

        // If the input is now empty, clear the results entirely
        if (string.IsNullOrEmpty(this.CommandInput))
        {
            foreach (ILogTableLogic table in this.TableLogics)
            {
                table.ClearData();
            }
        }

        this.SyncLivePreview(); // calls NotifyStateChanged internally
    }

    /// <summary>
    /// Appends the input key to the search bar if not currently there.
    /// </summary>
    /// <param name="key">The tag to be appended to the search.</param>
    /// <param name="fromTableTab">whether this call is from a table tab (or from the quick select options).</param>
    /// <returns>Whether the key was appended (or if it was already in the. </returns>
    public async Task<bool> AppendKey(string key, bool fromTableTab)
    {
        // Check if the key is already present (case-insensitive)
        if (this.CommandInput.Contains(key, StringComparison.OrdinalIgnoreCase))
        {
            return false; // Do nothing; we don't want to duplicate it
        }

        // If the search bar is currently empty, append right away, otherwise ensure there is a space between the current last character and the key
        this.CommandInput = string.IsNullOrWhiteSpace(this.CommandInput) ? $"{key}:" : $"{this.CommandInput.TrimEnd()} {key}:";

        // Trigger the JS helper to put the cursor back in the box if using quick select options
        if (!fromTableTab)
        {
            await this.JSService.FocusElement("searchBar");
        }

        this.SyncLivePreview(); // calls NotifyStateChanged internally
        return true;
    }
}
