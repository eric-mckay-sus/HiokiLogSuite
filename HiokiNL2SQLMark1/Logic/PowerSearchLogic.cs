using System.Text.RegularExpressions;
using HiokiNL2SQLMark1.Services;

namespace HiokiNL2SQLMark1.Logic;
/// <summary>
/// The methods and state necessary to run and display a power search
/// </summary>
public class PowerSearchLogic()
{
    public string CurrentType = "all"; // the table to check
    public string commandInput = ""; // the input from the search bar
    public string Preview = ""; // the human-readable preview of the query to be executed
    public List<string> errorMessages = []; // the text of the error message, if applicable
    public readonly string[] dateAliases = ["today", "yesterday", "last24h", "shift1", "shift2", "shift3",]; // the list of available date aliases
    public Dictionary<string, IFilter> Filters = []; // the key-value pairs parsed from command input
    public System.Timers.Timer? DebounceTimer; // to smooth the preview rendering
    public bool IsProcessingNavigation; // Whether the system is currently navigating to a new page (so it can't interrupt itself)
    public bool IsSearching; // Whether the system is currently getting query results
    public bool IsStale => commandInput != LastExecutedQuery.Replace("Search: ", ""); // Whether the search bar contents match the table(s) shown. WILL BREAK IF TAB NAME CHANGES IN THE FUTURE
    public int AllCount => TableLogics.Sum(t => t.TotalCount); // The count of all results, across all three tables
    public string LastExecutedQuery = "Hioki ICT Power Search"; // The details of the last executed query, for display in the tab name

    public readonly IEnumerable<ILogTableLogic> TableLogics; // The list of logic engines to perform the searches
    private readonly SearchParserService ParserService; // The service to which command input will be passed to get a filter dictionary back
    public readonly INavService NavService; // Controls the navigation between URLs constructed from modifying filters
    private readonly IJSService JSService; // Controls cursor focus when applying quick select tags

    public event Action? OnRefreshRequested; // The trigger for the view (implemented in the view)
    public void NotifyStateChanged() => OnRefreshRequested?.Invoke(); // The method to trigger a refresh in the view

    /// <summary>
    /// Constructs a new power search engine from the necessary parts, and wires the table engines to this display.
    /// </summary>
    /// <param name="tableLogics">The list of table engines to use</param>
    /// <param name="parserService">The service to parse command input</param>
    /// <param name="navManager">The navigation manager for URL use and manipulation</param>
    /// <param name="jsRuntime">The JS runtime to control cursor focus</param>
    public PowerSearchLogic(IEnumerable<ILogTableLogic> tableLogics, SearchParserService parserService, INavService navService, IJSService jsService) : this()
    {
        TableLogics = tableLogics;
        ParserService = parserService;
        NavService = navService;
        JSService = jsService;

        // Wire each table's notification to this class
        foreach (var table in TableLogics)
        {
            table.OnNotifyUI = NotifyStateChanged;
            table.UpdatePSUrl = SyncUrl;
            table.IsStaleOverride = () => IsStale;
            table.TriggerPowerSearch = (query) => NavService.UpdateSearchState(query);
        }
    }

