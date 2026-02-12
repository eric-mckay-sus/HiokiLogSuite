using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using JS = Microsoft.JSInterop.IJSRuntime;
using NavigationManager = Microsoft.AspNetCore.Components.NavigationManager;

namespace HiokiNL2SQLMark1.Logic;
/// <summary>
/// Model class for an FCT table.
/// Inherits from LogTableLogic
/// </summary>
/// <param name="dbFactory">Generates a new DB context per thread</param>
/// <param name="js">To handle saving to CSV</param>
/// <param name="navManager">To navigate away for barcode "drill-down"</param>
public class FctTableLogic(IDbContextFactory<LogDbContext> dbFactory, JS js, NavigationManager navManager) :
    LogTableLogic<FctResult>(dbFactory, db => db.FctView, js, navManager)
{
    public override string TableName => "fct";
    public override string DisplayName => "FCT Results";
    public Filter<int?> FilterStep = new("step", null);
    public Filter<string?> FilterMode = new("mode", null);

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
    /// Checks if a tag is FCT-specific. If it is, this method adds it
    /// </summary>
    /// <param name="filter">The filter to check for</param>
    /// <returns>Whether the input key was FCT-specific</returns>
    protected override bool AssignTableSpecific(IFilter filter)
    {
        if (filter is Filter<string?> strFilter)
        {
            if (string.Equals(strFilter.Key.ToLower(), "mode", StringComparison.OrdinalIgnoreCase))
            {
                FilterMode = strFilter;
                return true;
            }
        } else if (filter is Filter<int?> intFilter)
        {
            if (string.Equals(intFilter.Key.ToLower(), "step", StringComparison.OrdinalIgnoreCase))
            {
                FilterStep = intFilter; 
                return true;
            }
        }
        return false;
    }

    public override void ResetFilterState()
    {
        FilterStep.Value = null;
        FilterMode.Value = null;
        base.ResetFilterState();
    }
}