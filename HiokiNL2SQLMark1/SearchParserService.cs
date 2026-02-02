using System.Text.RegularExpressions;

/// <summary>
/// A service to contain state and methods relevant for parsing. Required to be injected into PowerSearch.razor
/// </summary>
public class SearchParserService
{
    // Regex to find key:value pairs. \w+ detects the key text, \s* surrounding the colon detects whitespace, : is the literal colon,
    // " is a literal quote, [^"]* iterates over non-quote characters, " is again a literal quote, | is for OR, and \S+ detects the value text
    // Verbatim strings (those starting with @) switch the normal escape character of backslash (\) for quote ("), which is why it appears twice
    // Parentheses and brackets are for grouping the regex itself. Regex OR is a short-circuiting operation
    // THIS REGEX WILL BREAK IF THE QUOTE IS REQUIRED AS A LITERAL VALUE IN THE SEARCH
    readonly string tagPattern = @"(\w+)\s*:\s*(""[^""]*""|\S+)";
    public string inPattern = @"in\s*:\s*(\w+)"; // represents the key-value pair for the "in" tag
    public static readonly string[] availableTypes = ["all", "group", "step", "fct"]; // all available tables

    // Sets of which tags are available to which tables. StepFctTags and AllTags inherit the contents of the lower sets
    readonly static HashSet<string> UniversalTags = new(StringComparer.OrdinalIgnoreCase) 
        { "in", "barcode", "group", "before", "after", "result" }; // tags available to all tables
    readonly static HashSet<string> GroupTags = new(StringComparer.OrdinalIgnoreCase) 
        { "comp", "short", "macro", "ic", "function" }; // tags available to group table only
    readonly static HashSet<string> StepFctTags = new(StringComparer.OrdinalIgnoreCase) 
        { "step", "mode" };
    readonly static HashSet<string> StepTags = CreateStepSet(); // tags available to step table only
    static HashSet<string> CreateStepSet() => new(StepFctTags, StringComparer.OrdinalIgnoreCase){"part"};
    readonly static HashSet<string> FctTags = StepFctTags; // tags available to FCT table only (alias)
    public readonly static HashSet<string> AllTags = CombineAllTags(); // all tags available to the system
    private static HashSet<string> CombineAllTags()
    {
        HashSet<string> all = new(UniversalTags, StringComparer.OrdinalIgnoreCase);
        all.UnionWith(GroupTags);
        all.UnionWith(StepTags);
        return all;
    }

