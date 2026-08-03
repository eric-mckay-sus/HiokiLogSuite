// <copyright file="SearchPreviewService.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace HiokiNL2SQL.Services;

using static SearchParserService;
using HiokiNL2SQL.Logic;

/// <summary>
/// Service responsible for generating search previews.
/// </summary>
public static class SearchPreviewService
{
    private static readonly Dictionary<string, string> TagDescriptions = new (StringComparer.OrdinalIgnoreCase) // maps search tags to their tooltip
    {
        { "in", "Switch table scope (all, group, step, fct)" },
        { "barcode", "Search by unique PCB serial number" },
        { "group", "Filter by group number in test sequence" },
        { "before", "Show results recorded before this date (exclusive)" },
        { "after", "Show results recorded after this date (inclusive)" },
        { "result", "Filter by PASS/FAIL status" },
        { "comp", "Filter by component test results" },
        { "short", "Filter by short-circuit test results" },
        { "open", "Filter by open-circuit test results" },
        { "macro", "Search by macro-test results" },
        { "ic", "Filter by integrated circuit (IC) test results" },
        { "function", "Search by functional test results" },
        { "step", "Filter by test step number" },
        { "mode", "Filter by test mode" },
        { "part", "Search by part name" },
    };

    /// <summary>
    /// Dynamically gets the tooltip based on what tables in which a key is valid.
    /// </summary>
    /// <param name="key">The key for which to get the tooltip.</param>
    /// <param name="showScope">Whether to provide the scope of this tag.</param>
    /// <returns>The tooltip associated with this string (and optional scope).</returns>
    public static string GetTagTooltip(string key, bool showScope)
    {
        // Get the functional description
        if (!TagDescriptions.TryGetValue(key, out string? description))
        {
            description = $"Filter by {key}";
        }

        // If instructed not to return the scope, return now
        if (!showScope)
        {
            return description;
        }

        // Otherwise, gather scope info by scanning tag sets
        string scopeInfo;
        if (UniversalTags.Contains(key))
        {
            scopeInfo = "All tables";
        }
        else
        {
            List<string> locations = [];
            if (GroupTags.Contains(key))
            {
                locations.Add("Group");
            }

            if (StepTags.Contains(key))
            {
                locations.Add("Step");
            }

            if (FctTags.Contains(key))
            {
                locations.Add("FCT");
            }

            scopeInfo = locations.Count > 0 ? string.Join(", ", locations) : "System tag";
        }

        return $"{description} | Works in: {scopeInfo}";
    }

    /// <summary>
    /// Translates the dictionary created by ParseQuery into a human-readable preview of what query would be executed if the search was run now.
    /// </summary>
    /// <param name="type">The table targeted by the query.</param>
    /// <param name="filters">The dictionary of filters for the query.</param>
    /// <returns>A string preview of the query to be executed.</returns>
    public static string GeneratePreview(string type, Dictionary<string, IFilter> filters)
    {
        string tableMessage = $"Showing all results";
        tableMessage += (type != "all") ? $" from **{type.ToUpper()}**" : " from **ALL** tables";
        if (filters.Count == 0)
        {
            return tableMessage;
        }

        // Build string snippets for each filter
        IEnumerable<string> parts = filters.Values.Select(filter =>
        {
            string cleanKey = filter.Key.ToUpper();
            string negationLabel = filter.IsNegated ? " NOT " : " ";
            string containLabel = filter.IsNegated ? "does NOT contain" : "contains";

            // Branch based on datatype
            return filter switch
            {
                // DateTime filters (format as datetime)
                Filter<DateTime?> { Value: { } dtValue } =>
                    $"DATE is **{cleanKey}** '{dtValue:yyyy-MM-dd HH:mm:ss}'",

                // Integer filters (translates to SQL '=')
                Filter<int?> { Value: { } intValue } =>
                    $"**{cleanKey}** is{negationLabel}'{intValue}'",

                // String filters (translates to SQL 'LIKE')
                Filter<string?> { Value: { } strValue } when !string.IsNullOrWhiteSpace(strValue) =>
                    $"**{cleanKey}** {containLabel} '{strValue}'",

                // Default fallback
                _ => $"**{cleanKey}** (incomplete value...)'"
            };
        });

        return $"Searching **{type.ToUpper()}** where " + string.Join(" and ", parts);
    }
}
