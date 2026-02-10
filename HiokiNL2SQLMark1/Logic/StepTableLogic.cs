using Microsoft.EntityFrameworkCore;
using JS = Microsoft.JSInterop.IJSRuntime;
using NavigationManager = Microsoft.AspNetCore.Components.NavigationManager;

namespace HiokiNL2SQLMark1.Logic;
public class StepTableLogic(IDbContextFactory<LogDbContext> dbFactory, JS js, NavigationManager navManager) :
    LogTableLogic<StepResult>(dbFactory, db => db.StepView, js, navManager)
{
    public Filter<string?> FilterPartName = new("part", null);
    public Filter<int?> FilterStep = new("step", null);
    public Filter<string?> FilterMode = new("mode", null);

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
            if (string.Equals(intFilter.Key.ToLower(), "step", StringComparison.OrdinalIgnoreCase)) FilterStep = intFilter; return true;
        }
        return false;
    }

    public override void ResetFilterState()
    {
        FilterPartName.Value = null;
        FilterStep.Value = null;
        FilterMode.Value = null;
        base.ResetFilterState();
    }
}