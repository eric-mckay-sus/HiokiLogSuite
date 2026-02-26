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
    public static readonly string beforePattern = @"before\s?:\s?(""[^""]*""|(?:(?!\s?-?\w+:)\S)+)";
    public static readonly string afterPattern = @"after\s?:\s?(""[^""]*""|(?:(?!\s?-?\w+:)\S)+)";
    public static readonly string[] availableTypes = ["all", "group", "step", "fct"]; // all available tables
    public static readonly Dictionary<string, (TimeSpan Start, double Hours)> shiftDetails = new()
    {
        {"shift1", (new TimeSpan(7,0,0), 8.5)}, {"shift2", (new TimeSpan(15,30,0), 7.0)}, {"shift3", (new TimeSpan(-1,-30,0), 8.5)}
    };

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
        { "comp", "short", "open", "macro", "ic", "function" }; // tags available to group table only
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

    // Maps each tag type to the ValType representing its accepted datatype
    private static readonly Dictionary<string, ValType> TagTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "barcode", ValType.String },
        { "group", ValType.Int },
        { "after", ValType.DateTime },
        { "before", ValType.DateTime },
        { "result", ValType.String },
        { "comp", ValType.String },
        { "short", ValType.String },
        { "open", ValType.String },
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
        { "open", "Filter by open-circuit test results" },
        { "macro", "Search by macro-test results" },
        { "ic", "Filter by integrated circuit (IC) test results" },
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
    /// A DTO for the return values from the parser
    /// </summary>
    public class SearchParseResult
    {
        public Dictionary<string, IFilter> Filters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ErrorMessages { get; set; } = [];
        public string CurrentType { get; set; } = "all";
        public string Preview { get; set; } = "Searching all records...";
        public bool HasDateFilter { get; set; } = false;
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
            string polarity = contextMatch.Groups[1].Value;
            string targetType = contextMatch.Groups[2].Value.ToLower();
            if (matches.Count > 1) result.ErrorMessages.Add($"Duplicate **in** tag. This search is now **in:{targetType}...**. All previous uses of this key are *ignored*.");

            if (polarity.Equals("-")) result.ErrorMessages.Add($"The **in** tag cannot be negated. This search is now **in : {targetType}...**.");

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
            string? message = MissingKeyOrValueMessage(gap);
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
                if (expectedType == ValType.DateTime) result.HasDateFilter = true;
                if (!IsValidValue(expectedType, cleanKey, value, out string errorMessage)) {
                    result.ErrorMessages.Add($"Invalid value for the **{key}** tag. {errorMessage}");
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
                result.HasDateFilter = true;
            }
            // We didn't actually remove "in", we just ignored it
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
            string? message = MissingKeyOrValueMessage(trailing);
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
    private static string? MissingKeyOrValueMessage(string toCheck)
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
                DateTime? normalized = ProcessDateValue(key, value); // disregard inclusivity for this check
                if (!normalized.HasValue) {
                    error = $"Date (read as **{value}**) cannot be empty.";
                    return false;
                }
                if (normalized.Equals(DateTime.MinValue))
                {
                    error = $"**{value}** (read as **{normalized}**) is not a valid date or alias. Please use \"YYYY-MM-DD HH:mm:ss\" (ISO formatting) or a shortcut below.";
                    return false;
                }
                if (key.Equals("after", StringComparison.OrdinalIgnoreCase) && normalized > DateTime.Now)
                {
                    error = $"**{value}** is in the future. Did you mean to search before that date?";
                    return false;
                }
                break;
        }
        // The input value is already a string, so there's no check that case (typos not a part of this check)
        return true;
    }

    /// <summary>
    /// Creates a filter for a key-value pair with its polarity
    /// </summary>
    /// <param name="key">The filter's name</param>
    /// <param name="value">The filter's value</param>
    /// <param name="isNegated">The filter's polarity (true when negated)</param>
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

            ValType.DateTime => new Filter<DateTime?>(key, ProcessDateValue(key, value, isInclusive), false), // Negation not allowed for dates

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
    public static string GeneratePreview(string type, Dictionary<string, IFilter> filters)
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
    public static DateTime? ProcessDateValue(string key, string value, bool isInclusive=true)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        bool isBefore = key.Equals("before", StringComparison.OrdinalIgnoreCase);

        // Detect specific time to deactivate date-only inclusivity check (excluding time shouldn't skip entire day)
        bool hasSpecificTime = Regex.IsMatch(value, @"\d{1,2}:\d{2}", RegexOptions.IgnoreCase);
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        DateTime baseDateTime;
        double offsetHours = 24; // default to day offset, used when no time (alias or literal) is provided

        if (parts.Length > 1) { // If we have a date and time, translate parts separately and re-combine
            // Example: "today shift1" or "2026-02-20 08:30" (or any similar combination of date & time, alias or not)

            // If there's a shift number offset in either part, prioritize it
            foreach (var part in parts)
            {
                if (shiftDetails.TryGetValue(part.ToLower(), out var detail))
                {
                    offsetHours = detail.Hours;
                    break; 
                }
            }

            DateTime datePart = BaseDateTimeFromAlias(parts[0]);
            DateTime timePart = BaseDateTimeFromAlias(parts[1]);

            // If either was unexpected, return immediately
            if (datePart.Equals(DateTime.MinValue) || timePart.Equals(DateTime.MinValue)) return DateTime.MinValue;

            // Shift 3's negative offset was overwritten by the date part, so reapply it.
            if (parts[1].Equals("shift3", StringComparison.OrdinalIgnoreCase)) datePart = datePart.AddDays(-1);

            // First part always provides the date, second part always provides the time
            baseDateTime = datePart.Date.Add(timePart.TimeOfDay);
        }
        else // Otherwise, just let BaseDateTime handle it
        {
            if (shiftDetails.TryGetValue(value.ToLower(), out var detail)) offsetHours = detail.Hours;
            baseDateTime = BaseDateTimeFromAlias(value);

            // If an after tag targets a future date, the search cannot possibly have results
            // For shift-only, this is just an expansion of auto-detection (we'll handle user-specified out-of-range dates later)
            if ((baseDateTime > DateTime.Now) && !isBefore && offsetHours != 24) baseDateTime = baseDateTime.AddDays(-1);
        }

        // Verify that there actually was a date
        if (!baseDateTime.Equals(DateTime.MinValue))
        {
            // Inclusivity logic
            // Terminology: unit refers to day or shift, whichever is relevant and smaller
            if (isBefore)
            {
                // Rule 1: isBefore && isInclusive (inclusive end point) -> 1 tick before start of next unit
                // Rule 2: isBefore && !isInclusive (exclusive end point) -> 1 tick before start of target unit
                if (!hasSpecificTime){ // Don't steal a tick when no alias provided
                    if (isInclusive) baseDateTime = baseDateTime.AddHours(offsetHours);
                    baseDateTime = baseDateTime.AddTicks(-1);
                }
            }
            else
            {
                // Rule 3: !isBefore && isInclusive (inclusive start point) -> Start of target unit (Default)
                // Rule 4: !isBefore && !isInclusive (exclusive start point) -> Start of next unit
                if (!isInclusive && !hasSpecificTime) baseDateTime = baseDateTime.AddHours(offsetHours);
            }
        }
        return baseDateTime;
    }

    /// <summary>
    /// Generates a datetime boundary for aliases like "today", "last24h", and "shift2"
    /// </summary>
    /// <param name="alias">The alias for which to get the bounding datetime</param>
    /// <returns>A datetime representing the starting boundary for this alias</returns>
    private static DateTime BaseDateTimeFromAlias(string alias)
    {
        DateTime now = DateTime.Now;
        DateTime today = now.Date; // Could use DateTime.Today, but if this parser were set up to run automatically at midnight, that would become unstable
        string lowerAlias = alias.ToLower();

        return lowerAlias switch
        {
            "today"     => today,
            "yesterday" => today.AddDays(-1),
            "lastweek"  => today.AddDays(-7),
            // Ensure 24 hours since this moment, not just since this morning.
            "last24h"   => now.AddHours(-24),
            // Shift aliases use the local helper to get the right end of the shift
            "shift1"    => GetShiftStartOnDay(shiftDetails["shift1"].Start),
            "shift2"    => GetShiftStartOnDay(shiftDetails["shift2"].Start),
            "shift3"    => GetShiftStartOnDay(shiftDetails["shift3"].Start),
            _           => DateTime.TryParse(alias, out var p) ? p : DateTime.MinValue // If it's not an alias, hopefully it's already a datetime, but default to min value
        };

        // If the shift hasn't started yet today, it refers to yesterday's instance
        // This is safe to do here because when the user provides a date and shift, the date part from the shift is discarded, so this date has no effect (just the time)
        DateTime GetShiftStartOnDay(TimeSpan start)
        {
            DateTime shiftTodayStart = DateTime.Today.Add(start);
            return now < shiftTodayStart ? shiftTodayStart.AddDays(-1) : shiftTodayStart;
        }
    }
}