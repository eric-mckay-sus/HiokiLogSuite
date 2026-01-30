using Microsoft.EntityFrameworkCore;
public class FctTableLogic : LogTableLogic<FctResult>
{
    public FctTableLogic(IDbContextFactory<LogDbContext> dbFactory, Func<LogDbContext, IQueryable<FctResult>> querySelector) 
        : base(dbFactory, querySelector) { }

    public int? FilterStep { get; set; }
    public string? FilterMode { get; set; }

    public override IQueryable<FctResult> ApplyFilters(IQueryable<FctResult> query)
    {
        // Apply the base filters (Barcode, Date, etc.)
        query = base.ApplyFilters(query);

        // Apply FCT-specific filters
        if (FilterStep != null)
            query = query.Where(s => s.Step == FilterStep);

        if (!string.IsNullOrWhiteSpace(FilterMode))
            query = query.Where(s => s.Mode.Contains(FilterMode));

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
                case "mode": FilterMode = value; break;
                case "step" when int.TryParse(value, out int i): FilterStep = i; break;
            }
        }

        // Call the base dictionary mapper for the common fields
        await base.ApplyFiltersFromDictionary(filterDict);
    }

    public override void ResetFilterState()
    {
        FilterMode = null;
        FilterStep = null;
        base.ResetFilterState();
    }
}