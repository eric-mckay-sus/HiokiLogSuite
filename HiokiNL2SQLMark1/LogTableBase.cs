using Microsoft.AspNetCore.Components;
using HiokiNL2SQLMark1.Logic;
using Parser = HiokiNL2SQLMark1.Services.SearchParserService;

namespace HiokiNL2SQLMark1;
/// <summary>
/// This abstract class forms the interface between a page and LogTableLogic
/// </summary>
/// <typeparam name="T">An implementation of IHiokiLog (defined in LogDbContext)</typeparam>
public abstract class LogTableBase<T> : ComponentBase where T : class, IHiokiLog
{
    protected Filter<string?> FilterBarcode => Logic.GetFilter<string?>("barcode");
    protected Filter<DateTime?> FilterStartDate => Logic.GetFilter<DateTime?>("after");
    protected Filter<DateTime?> FilterEndDate => Logic.GetFilter<DateTime?>("before");
    protected Filter<string?> FilterResult => Logic.GetFilter<string?>("result");
    protected Filter<int?> FilterGroup => Logic.GetFilter<int?>("group");
    protected LogTableLogic<T> Logic { get; set; } = default!; // Where all the logic lives. The particular instance of LogTableLogic is determined by the page
    protected DateTime? _startDatePart;
    protected string? _startTimePart;    
    protected DateTime? _endDatePart;
    protected string? _endTimePart;
    protected bool _isInclusive;

    protected override async Task OnInitializedAsync()
    {
        // Logic is instantiated in the synchronous OnInitialized()
        // Now we run the async setup
        await Logic.InitializeCaches();
        await RefreshData(); 
    }

    /// <summary>
    /// Calls Logic.RefreshData to update table, then tells the child the state has changed
    /// </summary>
    /// <param name="keepPage">Whether to keep the current page</param>
    /// <returns></returns>
    protected async Task RefreshData(bool keepPage = false)
    {
        await Logic.RefreshData(keepPage);
        StateHasChanged();
    }

    protected void UpdateStart()
    {
        string datePart = _startDatePart?.ToString("yyyy-MM-dd") ?? "";

        // Combine parts: date only, time only, or both
        string combined = $"{datePart} {_startTimePart}".Trim();
        
        FilterStartDate.Value = Parser.ProcessDateValue("after", combined, _isInclusive);
    }

    protected void UpdateEnd()
    {       
        string datePart = _endDatePart?.ToString("yyyy-MM-dd") ?? "";
       
        string combined = $"{datePart} {_endTimePart}".Trim();
        
        FilterEndDate.Value = Parser.ProcessDateValue("before", combined, _isInclusive);
    }

    /// <summary>
    /// Re-process date filters when inclusivity changes
    /// </summary>
    /// <param name="toSet">New value of inclusivity</param>
    protected void SetInclusivity(bool toSet)
    {
        _isInclusive = toSet;

        if (_startDatePart != null || !string.IsNullOrEmpty(_startTimePart))
        UpdateStart();
        
        if (_endDatePart != null || !string.IsNullOrEmpty(_endTimePart))
            UpdateEnd();
    }

    /// <summary>
    /// Clears all filters on a query
    /// </summary>
    /// <returns></returns>
    public virtual async Task ClearFilters() {
        _startDatePart = _endDatePart = null;
        _startTimePart = _endTimePart = null;
        await Logic.ClearFilters();
        StateHasChanged();
    }
}