using Microsoft.EntityFrameworkCore;
public class GroupTableLogic : LogTableLogic<GroupResult>
{
    public GroupTableLogic(IDbContextFactory<LogDbContext> dbFactory, Func<LogDbContext, IQueryable<GroupResult>> querySelector) 
        : base(dbFactory, querySelector) { }

    public string? FilterComp;
    public string? FilterShort;
    public string? FilterMacro;
    public string? FilterIC;
    public string? FilterFunction;

    public override IQueryable<GroupResult> ApplyFilters(IQueryable<GroupResult> query)
    {
        // Apply the base filters (Barcode, Date, etc.)
        query = base.ApplyFilters(query);

        // Apply Group-specific filters
        if (!string.IsNullOrWhiteSpace(FilterComp))
            query = query.Where(s => s.ComponentTest.Contains(FilterComp));

        if (!string.IsNullOrWhiteSpace(FilterShort))
            query = query.Where(s => s.ShortTest.Contains(FilterShort));
        if (!string.IsNullOrWhiteSpace(FilterMacro))
                    query = query.Where(s => s.MacroTest.Contains(FilterMacro));

        if (!string.IsNullOrWhiteSpace(FilterIC))
                    query = query.Where(s => s.IcTest.Contains(FilterIC));

        if (!string.IsNullOrWhiteSpace(FilterFunction))
                    query = query.Where(s => s.FunctionTest.Contains(FilterFunction));

        return query;
    }

    public override async Task ApplyFiltersFromDictionary(Dictionary<string, string> filterDict)
    {
        // Run base logic to handle common filters
        // Note: We don't await RefreshData here yet to avoid multiple DB calls
        foreach (var (key, value) in filterDict)
        {
            switch (key.ToLower())
            {
                case "comp": FilterComp = value; break;
                case "short": FilterShort = value; break;
                case "macro": FilterMacro = value; break;
                case "ic": FilterIC = value; break;
                case "function": FilterFunction = value; break;
            }
        }

        // Call the base dictionary mapper for the common fields
        await base.ApplyFiltersFromDictionary(filterDict);
    }

    public override void ResetFilterState()
    {
        FilterComp = null;
        FilterShort = null;
        FilterMacro = null;
        FilterIC = null;
        FilterFunction = null;

        base.ResetFilterState();
    }

    public override async Task ClearFilters()
    {
        ResetFilterState();
        await base.ClearFilters();
    }
}