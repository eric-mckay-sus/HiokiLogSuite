using Microsoft.EntityFrameworkCore;
using JS = Microsoft.JSInterop.IJSRuntime;

namespace HiokiNL2SQLMark1.Logic;
public class GroupTableLogic(IDbContextFactory<LogDbContext> dbFactory, JS js) : LogTableLogic<GroupResult>(dbFactory, db => db.GroupView, js)
{
    public Filter<string?> FilterComp = new(null);
    public Filter<string?> FilterShort = new(null);
    public Filter<string?> FilterMacro = new(null);
    public Filter<string?> FilterIC = new(null);
    public Filter<string?> FilterFunction = new(null);

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
        if (FilterComp.Value != null)
            query = FilterComp.IsNegated
                ? query.Where(g => !g.ComponentTest.Contains(FilterComp.Value))
                : query.Where(g => g.ComponentTest.Contains(FilterComp.Value));

        if (FilterShort.Value != null)
            query = FilterShort.IsNegated
                ? query.Where(g => !g.ShortTest.Contains(FilterShort.Value))
                : query.Where(g => g.ShortTest.Contains(FilterShort.Value));

        if (FilterMacro.Value != null)
            query = FilterMacro.IsNegated
                ? query.Where(g => !g.MacroTest.Contains(FilterMacro.Value))
                : query.Where(g => g.MacroTest.Contains(FilterMacro.Value));

        if (FilterIC.Value != null)
            query = FilterIC.IsNegated
                ? query.Where(g => !g.IcTest.Contains(FilterIC.Value))
                : query.Where(g => g.IcTest.Contains(FilterIC.Value));

        if (FilterFunction.Value != null)
            query = FilterFunction.IsNegated
                ? query.Where(g => !g.FunctionTest.Contains(FilterFunction.Value))
                : query.Where(g => g.FunctionTest.Contains(FilterFunction.Value));

        return query;
    }

    public override async Task ApplyFiltersFromDictionary(Dictionary<string, string> filterDict)
    {
        // Call the base dictionary mapper for the common fields
        await base.ApplyFiltersFromDictionary(filterDict);
        
        foreach (var (key, value) in filterDict)
        {
            bool isNegated = key.StartsWith('-');
            string cleanKey = isNegated ? key[1..] : key;
            switch (cleanKey.ToLower())
            {
                case "comp": 
                    FilterComp.Value = value;
                    FilterComp.IsNegated = isNegated; break;
                case "short": 
                    FilterShort.Value = value;
                    FilterShort.IsNegated = isNegated; break;
                case "macro": 
                    FilterMacro.Value = value;
                    FilterMacro.IsNegated = isNegated; break;
                case "ic": 
                    FilterIC.Value = value;
                    FilterIC.IsNegated = isNegated; break;
                case "function": 
                    FilterFunction.Value = value;
                    FilterFunction.IsNegated = isNegated; break;
            }
        }
    }

    public override void ResetFilterState()
    {
        FilterComp = new(null);
        FilterShort = new(null);
        FilterMacro = new(null);
        FilterIC = new(null);
        FilterFunction = new(null);

        base.ResetFilterState();
    }
}