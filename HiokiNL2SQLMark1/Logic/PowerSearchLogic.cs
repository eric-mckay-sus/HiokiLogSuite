using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Timers;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

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
    public Dictionary<string, IFilter> filters = []; // the key-value pairs parsed from command input
    public System.Timers.Timer? DebounceTimer; // to smooth the preview rendering
    public bool IsProcessingNavigation;
    public bool IsSearching;
    public int AllCount => TableLogics.Sum(t => t.TotalCount);
    public string LastExecutedQuery = "Hioki ICT Power Search";

    public readonly IEnumerable<ILogTableLogic> TableLogics;
    private readonly SearchParserService ParserService;
    public readonly NavigationManager NavManager;
    private readonly IJSRuntime JSRuntime;

    public event Action? OnRefreshRequested;
    public void NotifyStateChanged() => OnRefreshRequested?.Invoke();

    public PowerSearchLogic(
        IEnumerable<ILogTableLogic> tableLogics,
        SearchParserService parserService,
        NavigationManager navManager,
        IJSRuntime jsRuntime) : this()
    {
        TableLogics = tableLogics;
        ParserService = parserService;
        NavManager = navManager;
        JSRuntime = jsRuntime;

        // Wire each table's notification to this class
        foreach (var table in TableLogics)
        {
            table.OnNotifyUI = NotifyStateChanged;
            table.TriggerPowerSearch = (query) => _ = UpdateSearchState(query);
        }
    }

    /// <summary>
    /// Parse search bar input, update the model, then tell the view  
    /// </summary>
    /// <returns></returns>
    public async Task ExecutePowerSearch(bool isNavigatingInternal=false, bool skipUrlUpdate=false)
    {
        // Identify tables targeted by this query based on CurrentType
        var targets = TableLogics
            .Where(t => CurrentType == "all" || t.TableName.Equals(CurrentType, StringComparison.OrdinalIgnoreCase));

        // Clear untargeted tables, run targeted ones in parallel
        foreach (var table in TableLogics.Except(targets)) table.ClearData();

        var parseResult = ParserService.ParseQuery(commandInput, CurrentType);

        filters = parseResult.Filters;
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

        if(!skipUrlUpdate){
            IsProcessingNavigation = true;
            try{
                if(!isNavigatingInternal){
                    // Update the URL query string
                    string? newUri = NavManager.GetUriWithQueryParameter("q", string.IsNullOrWhiteSpace(commandInput) ? null : commandInput);

                    // 'false' means don't reload the page, just update the address bar
                    NavManager.NavigateTo(newUri, replace: false);
                } 
            } finally{
                await Task.Yield(); // if something goes wrong, give the browser a second, then unlock navigation
                IsProcessingNavigation=false;
            }
        }

        // Inform the UI that we're loading
        IsSearching = true;
        LastExecutedQuery = string.IsNullOrWhiteSpace(commandInput) ? "Hioki ICT Power Search" : $"Search: {commandInput}";
        NotifyStateChanged();

        try{
            // Parallelize search
            await Task.WhenAll(targets.Select(t => t.DictionaryToFilters(filters)));
        } catch (Exception ex){
            errorMessages.Add($"Search failed: {ex.Message}");
        } finally{
            IsSearching = false;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Helper to actually execute the new search
    /// </summary>
    /// <param name="newValue">The content to put in the search bar</param>
    public async Task UpdateSearchState(string newValue)
    {
        commandInput = newValue;
        SyncLivePreview();
        await ExecutePowerSearch(isNavigatingInternal: true, skipUrlUpdate: true); 
        NotifyStateChanged();
    }

    /// <summary>
    /// Helper to append the datetime associated with a date shortcut to keep AppendKey for key checking only
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
            await JSRuntime.InvokeVoidAsync("focusElement", "searchBar");
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
        await JSRuntime.InvokeVoidAsync("focusElement", "searchBar");
        NotifyStateChanged();
    }

    /// <summary>
    /// Helper for quick select table tabs. Automatically runs a search for the target table if the last query had results or the command input has content
    /// </summary>
    /// <param name="toType">The target table</param>
    public async Task SetType(string toType){
        if(Regex.IsMatch(commandInput, ParserService.inPattern, RegexOptions.IgnoreCase)){ // if the search already has an "in" tag, replace it
            commandInput = Regex.Replace(commandInput, ParserService.inPattern, $"in:{toType}", RegexOptions.IgnoreCase);
        } else { // otherwise, just append it
            await AppendKey("in", true);
            commandInput += toType;
        }
        CurrentType = toType;
        SyncLivePreview();
        // If the user is currently viewing a search result or has something in the search bar, they probably want to search now
        if (AllCount > 0){
            await ExecutePowerSearch();
        }
        NotifyStateChanged();
    } 

    /// <summary>
    /// Helper for search bar X button
    /// </summary>
    public void ClearSearchBar(){
        commandInput = "";
        filters = [];
        errorMessages = [];
        SyncLivePreview();
        NotifyStateChanged();
    }

    /// <summary>
    /// Manage the debounce timer and notify the preview
    /// </summary>
    /// <param name="e">Detects a change in input</param>
    public void HandleInput(ChangeEventArgs e){
        commandInput = e.Value?.ToString() ?? "";

        // Stop the old (if there is one)
        if (DebounceTimer != null) {
            DebounceTimer.Stop();
            DebounceTimer.Dispose();
        }

        // Start the new
        DebounceTimer = new System.Timers.Timer(300);
        DebounceTimer.Elapsed += OnUserStoppedTyping;
        DebounceTimer.AutoReset = false;
        DebounceTimer.Start();
        NotifyStateChanged();
    }

    /// <summary>
    /// As the user types, get the interpretation of their search
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnUserStoppedTyping(object? sender, ElapsedEventArgs e)
    {
        // Timer runs on a background thread, so use InvokeAsync to update the UI thread.
        var liveResult = ParserService.ParseQuery(commandInput, CurrentType);
        Preview = liveResult.Preview;

        // Update the CurrentType if the user entered the "in" tag
        if (!string.IsNullOrEmpty(liveResult.CurrentType)) 
            CurrentType = liveResult.CurrentType;

        NotifyStateChanged();
    }

    /// <summary>
    /// Updates the live preview based on current search bar contents
    /// </summary>
    public void SyncLivePreview()
    {
        var liveResult = ParserService.ParseQuery(commandInput, CurrentType);
        Preview = liveResult.Preview;
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
        commandInput = Regex.Replace(commandInput, @"\s+", " ");

        if(key.Contains("in")) CurrentType = "all";

        // Sync the preview and re-execute search to update the DataViews
        SyncLivePreview();

        // If the input is now empty, clear the results entirely
        if (string.IsNullOrEmpty(commandInput))
        {
            var targets = TableLogics
                .Where(t => CurrentType == "all" || t.TableName.Equals(CurrentType, StringComparison.OrdinalIgnoreCase));
            foreach (var table in TableLogics.Except(targets)) table.ClearData();
        }
        NotifyStateChanged();
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
            await JSRuntime.InvokeVoidAsync("focusElement", "searchBar");
        }
        SyncLivePreview();
        NotifyStateChanged();
        return true;
    }
}