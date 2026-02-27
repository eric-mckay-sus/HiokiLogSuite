namespace HiokiNL2SQLMark1.Logic;
/// <summary>
/// The data visible to the power search page from the true backend
/// </summary>
public interface ILogTableLogic
{
    string TableName { get; } // This table's internal "type" as it would appear in currentType (e.g., "group", "step", "fct")
    string DisplayName { get; } // The label to apply to this table in the view (e.g., "Group Results")

    // Sorting parameters for URL control
    string CurrentSortColumn { get; set; } // The column currently being sorted
    string SortDir { get; set; } // The direction of the current sort
    
    // The core methods we need to trigger from the UI
    Task RefreshData(bool keepPage=false, bool force=false);
    Task DictionaryToFilters(Dictionary<string, IFilter> filterDict, bool keepPage=false);
    void ClearData();
    
    // Shared UI state for the MasterTable
    int CurrentPage { get; set; } // The page number shown in the data view
    int PageSize { get; set; } // The number of results per page
    int TotalCount { get; } // The result count for this query on this page

    // UI-only indicator used by MasterTable when deciding whether to fade the results
    // (blinking inputs on power-search). This value is entirely for rendering and does
    // not influence any query logic or caching.
    Func<bool>? UIIsStaleOverride { get; set; }
    bool UIIsStale { get; }

    // Linking to the power search page
    Action? OnNotifyUI { set; } // the trigger for which the view must watch
    Action<string>? TriggerPowerSearch { get; set; } // the trigger to jump to the power search page for a barcode "drill-down"
    Action? UpdatePSUrl { get; set; } // update the URL from the power search page
    
}