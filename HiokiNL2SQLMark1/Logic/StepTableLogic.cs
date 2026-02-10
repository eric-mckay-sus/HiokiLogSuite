using Microsoft.EntityFrameworkCore;
using JS = Microsoft.JSInterop.IJSRuntime;
using NavigationManager = Microsoft.AspNetCore.Components.NavigationManager;

namespace HiokiNL2SQLMark1.Logic;
public class StepTableLogic(IDbContextFactory<LogDbContext> dbFactory, JS js, NavigationManager navManager) :
    LogTableLogic<StepResult>(dbFactory, db => db.StepView, js, navManager)
{
    public Filter<string?> FilterPartName = new(null);
    public Filter<int?> FilterStep = new(null);
    public Filter<string?> FilterMode = new(null);

    public override IQueryable<StepResult> ApplyFilters(IQueryable<StepResult> query)
    {
        // Apply the base filters (Barcode, Date, etc.)
        query = base.ApplyFilters(query);

        // Apply Step-specific filters
        if (FilterStep.Value != null)
            query = FilterStep.IsNegated
                ? query = query.Where(s => s.Step != FilterStep.Value)
                : query = query.Where(s => s.Step == FilterStep.Value);

        if (FilterPartName.Value != null)
            query = FilterPartName.IsNegated
                ? query.Where(s => !s.PartName.Contains(FilterPartName.Value))
                : query.Where(s => s.PartName.Contains(FilterPartName.Value));

        if (FilterMode.Value != null)
            query = FilterMode.IsNegated
                ? query.Where(s => !s.Mode.Contains(FilterMode.Value))
                : query.Where(s => s.Mode.Contains(FilterMode.Value));

        return query;
    }

    /// <summary>
    /// Checks if a tag is step-specific. If it is, this method adds it
    /// </summary>
    /// <param name="key">The key to check</param>
    /// <param name="value">The value to add, if applicable</param>
    /// <returns>Whether the input key was step-specific</returns>
    protected override bool AssignTableSpecific(string key, string value)
    {
        bool isNegated = key.StartsWith('-');
        string cleanKey = isNegated ? key[1..] : key;
        return cleanKey.ToLower() switch
        {
            "part" => SetFilter(FilterPartName, value, isNegated),
            "mode" => SetFilter(FilterMode, value, isNegated),
            "step" when int.TryParse(value, out int i) => SetFilter(FilterStep, i, isNegated),
            _ => false
        };
    }

    public override void ResetFilterState()
    {
        FilterPartName = new(null);
        FilterMode = new(null);
        FilterStep = new(null);
        base.ResetFilterState();
    }
}