// <copyright file="SearchParserService.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace HiokiNL2SQL.Services;

using System.Text.RegularExpressions;

using static DateParsingService;
using static SearchPreviewService;
using static SearchValidationService;
using HiokiNL2SQL.Logic;

/// <summary>
/// A DTO for the return values from the parser.
/// </summary>
public record SearchParseResult
{
    /// <summary>
    /// Gets or sets the dictionary of filters constructed from parsing.
    /// </summary>
    public Dictionary<string, IFilter> Filters { get; set; } = new (StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the list of error messages collected while parsing.
    /// </summary>
    public List<string> ErrorMessages { get; set; } = [];

    /// <summary>
    /// Gets or sets the scope of this search (which table type).
    /// </summary>
    public string CurrentType { get; set; } = "all";

    /// <summary>
    /// Gets or sets the human-readable query parse.
    /// </summary>
    public string Preview { get; set; } = "Searching all records...";

    /// <summary>
    /// Gets a value indicating whether the query has a valid date filter (before/after tag).
    /// </summary>
    public bool HasDateFilter { get; }
}

/// <summary>
/// A service to contain state and methods relevant for parsing. Required to be injected into PowerSearch.razor
/// This service does have state, but it is all static.
/// </summary>
public partial class SearchParserService
{
    /// <summary>
    /// Pattern to match the 'before' tag and associated DateTime.
    /// </summary>
    public static readonly string BeforePattern = @"before\s?:\s?(""[^""]*""|(?:(?!\s?-?\w+:)\S)+)";

    /// <summary>
    /// Pattern to match the 'after' tag and associated DateTime.
    /// </summary>
    public static readonly string AfterPattern = @"after\s?:\s?(""[^""]*""|(?:(?!\s?-?\w+:)\S)+)";

    /// <summary>
    /// List of all available tables.
    /// </summary>
    public static readonly string[] AvailableTypes = ["all", "group", "step", "fct"];

    /// <summary>
    /// The pattern (regular expression) used to identify the 'in' tag, with attempted (disallowed) optional negation of either key or value.
    /// </summary>
    private const string InPattern = @"(-?)in\s*:\s*(-?\w+)";

    /// <summary>
    /// Regex to find key:value pairs. -? detects optional negation, \w+ detects the key text, \s* surrounding the colon detects whitespace,
    /// : is the literal colon, " is a literal quote, [^"]* iterates over non-quote characters, " is again a literal quote, | is for OR,
    /// and (?:(?!\s?-?\w+:)\S)+ is a non-capturing group that matches strings that don't look like a key (by using negative lookahead).
    /// Verbatim strings (those starting with @) switch out the usual escape character of backslash (\) for quote ("), which is why it appears twice
    /// Parentheses and brackets are for grouping the regex itself (VS does a little better at demonstrating this than VS Code).
    /// THIS REGEX WILL BREAK IF THE QUOTE IS REQUIRED AS A LITERAL VALUE IN THE SEARCH (quoted values are only parsed as grouping).
    /// </summary>
    private static readonly string TagPattern = @"(-?\w+)\s?:\s?(""[^""]*""|(?:(?!\s?-?\w+:)\S)+)";

    /// <summary>
    /// Gets the core set of tags available when searching all tables.
    /// </summary>
    public static HashSet<string> UniversalTags { get; } = new (StringComparer.OrdinalIgnoreCase)
        { "in", "barcode", "group", "before", "after", "result" };

    /// <summary>
    /// Gets the set of tags exclusive to the group table.
    /// </summary>
    public static HashSet<string> GroupTags { get; } = new (StringComparer.OrdinalIgnoreCase)
        { "comp", "short", "open", "macro", "ic", "function" };

    /// <summary>
    /// Gets the set of tags exclusive to the step table.
    /// </summary>
    public static HashSet<string> StepTags { get; } = new (StringComparer.OrdinalIgnoreCase)
        { "step", "mode", "part" };

    /// <summary>
    /// Gets the set of tags exclusive to the FCT table.
    /// </summary>
    public static HashSet<string> FctTags { get; } = new (StringComparer.OrdinalIgnoreCase)
        { "step", "mode" };

    /// <summary>
    /// Gets the set of all tags available across the system (union of above sets).
    /// </summary>
    public static HashSet<string> AllTags { get; } = CombineAllTags();

    [GeneratedRegex(InPattern, RegexOptions.IgnoreCase, "en-US")]
    public static partial Regex ApplyInPattern();

