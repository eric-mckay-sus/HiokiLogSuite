using System.Text.RegularExpressions;
using HiokiNL2SQLMark1.Logic;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace HiokiNL2SQLMark1;
/// <summary>
/// A service to contain state and methods relevant for parsing. Required to be injected into PowerSearch.razor
/// </summary>
public class SearchParserService
{
    // Regex to find key:value pairs. -? detects optional negation, \w+ detects the key text, \s* surrounding the colon detects whitespace, : is the literal colon,
    // " is a literal quote, [^"]* iterates over non-quote characters, " is again a literal quote, | is for OR, and \S+ detects the value text
    // Verbatim strings (those starting with @) switch out the usual escape character of backslash (\) for quote ("), which is why it appears twice
    // Parentheses and brackets are for grouping the regex itself (VS does a little better at demonstrating this than VS Code).
    // THIS REGEX WILL BREAK IF THE QUOTE IS REQUIRED AS A LITERAL VALUE IN THE SEARCH (quoted values are only parsed as grouping)
    readonly string tagPattern = @"(-?\w+)\s*:\s*(""[^""]*""|\S+)";
    public string inPattern = @"(-?)in\s*:\s*(\w+)"; // represents the key-value pair for the "in" tag. Includes optional negation
    public static readonly string[] availableTypes = ["all", "group", "step", "fct"]; // all available tables

