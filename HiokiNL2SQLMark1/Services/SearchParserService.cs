using System.Text.RegularExpressions;
using HiokiNL2SQLMark1.Logic;

namespace HiokiNL2SQLMark1.Services;
/// <summary>
/// A service to contain state and methods relevant for parsing. Required to be injected into PowerSearch.razor
/// This service does have state, but it is all static
/// </summary>
public class SearchParserService
{
    // Regex to find key:value pairs. -? detects optional negation, \w+ detects the key text, \s* surrounding the colon detects whitespace,
    // : is the literal colon, " is a literal quote, [^"]* iterates over non-quote characters, " is again a literal quote, | is for OR,
    // and (?:(?!\s?-?\w+:)\S)+ is a non-capturing group that matches strings that don't look like a key (by using negative lookahead).
    // Verbatim strings (those starting with @) switch out the usual escape character of backslash (\) for quote ("), which is why it appears twice
    // Parentheses and brackets are for grouping the regex itself (VS does a little better at demonstrating this than VS Code).
    // THIS REGEX WILL BREAK IF THE QUOTE IS REQUIRED AS A LITERAL VALUE IN THE SEARCH (quoted values are only parsed as grouping)
    protected const string tagPattern = @"(-?\w+)\s?:\s?(""[^""]*""|(?:(?!\s?-?\w+:)\S)+)";
    public const string inPattern = @"(-?)in\s*:\s*(\w+)"; // represents the key-value pair for the "in" tag. Includes optional negation
    public static readonly string[] availableTypes = ["all", "group", "step", "fct"]; // all available tables