    /// <summary>
    /// Gets the list of all supported tags for the current mode
    /// This list does not detect when the "in" key is used.
    /// </summary>
    /// <param name="mode">The mode to check keys for.</param>
    /// <returns>The tags applicable to the current table.</returns>
    public static IEnumerable<string> GetSupportedKeysThisMode(string mode)
    {
        HashSet<string> keys = new (UniversalTags, StringComparer.OrdinalIgnoreCase);

        switch (mode)
        {
            case "group": keys.UnionWith(GroupTags); break;
            case "step": keys.UnionWith(StepTags); break;
            case "fct": keys.UnionWith(FctTags); break;
        }

        return keys.OrderBy(x => x); // Implicitly casts to an orderable implementation of IEnumerable (not a set)
    }

    /// <summary>
    /// Parses a string for key-value pairs. Upon finding the "in" key, immediately updates the table reference to get only valid keys
    /// Catches date aliases and calls TranslateDateAlias() to handle them
    /// Continues upon finding an error in order to find and inform the user of all of them
    /// To comply with PowerSearch.razor, ensure that all non-fatal errors contain "This search".
    /// </summary>
    /// <param name="rawInput">The string to parse.</param>
    /// <param name="currentType">The current table to check.</param>
    /// <param name="isInclusive">Whether date filters include the specified value in their range.</param>
    /// <returns>a SearchParseResult containing the dictionary of filters, list of errors, current table, and preview.</returns>
    public SearchParseResult ParseQuery(string rawInput, string currentType, bool isInclusive = true)
    {
        // Initialize the return package with the current table
        var result = new SearchParseResult
        {
            CurrentType = currentType,
        };

        // If the query is empty, generate the default preview and return immediately
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            result.Preview = GeneratePreview(result.CurrentType, result.Filters); // guarantees that preview matches current state
            return result;
        }

        // In the first pass, look for the "in" keyword to ensure further filters are applicable
        ProcessInTag(rawInput, result);

        MatchCollection matches = Regex.Matches(rawInput, TagPattern);
        int lastIndex = 0; // keep track of the location of the last match to determine if there is a break (invalid tags)

        // Loop through
        foreach (Match match in matches)
        {
            CheckForGaps(rawInput, lastIndex, match.Index, result);
            ProcessTagMatch(match, result, isInclusive);

            lastIndex = match.Index + match.Length; // for use in gap checking in the next iteration
        }

        // Check if there is any unparsed input (that didn't match the pattern)
        if (lastIndex < rawInput.Length)
        {
            CheckForGaps(rawInput, lastIndex, rawInput.Length, result);
        }

        SwapDatesIfInverted(result);
        result.Preview = GeneratePreview(result.CurrentType, result.Filters);
        return result;
    }

    /// <summary>
    /// Detects and assigns the target table based on the value of the "in" tag.
    /// </summary>
    /// <param name="rawInput">The complete search query.</param>
    /// <param name="result">The <see cref="SearchParseResult"/> object containing relevant search information.</param>
    private static void ProcessInTag(string rawInput, SearchParseResult result)
    {
        MatchCollection matches = ApplyInPattern().Matches(rawInput);
        if (matches.Count > 0)
        {
            bool multipleInTags = matches.Count > 1;
            Match contextMatch = matches[^1]; // Always use the last 'in' tag, if multiple
            string polarity = contextMatch.Groups[1].Value;
            string targetType = contextMatch.Groups[2].Value.ToLower();
            string cleanType = targetType.TrimStart('-');

            // No tag can be duplicated, but we need separate handling here to pin down the table now
            if (multipleInTags)
            {
                result.ErrorMessages.Add($"Duplicate **in** tag. This search is now **in:{cleanType}...**. All previous uses of this key are *ignored*.");
            }

            // Can't negate the 'in' tag, so don't assign it, and throw a non-fatal error
            if (polarity.Equals("-"))
            {
                result.ErrorMessages.Add($"The {(multipleInTags ? "active" : string.Empty)} **in** tag cannot be negated. This search is now **in:{cleanType}...**.");
            }

            // Conveniently, this strictness removes the need for a SQL injection check for this tag later
            if (AvailableTypes.Contains(cleanType))
            {
                // If they tried to negate the value for the 'in' tag, provide an error about negating key instead of value and non-negatability of 'in' tag
                if (targetType != cleanType)
                {
                    result.ErrorMessages.Add($"Negation should be applied to the key instead of value, but the **in** tag cannot be negated anyway. This search is now **in:{cleanType}.**");
                }

                result.CurrentType = cleanType;
            }
            else
            {
                result.ErrorMessages.Add($"**{targetType}** is not a valid table. The **in** keyword only accepts the values *all*, *group*, *step*, or *fct*.");
            }
        }
    }

