using Microsoft.AspNetCore.Components;

namespace HiokiNL2SQLMark1.Logic;
public interface ILogTableLogic
{
    string TableName { get; } // e.g., "group", "step", "fct"
    string DisplayName { get; } // e.g., "Group Results"
    bool IsLoading { get; }
    int TotalCount { get; }
    
    // The core methods we need to trigger from the UI
    Task DictionaryToFilters(Dictionary<string, IFilter> filterDict);
    void ClearData();
    
    // Shared UI state for the MasterTable
    int CurrentPage { get; }
    int TotalPages { get; }
    Action? OnNotifyUI { get; set; }
    Action<string>? TriggerPowerSearch { get; set; } 
    
    RenderFragment RenderTable();
}