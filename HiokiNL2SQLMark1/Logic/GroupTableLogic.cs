using Microsoft.EntityFrameworkCore;

namespace HiokiNL2SQLMark1.Logic;
public class GroupTableLogic : LogTableLogic<GroupResult>
{
    public GroupTableLogic(IDbContextFactory<LogDbContext> dbFactory, Func<LogDbContext, IQueryable<GroupResult>> querySelector) 
        : base(dbFactory, querySelector) { }

    public Filter<string?> FilterComp = new(null);
    public Filter<string?> FilterShort = new(null);
    public Filter<string?> FilterMacro = new(null);
    public Filter<string?> FilterIC = new(null);
    public Filter<string?> FilterFunction = new(null);

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

        // Call the base dictionary mapper for the common fields
        await base.ApplyFiltersFromDictionary(filterDict);
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