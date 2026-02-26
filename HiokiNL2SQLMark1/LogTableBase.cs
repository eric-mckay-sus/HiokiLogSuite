using Microsoft.AspNetCore.Components;
using HiokiNL2SQLMark1.Logic;
using Parser = HiokiNL2SQLMark1.Services.SearchParserService;

namespace HiokiNL2SQLMark1;
/// <summary>
/// This abstract class forms the interface between a page and LogTableLogic and holds all shared information between VQ pages
/// </summary>
/// <typeparam name="T">An implementation of IHiokiLog (defined in LogDbContext)</typeparam>
public abstract class LogTableBase<T> : ComponentBase where T : class, IHiokiLog
{
    protected Filter<string?> FilterBarcode => Logic.GetFilter<string?>("barcode"); // For filtering barcodes
    protected Filter<DateTime?> FilterStartDate => Logic.GetFilter<DateTime?>("after"); // For filtering a start date
    protected Filter<DateTime?> FilterEndDate => Logic.GetFilter<DateTime?>("before"); // For filtering an end date
    protected Filter<string?> FilterResult => Logic.GetFilter<string?>("result"); // For filtering a test result
    protected Filter<int?> FilterGroup => Logic.GetFilter<int?>("group"); // For filtering a group number
    protected LogTableLogic<T> Logic { get; set; } = default!; // Where all the logic lives. The particular instance of LogTableLogic is determined by the page
    public DateTime? StartDatePart { get; set; } // The date part of the start datetime
    public string? StartTimePart { get; set; } // The time part of the start datetime
    public DateTime? EndDatePart { get; set; } // The date part of the end datetime
    public string? EndTimePart { get; set; } // The time part of the end datetime
    public bool IsInclusive { get; set; } = true; // Whether date filters should be applied in inclusive mode (or exclusive)
    protected string Preview { get; set; } = ""; // The human-readable preview

    protected override async Task OnInitializedAsync()
    {
        // Logic is instantiated in the synchronous OnInitialized() (in children)
        // Now we initialize caches and get the initial data for this table (must be performed async)
        await Logic.InitializeCaches();
        await RefreshData(); 
    }

    /// <summary>
    /// Calls Logic.RefreshData to update table, then tells the child the state has changed
    /// </summary>
    /// <param name="keepPage">Whether to keep the current page</param>
    /// <param name="force">Whether to skip the hydration check</param>
    /// <returns></returns>
    protected async Task RefreshData(bool keepPage=false)
    {
        await Logic.RefreshData(keepPage);
        Preview = Parser.GeneratePreview(Logic.TableName, Logic.Filters.Where(kvp => kvp.Value.GetValue() != null).ToDictionary());
        StateHasChanged();
    }

    /// <summary>
    /// Updates the start date filter from its constituent parts
    /// </summary>
    public void UpdateStart()
    {
        string datePart = StartDatePart?.ToString("yyyy-MM-dd") ?? "";

        // Combine parts: date only, time only, or both
        string combined = $"{datePart} {StartTimePart}".Trim();
        
        FilterStartDate.Value = Parser.ProcessDateValue("after", combined, IsInclusive);
    }

    /// <summary>
    /// Updates the end date filter from its constituent parts
    /// </summary>
    public void UpdateEnd()
    {       
        string datePart = EndDatePart?.ToString("yyyy-MM-dd") ?? "";
       
        string combined = $"{datePart} {EndTimePart}".Trim();
        
        FilterEndDate.Value = Parser.ProcessDateValue("before", combined, IsInclusive);
    }

    /// <summary>
    /// Re-process date filters when inclusivity changes
    /// </summary>
    /// <param name="toSet">New value of inclusivity</param>
    public void SetInclusivity(bool toSet)
    {
        IsInclusive = toSet;

        if (StartDatePart != null || !string.IsNullOrEmpty(StartTimePart))
        UpdateStart();
        
        if (EndDatePart != null || !string.IsNullOrEmpty(EndTimePart))
            UpdateEnd();
    }

    /// <summary>
    /// Clears all filters on a query, plus internal fields backing date filters
    /// </summary>
    /// <returns></returns>
    protected virtual async Task ClearFilters() {
        StartDatePart = EndDatePart = null;
        StartTimePart = EndTimePart = null;
        await Logic.ClearFilters();
        StateHasChanged();
    }
}