    /// <summary>
    /// Parse search bar input, update the model, then tell the view  
    /// </summary>
    /// <param name="SkipUrlUpdate"></param>
    /// <returns></returns>
    public async Task ExecutePowerSearch(bool skipUrlUpdate=false, bool keepPage=false)
    {
        // Identify tables targeted by this query based on CurrentType
        var targets = TableLogics
            .Where(t => CurrentType == "all" || t.TableName.Equals(CurrentType, StringComparison.OrdinalIgnoreCase));

        var parseResult = ParserService.ParseQuery(commandInput, CurrentType);

        Filters = parseResult.Filters;
        errorMessages = parseResult.ErrorMessages;
        CurrentType = parseResult.CurrentType;
        Preview = parseResult.Preview;

        // Detect fatal versus non-fatal errors
        bool hasFatalErrors = errorMessages.Any(m => !m.Contains("This search", StringComparison.OrdinalIgnoreCase));
        if (hasFatalErrors) // if there was an fatal error, don't execute the search (warnings ok)
        {
            foreach (var table in TableLogics) table.ClearData();
            IsSearching = false;
            NotifyStateChanged();
            return;
        }

        // Now we know some change will be made, regardless of whether the DB is hit
        LastExecutedQuery = string.IsNullOrWhiteSpace(commandInput) ? "Hioki ICT Power Search" : $"Search: {commandInput}";
        var targetTable = TableLogics.FirstOrDefault(t => t.TableName.Equals(CurrentType, StringComparison.OrdinalIgnoreCase));

        // Verify that the target is exactly one table, otherwise we're forced to run the query
        if (targetTable != null && CurrentType != "all")
        {
            // Pre-flight check: Hash the PARSED results
            int prospectiveHash = targetTable.GetFilterStateHash(Filters);

            // Compare to target table's LAST SUCCESSFUL execution hash
            if (targetTable.LastQueryHash == prospectiveHash && targetTable.TotalCount > 0)
            {
                NotifyStateChanged();
                return; // skip DB
            }
        }

        // If we're at this point, we prepare to execute the query
        IsSearching = true;

        try{
            // Parallelize search
            await Task.WhenAll(targets.Select(t => t.DictionaryToFilters(Filters, keepPage)));
        } catch (Exception ex){
            errorMessages.Add($"Search failed: {ex.Message}");
        } finally{
            IsSearching = false;
            if(!skipUrlUpdate) SyncUrl();
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Updates the URL query string based on the current state of the active table 
    /// without triggering a database refresh.
    /// </summary>
    public void SyncUrl()
    {
        var activeTable = TableLogics.FirstOrDefault(t => 
            t.TableName.Equals(CurrentType, StringComparison.OrdinalIgnoreCase));

        if (activeTable != null && CurrentType != "all")
        {
            NavService.UpdateSearchState(
            commandInput, 
            activeTable.CurrentPage, 
            activeTable.PageSize, 
            activeTable.CurrentSortColumn, 
            activeTable.SortDir, 
            replaceHistory: true
            );
        }
        else
        {
            // If we are in "all" mode, null out optional keys (null by default) to demonstrate to the user that they are ignored.
            // NavManager.GetUriWithQueryParameters will strip them from the existing URL.
            NavService.UpdateSearchState(commandInput);
        }
    }

    /// <summary>
    /// Helper for quick select date aliases
    /// </summary>
    /// <param name="key">The key to append</param>
    /// <param name="shortcut">The shortcut to append</param>
    /// <returns></returns>
    public async Task AppendShortcut(string key, string shortcut)
    {
        string toAppend = $"{key}:{shortcut} ";
        // Short-circuit if search bar is empty
        if(string.IsNullOrEmpty(commandInput)){
            commandInput = toAppend;
            await JSService.FocusElement("searchBar");
            NotifyStateChanged();
            return;
        }

        // If the shortcut is already touching a tag or the key/shortcut is already present in the search bar, only add the shortcut
        if (commandInput.Contains(shortcut) || commandInput.Contains(key) || commandInput.TrimEnd()[^1] == ':'){
            toAppend = $"{shortcut} ";
        } else { // technically unnecessary bc the parser is smart enough but it's easier to read
            toAppend = ' ' + toAppend;
        }

        // Append the shortcut
        commandInput = commandInput.TrimEnd() + toAppend;
        SyncLivePreview();
        await JSService.FocusElement("searchBar");
    }

    /// <summary>
    /// Helper for quick select table tabs. Automatically runs a search for the target table.
    /// </summary>
    /// <param name="toType">The target table</param>
    public async Task SetType(string toType){
        if (toType == CurrentType) return; // Don't waste time performing an action that does nothing

        // If the search already has an "in" tag, replace it
        if(Regex.IsMatch(commandInput, ParserService.inPattern, RegexOptions.IgnoreCase)){
            commandInput = Regex.Replace(commandInput, ParserService.inPattern, $"in:{toType}", RegexOptions.IgnoreCase);
        } else { // otherwise, just append it
            await AppendKey("in", true);
            commandInput += toType;
        }
        CurrentType = toType;
        SyncUrl(); // technically called inside ExecutePowerSearch but we do it here to avoid the wait
        await ExecutePowerSearch();
    } 

    /// <summary>
    /// Helper for search bar X button
    /// </summary>
    public void ClearSearchBar(){
        commandInput = "";
        Filters = [];
        errorMessages = [];
        SyncLivePreview(); // calls NotifyStateChanged internally
    }

    public void RestartDebounceTimer()
    {
        // Ensure the preview updates immediately as they type
        SyncLivePreview();

        // Reset the timer
        DebounceTimer?.Stop();
        DebounceTimer?.Start(); 
    }

    /// <summary>
    /// Updates the live preview based on current search bar contents
    /// </summary>
    public void SyncLivePreview()
    {
        var liveResult = ParserService.ParseQuery(commandInput, CurrentType);
        Preview = liveResult.Preview;

        // Update the CurrentType if the user entered the "in" tag
        if (!string.IsNullOrEmpty(liveResult.CurrentType)) 
            CurrentType = liveResult.CurrentType;
        
        NotifyStateChanged();
    }

    /// <summary>
    /// Toggles color depending on whether the input key is present in the search bar
    /// </summary>
    /// <param name="key">The tag to check against the search bar contents</param>
    /// <param name="thisMode">Whether the tag is active in this mode (lighter styling)</param>
    /// <returns>The badge style reflecting its activation state</returns>
    public string GetBadgeClass(string key, bool thisMode){
        // Highlight red for negative presence (-key:)
        if (commandInput.Contains($"-{key}:", StringComparison.OrdinalIgnoreCase))
        {
            return thisMode ? "search-tag is-active negative" : "search-tag is-active-tertiary negative";
        }

        // Highlight blue for positive presence (key:)
        if (commandInput.Contains($"{key}:", StringComparison.OrdinalIgnoreCase))
        {
            return thisMode ? "search-tag is-active" : "search-tag is-active-tertiary";
        }

        // Otherwise, revert to respective default (inactive) states
        return thisMode ? "search-tag secondary" : "search-tag tertiary";
    }

    /// <summary>
    /// Removes a key and its associated value from the query
    /// </summary>
    /// <param name="key">The key to remove the tag for</param>
    /// <returns></returns>
    public void RemoveTagFromQuery(string key)
    {
        if (string.IsNullOrWhiteSpace(commandInput)) return; // in the case this method is somehow triggered without any filters

        // Pattern handles: -?key: followed by ("quoted value" OR unquotedValue)
        string pattern = $@"-?{key}:(""[^""]*""|[^\s]*)";

        commandInput = Regex.Replace(commandInput, pattern, "", RegexOptions.IgnoreCase).Trim();

        // Clean up double spaces
        commandInput = NormalizeWhiteSpace(commandInput);

        // If removing the 'in' tag, release the type it had set
        if(key.Contains("in")) CurrentType = "all";

        // If the input is now empty, clear the results entirely
        if (string.IsNullOrEmpty(commandInput))
        {
            foreach (var table in TableLogics) table.ClearData();
        }

        SyncLivePreview(); // calls NotifyStateChanged internally
    }

    /// <summary>
    /// Performant helper to replace multiple spaces with one
    /// </summary>
    /// <param name="input">The string for which to normalize whitespace</param>
    /// <returns>The input string with spaces normalized</returns>
    public static string NormalizeWhiteSpace(string input)
    {
        int len = input.Length,
            index = 0,
            i = 0;
        var src = input.ToCharArray();
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
                    if (skip) continue;
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
    /// Appends the input key to the search bar if not currently there
    /// </summary>
    /// <param name="key">The tag to be appended to the search</param>
    /// <param name="fromTableTab">whether this call is from a table tab (or from the quick select options)</param>
    /// <returns>Whether the key was appended (or if it was already in the </returns>
    public async Task<bool> AppendKey(string key, bool fromTableTab)
    {
        // Check if the key is already present (case-insensitive)
        if (commandInput.Contains(key, StringComparison.OrdinalIgnoreCase))
        {
            return false; // Do nothing; we don't want to duplicate it
        }

        // If the search bar is currently empty, append right away, otherwise ensure there is a space between the current last character and the key
        commandInput = string.IsNullOrWhiteSpace(commandInput) ? $"{key}:" : $"{commandInput.TrimEnd()} {key}:";

        // Trigger the JS helper to put the cursor back in the box if using quick select options
        if(!fromTableTab){ 
            await JSService.FocusElement("searchBar");
        }
        SyncLivePreview(); // calls NotifyStateChanged internally
        return true;
    }
}