using Microsoft.EntityFrameworkCore;
using JS = Microsoft.JSInterop.IJSRuntime;
using NavigationManager = Microsoft.AspNetCore.Components.NavigationManager;

namespace HiokiNL2SQLMark1.Logic;
public class GroupTableLogic(IDbContextFactory<LogDbContext> dbFactory, JS js, NavigationManager navManager) : 
    LogTableLogic<GroupResult>(dbFactory, db => db.GroupView, js, navManager)
{
    public Filter<string?> FilterComp = new("comp", null);
    public Filter<string?> FilterShort = new("short", null);
    public Filter<string?> FilterMacro = new("macro", null);
    public Filter<string?> FilterIC = new("ic", null);
    public Filter<string?> FilterFunction = new("function", null);

    /// <summary>
    /// Applies all filters available to the group table
    /// </summary>
    /// <param name="query">The query to which the filters will be appended</param>
    /// <returns>The query, now with filters</returns>
    public override IQueryable<GroupResult> ApplyFilters(IQueryable<GroupResult> query)
    {
        // Apply the base filters (Barcode, Date, etc.)
        query = base.ApplyFilters(query);

        // Apply Group-specific filters
        if (FilterComp.IsActive)
            query = FilterComp.IsNegated
                ? query.Where(g => !g.ComponentTest.Contains(FilterComp.Value))
                : query.Where(g => g.ComponentTest.Contains(FilterComp.Value));

        if (FilterShort.IsActive)
            query = FilterShort.IsNegated
                ? query.Where(g => !g.ShortTest.Contains(FilterShort.Value))
                : query.Where(g => g.ShortTest.Contains(FilterShort.Value));

        if (FilterMacro.IsActive)
            query = FilterMacro.IsNegated
                ? query.Where(g => !g.MacroTest.Contains(FilterMacro.Value))
                : query.Where(g => g.MacroTest.Contains(FilterMacro.Value));

        if (FilterIC.IsActive)
            query = FilterIC.IsNegated
                ? query.Where(g => !g.IcTest.Contains(FilterIC.Value))
                : query.Where(g => g.IcTest.Contains(FilterIC.Value));

        if (FilterFunction.IsActive)
            query = FilterFunction.IsNegated
                ? query.Where(g => !g.FunctionTest.Contains(FilterFunction.Value))
                : query.Where(g => g.FunctionTest.Contains(FilterFunction.Value));

        return query;
    }

    /// <summary>
    /// Checks if a tag is group-specific. If it is, this method adds it
    /// </summary>
    /// <param name="filter">The filter to check for</param>
    /// <returns>Whether the input key was group-specific</returns>
    protected override bool AssignTableSpecific(IFilter filter)
    {
        if (filter is Filter<string> strFilter)
        {
            switch (strFilter.Key.ToLower())
            {
                case "comp": FilterComp = strFilter; return true;
                case "short": FilterShort = strFilter; return true;
                case "macro": FilterMacro = strFilter; return true;
                case "ic": FilterIC = strFilter; return true;
                case "function": FilterFunction = strFilter; return true;
                default: return false;
            }
        }
        return false;
    }

    public override void ResetFilterState()
    {
        FilterComp.Value = null;
        FilterShort.Value = null;
        FilterMacro.Value = null;
        FilterIC.Value = null;
        FilterFunction.Value = null;

        base.ResetFilterState();
    }
}