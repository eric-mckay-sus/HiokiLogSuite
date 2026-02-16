using Microsoft.EntityFrameworkCore;
using JS = Microsoft.JSInterop.IJSRuntime;
using NavigationManager = Microsoft.AspNetCore.Components.NavigationManager;

namespace HiokiNL2SQLMark1.Logic;
/// <summary>
/// Model class for a step table. Inherits from LogTableLogic
/// </summary>
/// <param name="dbFactory">Generates a new DB context per thread</param>
/// <param name="js">To handle saving to CSV</param>
/// <param name="navManager">To navigate away for barcode "drill-down"</param>
public class StepTableLogic(IDbContextFactory<LogDbContext> dbFactory, JS js, NavigationManager navManager) :
    LogTableLogic<StepResult>(dbFactory, db => db.StepView, js, navManager)
{
    public override string TableName => "step"; // This table's internal "type" as it would appear in currentType
    public override string DisplayName => "Step Results"; // The label to apply to this table in the view
    public Filter<string?> FilterPartName = new("part", null); // to filter part name
    public Filter<int?> FilterStep = new("step", null); // to filter test step
    public Filter<string?> FilterMode = new("mode", null); // to filter test mode

    /// <summary>
    /// Hashes the filters for comparison with the last query
    /// </summary>
    /// <returns>The hash of all filters applicable to a step table</returns>
    public override int GetFilterStateHash() => HashCode.Combine(base.GetFilterStateHash(), 
            FilterPartName.Value, FilterStep.Value, FilterMode.Value);

    /// <summary>
    /// Calls the base class to apply the generic filters, then applies the step-specific ones
    /// </summary>
    /// <param name="query">The query to which the filters will be applied</param>
    /// <returns>The query, with all filters applied</returns>
    public override IQueryable<StepResult> ApplyFilters(IQueryable<StepResult> query)
    {
        // Apply the base filters (Barcode, Date, etc.)
        query = base.ApplyFilters(query);

        // Apply Step-specific filters
        if (FilterStep.IsActive)
            query = FilterStep.IsNegated
                ? query = query.Where(s => s.Step != FilterStep.Value)
                : query = query.Where(s => s.Step == FilterStep.Value);

        if (FilterPartName.IsActive)
            query = FilterPartName.IsNegated
                ? query.Where(s => !s.PartName.Contains(FilterPartName.Value))
                : query.Where(s => s.PartName.Contains(FilterPartName.Value));

        if (FilterMode.IsActive)
            query = FilterMode.IsNegated
                ? query.Where(s => !s.Mode.Contains(FilterMode.Value))
                : query.Where(s => s.Mode.Contains(FilterMode.Value));

        return query;
    }

    /// <summary>
    /// Checks if a tag is step-specific. If it is, this method adds it
    /// </summary>
    /// <param name="filter">The IFilter to check for</param>
    /// <returns>Whether the input key was step-specific</returns>
    protected override bool AssignTableSpecific(IFilter filter)
    {
        if (filter is Filter<string?> strFilter)
        {
            switch (strFilter.Key.ToLower())
            {
                case "part": FilterPartName = strFilter; return true;
                case "mode": FilterMode = strFilter; return true;
                default: return false;
            }
        } else if (filter is Filter<int?> intFilter)
        {
            if (string.Equals(intFilter.Key.ToLower(), "step", StringComparison.OrdinalIgnoreCase)) { 
                FilterStep = intFilter;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Reset the step-specific filters, then pass to the base class to reset the generic ones
    /// </summary>
    public override void ResetFilterState()
    {
        FilterPartName.Value = null;
        FilterStep.Value = null;
        FilterMode.Value = null;
        base.ResetFilterState();
    }
}