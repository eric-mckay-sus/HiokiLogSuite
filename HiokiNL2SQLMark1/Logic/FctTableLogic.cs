using Microsoft.EntityFrameworkCore;
using JS = Microsoft.JSInterop.IJSRuntime;
using NavigationManager = Microsoft.AspNetCore.Components.NavigationManager;

namespace HiokiNL2SQLMark1.Logic;
/// <summary>
/// Model class for a FCT table. Inherits from LogTableLogic
/// </summary>
/// <param name="dbFactory">Generates a new DB context per thread</param>
/// <param name="js">To handle saving to CSV</param>
/// <param name="navManager">To navigate away for barcode "drill-down"</param>
public class FctTableLogic(IDbContextFactory<LogDbContext> dbFactory, JS js, NavigationManager navManager) :
    LogTableLogic<FctResult>(dbFactory, db => db.FctView, js, navManager)
{
    public override string TableName => "fct"; // This table's internal "type" as it would appear in currentType
    public override string DisplayName => "FCT Results"; // The label to apply to this table in the view
    public Filter<int?> FilterStep = new("step", null); // to filter test steps
    public Filter<string?> FilterMode = new("mode", null); // to filter test modes

    /// <summary>
    /// Wires filters to automatically push and pull data from fields
    /// </summary>
    protected override void InitializeFilters()
    {
        base.InitializeFilters();

        FilterStep.OnChanged = NotifyStateChanged;
        FilterMode.OnChanged = NotifyStateChanged;
    }

    /// <summary>
    /// Hashes the filters for comparison with the last query
    /// </summary>
    /// <returns>The hash of all filters applicable to a FCT table</returns>
    public override int GetFilterStateHash() {
        var hash = new HashCode();
        hash.Add(base.GetFilterStateHash()); 
        hash.Add(FilterStep.IsNegated);
        hash.Add(FilterStep.Value);
        hash.Add(FilterMode.IsNegated);
        hash.Add(FilterMode.Value?.Trim() ?? "");
        return hash.ToHashCode();
    }

    /// <summary>
    /// Calls the base class to apply the generic filters, then applies the FCT-specific ones
    /// </summary>
    /// <param name="query">The query to which the filters will be applied</param>
    /// <returns>The query, with all filters applied</returns>
    public override IQueryable<FctResult> ApplyFilters(IQueryable<FctResult> query)
    {
        // Apply the base filters (Barcode, Date, etc.)
        query = base.ApplyFilters(query);

        // Apply FCT-specific filters
        if (FilterStep.IsActive)
            query = FilterStep.IsNegated
                ? query = query.Where(s => s.Step != FilterStep.Value)
                : query = query.Where(s => s.Step == FilterStep.Value);

        if (FilterMode.IsActive)
            query = FilterMode.IsNegated
                ? query.Where(s => !s.Mode.Contains(FilterMode.Value))
                : query.Where(s => s.Mode.Contains(FilterMode.Value));

        return query;
    }

    /// <summary>
    /// Checks if a tag from the dictionary is FCT-specific. If it is, this method adds it
    /// </summary>
    /// <param name="filter">The filter to check for</param>
    /// <returns>Whether the input key was FCT-specific</returns>
    protected override bool AssignTableSpecific(IFilter filter)
    {
        return filter switch
        {
            Filter<int?> f when f.Key == "step" => Wire(ref FilterStep, f),
            Filter<string?> f when f.Key == "mode" => Wire(ref FilterMode, f),
            _ => false
        };
    }

    /// <summary>
    /// Reset the FCT-specific filters, then pass to the base class to reset the generic ones
    /// </summary>
    public override void ResetFilterState()
    {
        FilterStep.Value = null;
        FilterMode.Value = null;
        base.ResetFilterState();
    }
}