    // Basic SQL injection countermeasure (these words are disallowed in a query)
    private readonly static HashSet<string> sqlBlacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "DROP", "DELETE", "UPDATE", "INSERT", "TRUNCATE", 
        "EXEC", "EXECUTE", "ALTER", "CREATE", "GRANT", "REVOKE"
    };

    // Sets of which tags are available to which tables. StepFctTags inherits from StepTags and FctTags, and AllTags inherits from all other tag sets
    readonly static HashSet<string> UniversalTags = new(StringComparer.OrdinalIgnoreCase) 
        { "in", "barcode", "group", "before", "after", "result" }; // tags available for use on any table
    readonly static HashSet<string> GroupTags = new(StringComparer.OrdinalIgnoreCase) 
        { "comp", "short", "macro", "ic", "function" }; // tags available to group table only
    readonly static HashSet<string> StepFctTags = new(StringComparer.OrdinalIgnoreCase) 
        { "step", "mode" }; // tags available to both the step and FCT tables
    readonly static HashSet<string> StepTags = CreateStepSet(); // tags available to step table only
    static HashSet<string> CreateStepSet() => new(StepFctTags, StringComparer.OrdinalIgnoreCase){"part"};
    readonly static HashSet<string> FctTags = StepFctTags; // tags available to FCT table only (alias for StepFctTags at the moment)
    public readonly static HashSet<string> AllTags = CombineAllTags(); // all tags available to the system (union of all other tables)
    private static HashSet<string> CombineAllTags()
    {
        HashSet<string> all = new(UniversalTags, StringComparer.OrdinalIgnoreCase);
        all.UnionWith(GroupTags);
        all.UnionWith(StepTags);
        all.UnionWith(FctTags);
        return all;
    }

    private enum ValType { String, Int, DateTime} // enumerates the types allowed by a tag

    // Maps each tag type to 
    private static readonly Dictionary<string, ValType> TagTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "barcode", ValType.String },
        { "group", ValType.Int },
        { "after", ValType.DateTime },
        { "before", ValType.DateTime },
        { "result", ValType.String },
        { "comp", ValType.String },
        { "short", ValType.String },
        { "macro", ValType.String },
        { "ic", ValType.String },
        { "function", ValType.String },
        { "step", ValType.Int },
        { "mode", ValType.String },
        { "part", ValType.String }
    };

    private static readonly Dictionary<string, string> TagDescriptions = new(StringComparer.OrdinalIgnoreCase) // maps search tags to their tooltip
    {
        { "in", "Switch table scope (all, group, step, fct)" },
        { "barcode", "Search by unique PCB identifier" },
        { "group", "Filter by specific group name" },
        { "before", "Show results recorded before this date (exclusive)" },
        { "after", "Show results recorded after this date (inclusive)" },
        { "result", "Filter by PASS/FAIL status" },
        { "comp", "Filter by component reference (e.g., R101)" },
        { "short", "Filter by short-circuit test results" },
        { "macro", "Search by macro-test identifier" },
        { "ic", "Filter by Integrated Circuit (IC) name" },
        { "function", "Search by specific function test name" },
        { "step", "Filter by test step name" },
        { "mode", "Filter by test mode" },
        { "part", "Search by part number" }
    };

    /// <summary>
    /// Dynamically gets the tooltip based on what tables in which a key is valid
    /// </summary>
    /// <param name="key">The key for which to get the tooltip</param>
    /// <param name="showScope">Whether to provide the scope of this tag</param>
    /// <returns>The tooltip associated with this string (and optional scope)</returns>
    public static string GetTagTooltip(string key, bool showScope) 
    {
        // Get the functional description
        if (!TagDescriptions.TryGetValue(key, out var description)) description = $"Filter by {key}";

        // If instructed not to return the scope, return now
        if(!showScope) return description;

        // Otherwise, gather scope info by scanning tag sets
        string scopeInfo;
        if (UniversalTags.Contains(key)) {
            scopeInfo = "All tables";
        }
        else
        {
            List<string> locations = [];
            if (GroupTags.Contains(key)) locations.Add("Group");
            if (StepTags.Contains(key)) locations.Add("Step");
            if (FctTags.Contains(key))   locations.Add("FCT");

            scopeInfo = locations.Count > 0 ? string.Join(", ", locations) : "System tag";
        }

        return $"{description} | Works in: {scopeInfo}";
    }

    /// <summary>
    /// A container for the return values from the parser
    /// </summary>
    public class SearchParseResult
    {
        public Dictionary<string, IFilter> Filters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ErrorMessages { get; set; } = [];
        public string CurrentType { get; set; } = "all";
        public string Preview { get; set; } = "Searching all records...";
    }

    /// <summary>
    /// Parses a string for key-value pairs. Upon finding the "in" key, immediately updates the table reference to get only valid keys
    /// Catches date aliases and calls TranslateDateAlias() to handle them
    /// Continues upon finding an error in order to find and inform the user of all of them
    /// To comply with PowerSearch.razor, ensure that all non-fatal errors contain "This search is now"
    /// </summary>
    /// <param name="rawInput">The string to parse</param>
    /// <param name="currentType">The current table to check</param>
    /// <returns>a SearchParseResult containing the dictionary of filters, list of errors, current table, and preview</returns>
    public SearchParseResult ParseQuery(string rawInput, string currentType)
    {
        // Initialize the return package with the current table
        var result = new SearchParseResult{CurrentType = currentType};

        // If the query is empty, generate the default preview and return immediately
        if (string.IsNullOrWhiteSpace(rawInput)){
            result.Preview = GeneratePreview(result.CurrentType, result.Filters); // guarantees that preview matches current state
            return result;
        }

        // In the first pass, look for the "in" keyword to ensure further filters are applicable
        Match? contextMatch = Regex.Match(rawInput, inPattern, RegexOptions.IgnoreCase);
        if (contextMatch.Success)
        {
            string polarity = contextMatch.Groups[1].Value; // only two options from regex: '-' or empty
            string targetType = contextMatch.Groups[2].Value.ToLower();

            if (polarity == "-") result.ErrorMessages.Add($"The 'in' tag cannot be negated. This search is now 'in : {targetType}...'.");

            if (availableTypes.Contains(targetType))
            {
                result.CurrentType = targetType;
            }
            else
            {
                result.ErrorMessages.Add($"'{targetType}' is not a valid table. The 'in' keyword only accepts the values 'all', 'group', 'step', or 'fct'.");
            }
        }

        // Now the target table  is certain, we can enumerate all the valid keys
        HashSet<string> allowedKeys = new(GetSupportedKeysThisMode(result.CurrentType), StringComparer.OrdinalIgnoreCase);
        var matches = Regex.Matches(rawInput, tagPattern);
        int lastIndex = 0; // keep track of the location of the last match to determine if there is a break (invalid tags)

        foreach (Match match in matches)
        {
            // Create error if the space between the last match and this one contains non-whitespace characters
            string gap = rawInput[lastIndex..match.Index].Trim();
            if (!string.IsNullOrWhiteSpace(gap)) result.ErrorMessages.Add($"Unrecognized input: '{gap}'. Did you forget a tag?");

            string? key = match.Groups[1].Value.ToLower();
            bool isNegated = key.StartsWith('-');
            string cleanKey = isNegated ? key[1..] : key; // for use in checking against key sets
            string? value = match.Groups[2].Value.Trim('"'); // cut the quotes, if the regex found them

            // Basic SQL injection countermeasure
            if (sqlBlacklist.Any(forbidden => value.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
            {
                result.ErrorMessages.Add($"Security Issue: The value for '{key}' contains forbidden keywords.");
                lastIndex = match.Index + match.Length; // move the index so the skipped tag isn't flagged as bad input again
                continue;
            }

            // Validate if key is supported by system
            if (!AllTags.Contains(cleanKey)) {
                result.ErrorMessages.Add($"The tag '{key}' wasn't recognized. Try using the table and key options below the search bar.");
                lastIndex = match.Index + match.Length; // move the index so the skipped tag isn't flagged as bad input again
                continue;
            }
            // Validate if value matches the datatype required by the key
            if (TagTypeMap.TryGetValue(cleanKey, out var expectedType)) {
                if (!IsValidValue(expectedType, value, out string errorMessage)) {
                    result.ErrorMessages.Add($"Invalid value for '{key}': {errorMessage}");
                    lastIndex = match.Index + match.Length; // move the index so the skipped tag isn't flagged as bad input again
                    continue;
                }
            }
            // Validate if key is supported by the selected table
            if (!allowedKeys.Contains(cleanKey) && cleanKey != "in") {
                result.ErrorMessages.Add($"The tag '{key}:' is not available when searching '{result.CurrentType}'. Try a different tag or search a table with that attribute.");
                lastIndex = match.Index + match.Length; // move the index so the skipped tag isn't flagged as bad input again
                continue;
            }
            // Validate if filter was already used in this search. If it was, proceed and overwrite, but notify the user
            if (result.Filters.ContainsKey(cleanKey)) {
                result.ErrorMessages.Add($"Duplicate tag detected: '{key}:'. This search is now '{key}:{value}...'. The previous use of this key is ignored.");
            }
            // If there weren't any errors, add the tag to the dictionary, looking up the alias if applicable
            if (isNegated && (cleanKey == "before" || cleanKey == "after")) { // Attempting to negate before/after isn't fatal
                result.ErrorMessages.Add($"The '{cleanKey}' tag cannot be negated. This search is now '{cleanKey} : {value}...'");
                isNegated = false; // revoke negation for these keys
            }
            // we didn't actually remove "in", we just ignored it
            if (cleanKey != "in") { // if it's not a datetime, it doesn't get special treatment
                result.Filters[cleanKey] = CreateFilter(cleanKey, value, isNegated);
            }
            lastIndex = match.Index + match.Length; // for use in gap checking in the next iteration
        }

        // Verify that the start date is actually before the end date
        if(result.Filters.TryGetValue("before", out IFilter? before) && result.Filters.TryGetValue("after", out IFilter? after))
        {
            // Cast to DateTime filters and proceed
            if (before is Filter<DateTime> b && after is Filter<DateTime> a)
            {
                // Compare the typed values directly
                if (a.Value > b.Value && b.Value != DateTime.MinValue)
                {
                    result.ErrorMessages.Add($"Your start date is after your end date. This search is now 'after{b.Value:yyyy-MM-dd} before:{a.Value:yyyy-MM-dd}...'");

                    // Swap the values in the filter objects in the dictionary
                    (b.Value, a.Value) = (a.Value, b.Value);
                }
            }
        }

        // Check if there is any unparsed input (that didn't match the pattern)
        if (lastIndex < rawInput.Length)
        {
            string trailing = rawInput[lastIndex..].Trim();
            if (!string.IsNullOrWhiteSpace(trailing))
            {
                // Check if it's a key without a value
                if (trailing.EndsWith(':'))
                    result.ErrorMessages.Add($"Tag '{trailing}' is missing a value.");
                // or a value without key
                else
                    result.ErrorMessages.Add($"Unrecognized filter without key: '{trailing}'.");
            }
        }
        result.Preview = GeneratePreview(result.CurrentType, result.Filters);
        return result;
    }

    /// <summary>
    /// Used by ParseQuery to separate and translate a datetime
    /// Splits and rebuilds string to handle combined date and time aliases like "today shift1"
    /// </summary>
    /// <param name="value">The datetime to process</param>
    /// <returns>The ISO date string representing the input datetime</returns>
    private static string ProcessDateValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Translate parts separately and re-combine
        if (parts.Length > 1) {
            string datePart = TranslateDateAlias(parts[0], isTimePart: false);
            string timePart = TranslateDateAlias(parts[1], isTimePart: true);
            return $"{datePart} {timePart}".Trim();
        }
        // Otherwise treat it as a date only
        return TranslateDateAlias(value, isTimePart: false);
    }

    /// <summary>
    /// Verifies that a value matches a certain type
    /// </summary>
    /// <param name="type">A ValType (enum) representing the required type</param>
    /// <param name="value">The value for which to check the type</param>
    /// <param name="error">The error message (in case of failure)</param>
    /// <returns></returns>
    private static bool IsValidValue(ValType type, string value, out string error)
    {
        error = string.Empty;
        switch (type)
        {
            // If we're checking a field required to be an int, use int.TryParse
            case ValType.Int:
                if (!int.TryParse(value, out _))
                {
                    error = $"'{value}' is not a whole number.";
                    return false;
                }
                break;
            // If we're checking a field required to be a datetime, use DateTime.TryParse
            case ValType.DateTime:
                string normalized = ProcessDateValue(value);
                if (string.IsNullOrEmpty(normalized)) {
                    error = $"Date (read as '{normalized}') cannot be empty.";
                    return false;
                }
                if (!DateTime.TryParse(normalized, System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    error = $"'{value}' (read as '{normalized}') is not a valid date or alias. Please use YYYY-MM-DD or a shortcut below.";
                    return false;
                }
                break;
        }
        return true;
    }

    private static IFilter CreateFilter(string key, string value, bool isNegated)
    {
        // Determine the expected type from TagTypeMap
        if (!TagTypeMap.TryGetValue(key, out var type)) type = ValType.String; // Default fallback

        // Instantiate the correct generic Filter<T>
        return type switch
        {
            ValType.Int => new Filter<int>(key, int.TryParse(value, out int i) ? i : 0, true, isNegated),
            ValType.DateTime => new Filter<DateTime>(key, DateTime.TryParse(ProcessDateValue(value), out var dt) ? dt : DateTime.MinValue, true, false),
            _ => new Filter<string>(key, value, true, isNegated)
        };
    }

    /// <summary>
    /// Translates the dictionary created by ParseQuery into a human-readable preview of what query would be executed if the search was run now
    /// </summary>
    /// <param name="type">The table targeted by the query</param>
    /// <param name="filters">The dictionary of filters for the query</param>
    /// <returns>A string preview of the query to be executed</returns>
    private static string GeneratePreview(string type, Dictionary<string, IFilter> filters)
    {
        string tableMessage = $"Showing all results";
        tableMessage += (type!="all") ? $" from **{type.ToUpper()}**" : " from ALL tables";
        if(filters.Count == 0) return tableMessage;

        // Build string snippets for each filter
        var parts = filters.Values.Select(filter => 
        {
            string cleanKey = filter.Key.ToUpper();
            string negationLabel = filter.IsNegated ? " NOT " : " ";
            string containLabel = filter.IsNegated ? "does NOT contain" : "contains";

            // Branch based on datatype
            return filter switch
            {
                // DateTime filter with unrecognized date
                Filter<DateTime> dtFilter when dtFilter.Value == DateTime.MinValue => 
                    $"DATE is **{cleanKey}** (incomplete date...)'",

                // DateTime Filters (format as datetime)
                Filter<DateTime> dtFilter => 
                    $"DATE is **{cleanKey}** '{dtFilter.Value:yyyy-MM-dd HH:mm}'",

                // Integer filters (translates to SQL '=')
                Filter<int> intFilter => 
                    $"**{cleanKey}** is{negationLabel}'{intFilter.Value}'",

                // String filters (translates to SQL 'LIKE')
                Filter<string> strFilter => 
                    $"**{cleanKey}** {containLabel} '{strFilter.Value}'",

                // Default fallback
                _ => $"**{cleanKey}** is{negationLabel}'{filter.GetValue()}'"
            };
        });

        return $"Searching **{type.ToUpper()}** where " + string.Join(" and ", parts);
    }

    /// <summary>
    /// Gets the list of all supported tags for the current mode
    /// This list does not detect when the "in" key is used
    /// </summary>
    /// <param name="mode">The mode to check keys for</param>
    /// <returns>The tags applicable to the current table</returns>
    public static IEnumerable<string> GetSupportedKeysThisMode(string mode)
    {
        HashSet<string> keys = new(UniversalTags, StringComparer.OrdinalIgnoreCase);

        switch(mode) {
            case "group": keys.UnionWith(GroupTags); break;
            case "step":  keys.UnionWith(StepTags); break;
            case "fct":   keys.UnionWith(FctTags); break;
        }

        return keys.OrderBy(x => x); 
    }

    /// <summary>
    /// Translates aliases like "today", "last24h", and "shift2" to valid datetimes
    /// </summary>
    /// <param name="alias">the alias to translate to a datetime</param>
    /// <returns>The datetime referred to by the alias</returns>
    public static string TranslateDateAlias(string alias, bool isTimePart = false)
    {
        DateTime now = DateTime.Today;
        string lowerAlias = alias.ToLower();

        // adjust as needed, these are approximate
        string s1 = "07:00:00";
        string s2 = "15:00:00";
        string s3 = "23:00:00";

        return lowerAlias switch
        {
            "today"     => now.ToString("yyyy-MM-dd"),
            "yesterday" => now.AddDays(-1).ToString("yyyy-MM-dd"),
            "lastweek"  => now.AddDays(-7).ToString("yyyy-MM-dd"),
            "last24h"   => DateTime.Now.AddHours(-24).ToString("yyyy-MM-dd HH:mm:ss"), // ensure 24 hours since this moment, not just since this morning
            "shift1"    => isTimePart ? s1 : $"{now:yyyy-MM-dd} {s1}",
            "shift2"    => isTimePart ? s2 : $"{now:yyyy-MM-dd} {s2}",
            "shift3"    => isTimePart ? s3 : $"{now:yyyy-MM-dd} {s3}",
            _           => alias // If it's not an alias, hopefully it's already a datetime. Return the original string (e.g., 2024-01-01)
        };
    }
}