    // Basic injection countermeasure
    private readonly static HashSet<string> sqlBlacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "DROP", "DELETE", "UPDATE", "INSERT", "TRUNCATE", 
        "EXEC", "EXECUTE", "ALTER", "CREATE", "GRANT", "REVOKE"
    };

    /// <summary>
    /// A container for the return values from the parser
    /// </summary>
    public class SearchParseResult
    {
        public Dictionary<string, string> Filters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ErrorMessages { get; set; } = [];
        public string CurrentType { get; set; } = "all";
    }

    /// <summary>
    /// Parses a string for key-value pairs. Upon finding the "in" key, immediately updates the table reference
    /// Catches date aliases and calls TranslateDateAlias() to handle them
    /// Continues upon finding an error in order to find and inform the user of all of them
    /// </summary>
    /// <param name="rawInput">The string to parse</param>
    /// <param name="currentType">The current table to check</param>
    /// <returns>A dictionary containing the key-value pairs to filter with</returns>
    public SearchParseResult ParseQuery(string rawInput, string currentType)
    {
        var result = new SearchParseResult();
        result.ErrorMessages.Clear(); // on the off chance ParseQuery is called outside ExecutePowerSearch
        result.Filters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        if (string.IsNullOrWhiteSpace(rawInput)) return result;

        // In the first pass, look for the "in" keyword to ensure further filters are applicable
        Match? contextMatch = Regex.Match(rawInput, inPattern, RegexOptions.IgnoreCase);
        if (contextMatch.Success)
        {
            string targetType = contextMatch.Groups[1].Value.ToLower();
            if (availableTypes.Contains(targetType))
            {
                currentType = targetType;
            }
            else
            {
                result.ErrorMessages.Add($"'{targetType}' is not a valid table. The 'in' keyword only accepts the values 'all', 'group', 'step', or 'fct'.");
            }
        }

        // Now the type is certain, we can enumerate all the valid keys
        HashSet<string> allowedKeys = new(GetSupportedKeysThisMode(result.CurrentType), StringComparer.OrdinalIgnoreCase);
        var matches = Regex.Matches(rawInput, tagPattern);
        int lastIndex = 0; // keep track of the location of the last match to determine if there is a break (invalid tags)

        foreach (Match match in matches)
        {
            // Store the substring of unparsed input
            string gap = rawInput[lastIndex..match.Index].Trim();
            if (!string.IsNullOrWhiteSpace(gap))
            {
                result.ErrorMessages.Add($"Unrecognized input: '{gap}'. Did you forget a tag?");
            }

            string? key = match.Groups[1].Value.ToLower();
            string? value = match.Groups[2].Value.Trim('"'); // cut the quotes, if the regex found them

            // Basic SQL injection countermeasure
            if (sqlBlacklist.Any(forbidden => value.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
            {
                result.ErrorMessages.Add($"Security Issue: The value for '{key}' contains forbidden keywords.");
            }

            // Validate if key is supported by system
            if (!AllTags.Contains(key))
            {
                result.ErrorMessages.Add($"The tag '{key}' wasn't recognized. Try using the table and key options below the search bar.");
            } 
            // Validate if key is supported by the selected table
            else if (!allowedKeys.Contains(key) && key != "in")
            {
                result.ErrorMessages.Add($"The tag '{key}:' is not available when searching '{currentType}'. Either search a different table or remove this tag");
            }
            // Validate if filter was already used in this search. If it was, proceed, but notify the user
            else if (result.Filters.ContainsKey(key))
            {
                result.ErrorMessages.Add($"Duplicate tag detected: '{key}:'. Only the last value will be used.");
                result.Filters[key] = (key == "before" || key == "after") ? TranslateDateAlias(value) : value;
            }
            // If there weren't any errors, add the tag to the dictionary, looking up the alias if applicable
            else if (key != "in") // we didn't actually remove "in", we just ignored it
            {
                result.Filters[key] = (key == "before" || key == "after") ? TranslateDateAlias(value) : value;
            }
            lastIndex = match.Index + match.Length;
        }

        // Check if there is any unparsed input (that didn't match the pattern)
        if (lastIndex < rawInput.Length)
        {
            string trailing = rawInput[lastIndex..].Trim();
            if (!string.IsNullOrWhiteSpace(trailing))
            {
                // Check if it's a "tag:" without a value
                if (trailing.EndsWith(":"))
                    result.ErrorMessages.Add($"Tag '{trailing}' is missing a value.");
                else
                    result.ErrorMessages.Add($"Unrecognized filter without key: '{trailing}'.");
            }
        }
        return result;
    }

    /// <summary>
    /// Gets the list of all supported tags for the current mode
    /// This list does not detect when the "in" key is used
    /// </summary>
    /// <param name="mode">The mode to check keys for</param>
    /// <returns>The tags applicable to the current table</returns>
    public IEnumerable<string> GetSupportedKeysThisMode(string mode)
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
    public string TranslateDateAlias(string alias)
    {
        DateTime now = DateTime.Today;

        return alias.ToLower() switch
        {
            "today"     => now.ToString("yyyy-MM-dd"),
            "yesterday" => now.AddDays(-1).ToString("yyyy-MM-dd"),
            "lastweek"  => now.AddDays(-7).ToString("yyyy-MM-dd"),
            "last24h"   => now.AddHours(-24).ToString("yyyy-MM-dd HH:mm:ss"),
            "shift1"    => DateTime.Today.AddHours(7).ToString("yyyy-MM-dd HH:mm:ss"), // adjust as needed, this is approximate
            "shift2"    => DateTime.Today.AddHours(15).ToString("yyyy-MM-dd HH:mm:ss"),
            "shift3"    => DateTime.Today.AddHours(23).ToString("yyyy-MM-dd HH:mm:ss"),
            _           => alias // If it's not an alias, hopefully it's already a datetime. Return the original string (e.g., 2024-01-01)
        };
    }
}