    private static void ProcessTagMatch(Match match, SearchParseResult result, bool isInclusive)
    {
        string key = match.Groups[1].Value.ToLower();
        bool isNegated = key.StartsWith('-');
        string cleanKey = isNegated ? key[1..] : key;
        string value = match.Groups[2].Value;

        // Skip "in" tag (already handled)
        if (cleanKey == "in")
        {
            return;
        }

        // Validate key
        if (!ValidateMatchKey(cleanKey, key, result))
        {
            return;
        }

        int numErrorsBeforeValidation = result.ErrorMessages.Count;

        // Validate and normalize value
        (bool wasNegated, string? normalizedValue) = ValidateAndProcessValue(cleanKey, key, value, result);

        if (result.ErrorMessages.Count > numErrorsBeforeValidation)
        {
            return;
        }

        isNegated = HandleDateNegation(cleanKey, value, wasNegated, result);

        // Register duplicate (but still process it)
        CheckDuplicateKey(cleanKey, key, normalizedValue, result);

        // Create and store filter
        result.Filters[cleanKey] = CreateFilter(cleanKey, normalizedValue, isNegated, isInclusive);
    }

    private static (bool isNegated, string normalizedValue) ValidateAndProcessValue(string cleanKey, string key, string value, SearchParseResult result)
    {
        // If a user put a hyphen on their value, they probably wanted to negate
        bool isNegated = false;
        if (value.StartsWith('-'))
        {
            isNegated = true;
            value = value[1..];
            result.ErrorMessages.Add($"The value for **{cleanKey}** started with a hyphen. This search is now **-{cleanKey}:{value}...**. To search for a literal hyphen, use quotes like *{key}:\"-{value}\"*.");
        }

        value = value.Trim('"');

        // Basic SQL injection countermeasure
        if (SqlBlacklist.Any(forbidden => value.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
        {
            result.ErrorMessages.Add($"Security Issue: The value for **{key}** contains forbidden keywords.");
            return (false, value);
        }

        // Validate if value matches the datatype required by the key
        if (TagTypeMap.TryGetValue(cleanKey, out ValType expectedType) && !IsValidValue(expectedType, cleanKey, value, out string errorMessage))
        {
            result.ErrorMessages.Add($"Invalid value for the **{key}** tag. {errorMessage}");
            return (false, value);
        }

        return (isNegated, value);
    }

    private static bool HandleDateNegation(string cleanKey, string value, bool isNegated, SearchParseResult result)
    {
        // Attempting to negate before/after isn't fatal, but it needs to be deactivated
        if (isNegated && (cleanKey == "before" || cleanKey == "after"))
        {
            result.ErrorMessages.Add($"The **{cleanKey}** tag cannot be negated. This search is now **{cleanKey} : {value}...**");
            return false; // revoke negation
        }

        return isNegated;
    }

    /// <summary>
    /// Creates a filter for a key-value pair with its polarity.
    /// </summary>
    /// <param name="key">The filter's name.</param>
    /// <param name="value">The filter's value.</param>
    /// <param name="isNegated">The filter's polarity (true when negated).</param>
    /// <param name="isInclusive">Whether to treat date filters as inclusive of their value (or exclusive).</param>
    /// <returns>The filter constructed from its components.</returns>
    private static IFilter CreateFilter(string key, string value, bool isNegated, bool isInclusive)
    {
        // Determine the expected type from TagTypeMap
        if (!TagTypeMap.TryGetValue(key, out ValType type))
        {
            type = ValType.String; // Default fallback
        }

        // Instantiate the correct generic Filter<T>
        return type switch
        {
            ValType.Int => new Filter<int?>(key, int.TryParse(value, out int i) ? i : null, isNegated),

            ValType.DateTime => new Filter<DateTime?>(key, ProcessDateValue(key, value, isInclusive), false), // Negation not allowed for dates

            // String already handles empty/null internally
            _ => new Filter<string?>(key, value, isNegated),
        };
    }

    private static HashSet<string> CombineAllTags()
    {
        HashSet<string> all = new (UniversalTags, StringComparer.OrdinalIgnoreCase);
        all.UnionWith(GroupTags);
        all.UnionWith(StepTags);
        all.UnionWith(FctTags);
        return all;
    }
}
