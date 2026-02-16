using Microsoft.EntityFrameworkCore;
using JS = Microsoft.JSInterop.IJSRuntime;
using NavigationManager = Microsoft.AspNetCore.Components.NavigationManager;

namespace HiokiNL2SQLMark1.Logic;
/// <summary>
/// Model class for a group table. Inherits from LogTableLogic
/// </summary>
/// <param name="dbFactory">Generates a new DB context per thread</param>
/// <param name="js">To handle saving to CSV</param>
/// <param name="navManager">To navigate away for barcode "drill-down"</param>
public class GroupTableLogic(IDbContextFactory<LogDbContext> dbFactory, JS js, NavigationManager navManager) : 
    LogTableLogic<GroupResult>(dbFactory, db => db.GroupView, js, navManager)
{
    public override string TableName => "group"; // This table's internal "type" as it would appear in currentType
    public override string DisplayName => "Group Results"; // The label to apply to this table in the view
    public Filter<string?> FilterComp = new("comp", null); // to filter component test results
    public Filter<string?> FilterShort = new("short", null); // to filter short circuit test results
    public Filter<string?> FilterMacro = new("macro", null); // to filter macro test results
    public Filter<string?> FilterIC = new("ic", null); // to filter IC test results
    public Filter<string?> FilterFunction = new("function", null); // to filter functional test results

    /// <summary>
    /// Wires filters to automatically push and pull data from fields
    /// </summary>
    protected override void InitializeFilters()
    {
        base.InitializeFilters();

        FilterComp.OnChanged = NotifyStateChanged;
        FilterShort.OnChanged = NotifyStateChanged;
        FilterMacro.OnChanged = NotifyStateChanged;
        FilterIC.OnChanged = NotifyStateChanged;
        FilterFunction.OnChanged = NotifyStateChanged;
    }

    /// <summary>
    /// Hashes the filters for comparison with the last query
    /// </summary>
    /// <returns>The hash of all filters applicable to a group table</returns>
    public override int GetFilterStateHash()
    {
        var hash = new HashCode();
        hash.Add(base.GetFilterStateHash());
        hash.Add(FilterComp.IsNegated);
        hash.Add(FilterComp.Value?.Trim() ?? "");
        hash.Add(FilterShort.IsNegated);
        hash.Add(FilterShort.Value?.Trim() ?? "");
        hash.Add(FilterMacro.IsNegated);
        hash.Add(FilterMacro.Value?.Trim() ?? "");
        hash.Add(FilterIC.IsNegated);
        hash.Add(FilterIC.Value?.Trim() ?? "");
        hash.Add(FilterFunction.IsNegated);
        hash.Add(FilterFunction.Value?.Trim() ?? "");
        return hash.ToHashCode();
    }

    /// <summary>
    /// Calls the base class to apply the generic filters, then applies the group-specific ones
    /// </summary>
    /// <param name="query">The query to which the filters will be applied</param>
    /// <returns>The query, with all filters applied</returns>
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
        return filter switch
        {
            Filter<string?> f when f.Key == "comp" => Wire(ref FilterComp, f),
            Filter<string?> f when f.Key == "short" => Wire(ref FilterShort, f),
            Filter<string?> f when f.Key == "macro" => Wire(ref FilterMacro, f),
            Filter<string?> f when f.Key == "ic" => Wire(ref FilterIC, f),
            Filter<string?> f when f.Key == "function" => Wire(ref FilterFunction, f),
            _ => false
        };
    }

    /// <summary>
    /// Reset the group-specific filters, then pass to the base class to reset the generic ones
    /// </summary>
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