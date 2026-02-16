using Microsoft.AspNetCore.Components;

namespace HiokiNL2SQLMark1.Logic;
/// <summary>
/// 
/// </summary>
public interface ILogTableLogic
{
    string TableName { get; } // This table's internal "type" as it would appear in currentType (e.g., "group", "step", "fct")
    string DisplayName { get; } // The label to apply to this table in the view (e.g., "Group Results")
    bool IsLoading { get; } // Whether the query results are loading
    int TotalCount { get; } // The result count for this query on this page
    
    // The core methods we need to trigger from the UI
    Task DictionaryToFilters(Dictionary<string, IFilter> filterDict);
    void DictionaryToFiltersNoRefresh(Dictionary<string, IFilter> filterDict);
    void ClearData();
    RenderFragment RenderTable();
    
    // Shared UI state for the MasterTable
    int CurrentPage { get; }
    int TotalPages { get; }
    Action? OnNotifyUI { get; set; } // the trigger for which the view must watch
    Action<string>? TriggerPowerSearch { get; set; } // the trigger for which the viewmodel must watch
    public Func<bool>? IsStaleOverride { get; set; }
    bool IsStale { get; }
}