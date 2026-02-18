using Microsoft.AspNetCore.Components;

namespace HiokiNL2SQLMark1.Logic;
/// <summary>
/// 
/// </summary>
public interface ILogTableLogic
{
    string TableName { get; } // This table's internal "type" as it would appear in currentType (e.g., "group", "step", "fct")
    string DisplayName { get; } // The label to apply to this table in the view (e.g., "Group Results")
    int TotalCount { get; } // The result count for this query on this page

    // Sorting parameters for URL control
    string CurrentSortColumn { get; set; } // The column currently being sorted
    string SortDir { get; set; } // The direction of the current sort
    
    // The core methods we need to trigger from the UI
    Task DictionaryToFilters(Dictionary<string, IFilter> filterDict, bool keepPage=false);
    int GetFilterStateHash(Dictionary<string, IFilter> filterDict);
    void ClearData();
    
    // Shared UI state for the MasterTable
    int CurrentPage { get; set; }
    int PageSize { get; set; }
    int TotalPages { get; }
    Action? OnNotifyUI { get; set; } // the trigger for which the view must watch
    Action<string>? TriggerPowerSearch { get; set; } // the trigger to jump to the power search page for a barcode "drill-down"
    Action? UpdatePSUrl { get; set; } // update the URL from the power search page
    public Func<bool>? IsStaleOverride { get; set; } // to allow PowerSearch to provide its own definition of IsStale
    bool IsStale { get; } // Whether the query contents are the ones that generated the shown results
    bool IsLoading { get; } // Whether the query results are loading

    int? LastQueryHash { get; }
}