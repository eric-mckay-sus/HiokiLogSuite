using Microsoft.EntityFrameworkCore;

namespace HiokiNL2SQLMark1.Logic;
public class FctTableLogic : LogTableLogic<FctResult>
{
    public FctTableLogic(IDbContextFactory<LogDbContext> dbFactory, Func<LogDbContext, IQueryable<FctResult>> querySelector) 
        : base(dbFactory, querySelector) { }

    public Filter<int?> FilterStep = new(null);
    public Filter<string?> FilterMode = new(null);

    public override IQueryable<FctResult> ApplyFilters(IQueryable<FctResult> query)
    {
        // Apply the base filters (Barcode, Date, etc.)
        query = base.ApplyFilters(query);

        // Apply FCT-specific filters
        if (FilterStep.Value != null)
            query = FilterStep.IsNegated
                ? query = query.Where(s => s.Step != FilterStep.Value)
                : query = query.Where(s => s.Step == FilterStep.Value);

        if (FilterMode.Value != null)
            query = FilterMode.IsNegated
                ? query.Where(s => !s.Mode.Contains(FilterMode.Value))
                : query.Where(s => s.Mode.Contains(FilterMode.Value));

        return query;
    }

    public override async Task ApplyFiltersFromDictionary(Dictionary<string, string> filterDict)
    {
        // Run base logic to handle common filters
        // Note: We don't await RefreshData here yet to avoid multiple DB calls
        foreach (var (key, value) in filterDict)
        {
            bool isNegated = key.StartsWith('-');
            string cleanKey = isNegated ? key[1..] : key;
            switch (cleanKey.ToLower())
            {
                case "mode": 
                    FilterMode.Value = value;
                    FilterMode.IsNegated = isNegated; break;
                case "step" when int.TryParse(value, out int i): 
                    FilterStep.Value = i;
                    FilterStep.IsNegated = isNegated; break;
            }
        }

        // Call the base dictionary mapper for the common fields
        await base.ApplyFiltersFromDictionary(filterDict);
    }

    public override void ResetFilterState()
    {
        FilterMode.Value = null;
        FilterStep.Value = null;
        base.ResetFilterState();
    }
}