    // Basic SQL injection countermeasure (these words are disallowed in a query)
    private readonly static HashSet<string> sqlBlacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "DROP", "DELETE", "UPDATE", "INSERT", "TRUNCATE", 
        "EXEC", "EXECUTE", "ALTER", "CREATE", "GRANT", "REVOKE"
    };

    // Sets of which tags are available to which tables. StepFctTags inherits from StepTags and FctTags, and AllTags inherits from all other tag sets
    private static readonly HashSet<string> UniversalTags = new(StringComparer.OrdinalIgnoreCase) 
        { "in", "barcode", "group", "before", "after", "result" }; // tags available for use on any table
    private static readonly HashSet<string> GroupTags = new(StringComparer.OrdinalIgnoreCase) 
        { "comp", "short", "macro", "ic", "function" }; // tags available to group table only
    private static readonly HashSet<string> StepFctTags = new(StringComparer.OrdinalIgnoreCase) 
        { "step", "mode" }; // tags available to both the step and FCT tables
    private static readonly HashSet<string> StepTags = CreateStepSet(); // tags available to step table only
    private static HashSet<string> CreateStepSet() => new(StepFctTags, StringComparer.OrdinalIgnoreCase){"part"};
    private static readonly HashSet<string> FctTags = StepFctTags; // tags available to FCT table only (alias for StepFctTags at the moment)
    public static readonly HashSet<string> AllTags = CombineAllTags(); // all tags available to the system (union of all other tables)
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
        { "barcode", "Search by unique PCB serial number" },
        { "group", "Filter by group number in test sequence" },
        { "before", "Show results recorded before this date (exclusive)" },
        { "after", "Show results recorded after this date (inclusive)" },
        { "result", "Filter by PASS/FAIL status" },
        { "comp", "Filter by component test results" },
        { "short", "Filter by short-circuit test results" },
        { "macro", "Search by macro-test results" },
        { "ic", "Filter by Integrated Circuit (IC) test results" },
        { "function", "Search by functional test results" },
        { "step", "Filter by test step number" },
        { "mode", "Filter by test mode" },
        { "part", "Search by part name" }
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
    /// To comply with PowerSearch.razor, ensure that all non-fatal errors contain "This search"
    /// </summary>
    /// <param name="rawInput">The string to parse</param>
    /// <param name="currentType">The current table to check</param>
    /// <param name="isInclusive">Whether date filters include the specified value in their range</param>
    /// <returns>a SearchParseResult containing the dictionary of filters, list of errors, current table, and preview</returns>
    public SearchParseResult ParseQuery(string rawInput, string currentType, bool isInclusive=true)
    {
        // Initialize the return package with the current table
        var result = new SearchParseResult{CurrentType = currentType};

        // If the query is empty, generate the default preview and return immediately
        if (string.IsNullOrWhiteSpace(rawInput)){
            result.Preview = GeneratePreview(result.CurrentType, result.Filters); // guarantees that preview matches current state
            return result;
        }

        // In the first pass, look for the "in" keyword to ensure further filters are applicable
        var matches = Regex.Matches(rawInput, inPattern, RegexOptions.IgnoreCase);
        if (matches.Count > 0)
        {
            Match contextMatch = matches[^1];
            string polarity = contextMatch.Groups[1].Value; // only two options from regex: '-' or empty
            string targetType = contextMatch.Groups[2].Value.ToLower();
            if (matches.Count > 1) result.ErrorMessages.Add($"Duplicate **in** tag. This search is now **in:{targetType}...**. All previous uses of this key are *ignored*.");

            if (polarity == "-") result.ErrorMessages.Add($"The **in** tag cannot be negated. This search is now **in : {targetType}...**.");

            if (availableTypes.Contains(targetType))
            {
                result.CurrentType = targetType;
            }
            else
            {
                result.ErrorMessages.Add($"**{targetType}** is not a valid table. The **in** keyword only accepts the values *all*, *group*, *step*, or *fct*.");
            }
        }

        // Now the target table  is certain, we can enumerate all the valid keys
        HashSet<string> allowedKeys = new(GetSupportedKeysThisMode(result.CurrentType), StringComparer.OrdinalIgnoreCase);
        matches = Regex.Matches(rawInput, tagPattern);
        int lastIndex = 0; // keep track of the location of the last match to determine if there is a break (invalid tags)

        foreach (Match match in matches)
        {
            // Create error if the space between the last match and this one contains non-whitespace characters
            string gap = rawInput[lastIndex..match.Index].Trim();
            string message = MissingKeyOrValueMessage(gap);
            if (!string.IsNullOrEmpty(message)) result.ErrorMessages.Add(message);

            string? key = match.Groups[1].Value.ToLower();
            bool isNegated = key.StartsWith('-');
            string cleanKey = isNegated ? key[1..] : key; // for use in checking against key sets
            string? value = match.Groups[2].Value;

            // If a user put a hyphen on their value, they probably wanted to negate, but we can supply a warning for them to learn
            if (value.StartsWith('-')) 
            {
                isNegated = true;
                value = value[1..];
                result.ErrorMessages.Add($"The value for **{cleanKey}** started with a hyphen. This search is now **-{cleanKey}:{value}...**. To search for a literal hyphen, use quotes like *{key}:\"-{value}\"*.");
            }
            value = value.Trim('"'); // cut the quotes, if the regex found them (they're no longer protecting anything)

            // Basic SQL injection countermeasure
            if (sqlBlacklist.Any(forbidden => value.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
            {
                result.ErrorMessages.Add($"Security Issue: The value for **{key}** contains forbidden keywords.");
                lastIndex = match.Index + match.Length; // move the index so the skipped tag isn't flagged as bad input again
                continue;
            }

            // Validate if key is supported by system
            if (!AllTags.Contains(cleanKey)) {
                result.ErrorMessages.Add($"The tag **{key}** wasn't recognized. Try using the table and key options below the search bar.");
                lastIndex = match.Index + match.Length; // move the index so the skipped tag isn't flagged as bad input again
                continue;
            }
            // Validate if value matches the datatype required by the key
            if (TagTypeMap.TryGetValue(cleanKey, out var expectedType)) {
                if (!IsValidValue(expectedType, cleanKey, value, out string errorMessage)) {
                    result.ErrorMessages.Add($"Invalid value for **{key}**: {errorMessage}");
                    lastIndex = match.Index + match.Length; // move the index so the skipped tag isn't flagged as bad input again
                    continue;
                }
            }
            // Validate if key is supported by the selected table
            if (!allowedKeys.Contains(cleanKey) && cleanKey != "in") {
                result.ErrorMessages.Add($"The tag **{key}:** is not available when searching **{result.CurrentType}**. Try a different tag or search a table with that attribute.");
                lastIndex = match.Index + match.Length; // move the index so the skipped tag isn't flagged as bad input again
                continue;
            }
            // Validate if filter was already used in this search. If it was, proceed and overwrite, but notify the user
            if (result.Filters.ContainsKey(cleanKey)) {
                result.ErrorMessages.Add($"Duplicate tag detected: **{key}:**. This search is now '**{key}:{value}...**. The previous use of this key is *ignored*.");
            }
            // If there weren't any errors, add the tag to the dictionary, looking up the alias if applicable
            if (isNegated && (cleanKey == "before" || cleanKey == "after")) { // Attempting to negate before/after isn't fatal
                result.ErrorMessages.Add($"The **{cleanKey}** tag cannot be negated. This search is now **{cleanKey} : {value}...**");
                isNegated = false; // revoke negation for these keys
            }
            // we didn't actually remove "in", we just ignored it
            if (cleanKey != "in") { // if it's not a datetime, it doesn't get special treatment
                result.Filters[cleanKey] = CreateFilter(cleanKey, value, isNegated, isInclusive);
            }
            lastIndex = match.Index + match.Length; // for use in gap checking in the next iteration
        }

        // Verify that the start date is actually before the end date
        if(result.Filters.TryGetValue("before", out IFilter? before) && result.Filters.TryGetValue("after", out IFilter? after))
        {
            // Cast to DateTime filters and proceed
            if (before is Filter<DateTime?> b && after is Filter<DateTime?> a)
            {
                // Compare the typed values directly
                if (a.Value.HasValue && b.Value.HasValue)
                {
                    if (a.Value > b.Value)
                    {
                        result.ErrorMessages.Add($"Your start date is after your end date. This search is now **after:{b.Value:yyyy-MM-dd} before:{a.Value:yyyy-MM-dd}...**");

                        // Swap the values inside the filter objects in the dictionary
                        (b.Value, a.Value) = (a.Value, b.Value);
                    }
                }
            }
        }

        // Check if there is any unparsed input (that didn't match the pattern)
        if (lastIndex < rawInput.Length)
        {
            string trailing = rawInput[lastIndex..].Trim();
            string message = MissingKeyOrValueMessage(trailing);
            if (!string.IsNullOrEmpty(message)) result.ErrorMessages.Add(message);
        }
        result.Preview = GeneratePreview(result.CurrentType, result.Filters);
        return result;
    }

    /// <summary>
    /// Generates an error message depending on whether the string is a keyless value or value without a key
    /// </summary>
    /// <param name="toCheck">The string for which to generate the error</param>
    /// <returns>An error message describing the missing key/value</returns>
    private static string MissingKeyOrValueMessage(string toCheck)
    {
        if (!string.IsNullOrWhiteSpace(toCheck))
        {
            // Check if it's a key without a value
            if (toCheck.EndsWith(':')) {
                return $"Tag **{toCheck}** is missing a value. This search excludes **{toCheck}**.";
            }
            // or a value without key
            else {
                return $"Unrecognized filter without key: **{toCheck}**. This search excludes **{toCheck}**.";
            }
        }
        return null;
    }

    /// <summary>
    /// Verifies that a value matches a certain type
    /// </summary>
    /// <param name="type">A ValType (enum) representing the required type</param>
    /// <param name="value">The value for which to check the type</param>
    /// <param name="error">The error message (in case of failure)</param>
    /// <returns>Whether the value matches the type specified</returns>
    private static bool IsValidValue(ValType type, string key, string value, out string error)
    {
        error = string.Empty;
        switch (type)
        {
            // If we're checking a field required to be an int, use int.TryParse
            case ValType.Int:
                if (!int.TryParse(value, out _))
                {
                    error = $"**{value}** is not a whole number.";
                    return false;
                }
                break;
            // If we're checking a field required to be a datetime, use DateTime.TryParse
            case ValType.DateTime:
                string normalized = ProcessDateValue(key, value); // disregard inclusivity for this check
                if (string.IsNullOrEmpty(normalized)) {
                    error = $"Date (read as **{normalized}**) cannot be empty.";
                    return false;
                }
                if (!DateTime.TryParse(normalized, System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    error = $"**{value}** (read as **{normalized}**) is not a valid date or alias. Please use YYYY-MM-DD or a shortcut below.";
                    return false;
                }
                break;
        }
        // The input value is already a string, so there's no check when
        return true;
    }

    /// <summary>
    /// Creates a filter for a key-value pair with its polarity
    /// </summary>
    /// <param name="key">The filter's name</param>
    /// <param name="value">The filter's value</param>
    /// <param name="isNegated">The filter's polarity</param>
    /// <param name="isInclusive">Whether to treat date filters as inclusive of their value (or exclusive)</param>
    /// <returns>The filter constructed from its components</returns>
    private static IFilter CreateFilter(string key, string value, bool isNegated, bool isInclusive)
    {
        // Determine the expected type from TagTypeMap
        if (!TagTypeMap.TryGetValue(key, out var type)) type = ValType.String; // Default fallback

        // Instantiate the correct generic Filter<T>
        return type switch
        {
            ValType.Int => new Filter<int?>(key, int.TryParse(value, out int i) ? i : null, isNegated),

            ValType.DateTime => new Filter<DateTime?>(key, DateTime.TryParse(ProcessDateValue(key, value, isInclusive:isInclusive), out var dt) ? dt : null, false), // Negation not allowed for dates

            // String already handles empty/null internally
            _ => new Filter<string?>(key, value, isNegated),
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
        tableMessage += (type!="all") ? $" from **{type.ToUpper()}**" : " from **ALL** tables";
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

                // DateTime Filters (format as datetime)
                Filter<DateTime?> { Value: { } dtValue } => 
                    $"DATE is **{cleanKey}** '{dtValue:yyyy-MM-dd HH:mm}'",

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

        return keys.OrderBy(x => x); // Implicitly casts to an orderable implementation of IEnumerable (not a set)
    }

    /// <summary>
    /// Used by ParseQuery to separate and translate a datetime
    /// Splits and rebuilds string to handle combined date and time aliases like "today shift1"
    /// </summary>
    /// <param name="key">The key for which to get the datetime boundary (should be 'before' or 'after')</param>
    /// <param name="value">The value for the key, with optional aliases</param>
    /// <param name="isInclusive">Whether the boundary datetime should be inclusive of the value provided</param>
    /// <returns>The full ISO date string representing the boundary for the date filter</returns>
    private static string ProcessDateValue(string key, string value, bool isInclusive=true)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool isBefore = key.Equals("before", StringComparison.OrdinalIgnoreCase);

        // Translate parts separately and re-combine
        if (parts.Length > 1) {
            string datePart = TranslateDateAlias(parts[0], isTimePart:false, isBefore:isBefore, isInclusive:isInclusive);
            string timePart = TranslateDateAlias(parts[1], isTimePart:true, isBefore:isBefore, isInclusive:isInclusive);

            // If datePart returned a full ISO string (with time) extract only the date
            if(DateTime.TryParse(datePart, out var dt)) datePart = dt.ToString("yyyy-MM-dd");
            return $"{datePart} {timePart}".Trim();
        }
        // Otherwise treat it as a date only
        return TranslateDateAlias(value, isTimePart:false, isBefore:isBefore, isInclusive:isInclusive);
    }

    /// <summary>
    /// Generates a datetime boundary for aliases like "today", "last24h", and "shift2" to valid datetimes
    /// Bounding datetime is sensitive to filter type (e.g. inclusive after:shift1 gets start time of shift 1)
    /// </summary>
    /// <param name="alias">The alias for which to get the bounding datetime</param>
    /// <param name="isTimePart">Whether to get only the time for this alias (or to get the full datetime)</param>
    /// <param name="isBefore">Whether the key to be used is 'before' (or 'after')</param>
    /// <param name="isInclusive">Whether the bound</param>
    /// <returns>The ISO date string representing the boundary for use of this alias with this key</returns>
    public static string TranslateDateAlias(string alias, bool isTimePart, bool isBefore, bool isInclusive=true)
    {
        DateTime now = DateTime.Now;
        DateTime today = DateTime.Today;
        string lowerAlias = alias.ToLower();
        // We avoid excess branching/method definitions by using the knowledge that an exclusive filter will use the opposite endpoint as the inclusive filter
        if(!isInclusive) isBefore = !isBefore;

        // adjust as needed, these are approximate
        TimeSpan s1Start = new(7,0,0);
        TimeSpan s2Start = new(15,0,0);
        TimeSpan s3Start = new(23,0,0);
        TimeSpan shiftDuration = TimeSpan.FromHours(8);

        string result = lowerAlias switch
        {
            // Today (inclusive) goes from the end of today if using 'before', but the start of today if using 'after'
            "today"     => isBefore ? today.AddDays(1).AddTicks(-1).ToString("yyyy-MM-dd HH:mm:ss")
                                    : today.ToString("yyyy-MM-dd HH:mm:ss"),
            // Yesterday (inclusive) goes from the end of yesterday when using 'before', but the start of yesterday when using 'after'
            "yesterday" => isBefore ? today.AddTicks(-1).ToString("yyyy-MM-dd HH:mm:ss")
                                    : today.AddDays(-1).ToString("yyyy-MM-dd HH:mm:ss"),
            // Last week always gets 7 days ago, regardless of filter
            "lastweek"  => today.AddDays(-7).ToString("yyyy-MM-dd"),
            // Ensure 24 hours since this moment, not just since this morning. Same endpoint regardless of filter
            "last24h"   => now.AddHours(-24).ToString("yyyy-MM-dd HH:mm:ss"),
            // Shift aliases use the local helper to get the right end of the shift
            "shift1" => ResolveShift(s1Start),
            "shift2" => ResolveShift(s2Start),
            "shift3" => ResolveShift(s3Start),
            _           => alias // If it's not an alias, hopefully it's already a datetime. Return the original string (e.g., 2024-01-01)
        };

        // If it's a valid date but has no time (midnight), roll it to the end of that day to maintain inclusivity
        if (isBefore && !isTimePart && isInclusive && DateTime.TryParse(result, out var parsedDate) && parsedDate.TimeOfDay == TimeSpan.Zero)
        {
            return parsedDate.AddDays(1).AddTicks(-1).ToString("yyyy-MM-dd HH:mm:ss");
        }

        return result;

        // Local helper to get the datetime associated with a shift alias, sensitive to whether it is only to get the time part of the string
        string ResolveShift(TimeSpan start)
        {
            // If instructed to only get the time part, discard the date part and return
            if (isTimePart) return isBefore ? start.Add(shiftDuration).ToString(@"hh\:mm\:ss") : start.ToString(@"hh\:mm\:ss");

            // At this point, we resolve the date part
            // Calculate the occurrence of this shift today
            DateTime shiftTodayStart = DateTime.Today.Add(start);

            // If the shift hasn't started yet today, the user means the one from yesterday
            if (now < shiftTodayStart) shiftTodayStart = shiftTodayStart.AddDays(-1);

            // Otherwise return the full ISO date string 
            DateTime result = isBefore ? shiftTodayStart.Add(shiftDuration) : shiftTodayStart;
            return result.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}