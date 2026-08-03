// <copyright file="SearchValidationService.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace HiokiNL2SQL.Services;

using static DateParsingService;
using static SearchParserService;

/// <summary>
/// Enumerates the datatypes allowed for a tag.
/// </summary>
 public enum ValType
 {
    /// <summary>
    /// Strings are valid tag values
    /// </summary>
    String,

    /// <summary>
    /// Integers are valid tag values
    /// </summary>
    Int,

    /// <summary>
    /// DateTimes are valid tag values
    /// </summary>
    DateTime,
}

/// <summary>
/// Service responsible for generating search previews.
/// </summary>
public static class SearchValidationService
{
    /// <summary>
    /// Gets the list of disallowed query terms which suggest an attempted SQL injection.
    /// </summary>
    public static HashSet<string> SqlBlacklist { get; } = new (StringComparer.OrdinalIgnoreCase)
    {
        "DROP", "DELETE", "UPDATE", "INSERT", "TRUNCATE",
        "EXEC", "EXECUTE", "ALTER", "CREATE", "GRANT", "REVOKE",
    };

    /// <summary>
    /// Gets the dictionary mapping each tag type to the ValType representing its accepted datatype (simulated reflection).
    /// </summary>
    public static Dictionary<string, ValType> TagTypeMap { get; } = new (StringComparer.OrdinalIgnoreCase)
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
        { "part", ValType.String },
    };

    /// <summary>
    /// Verifies that a value matches a certain type.
    /// </summary>
    /// <param name="type">A ValType (enum) representing the required type.</param>
    /// <param name="key">The tag-identifying string.</param>
    /// <param name="value">The value for which to check the type.</param>
    /// <param name="error">The error message (in case of failure).</param>
    /// <returns>Whether the value matches the type specified.</returns>
    public static bool IsValidValue(ValType type, string key, string value, out string error)
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
                if (!normalized.HasValue)
                {
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
    /// Generates an error message depending on whether the string is a keyless value or value without a key.
    /// </summary>
    /// <param name="toCheck">The string for which to generate the error.</param>
    /// <returns>An error message describing the missing key/value.</returns>
    public static string? MissingKeyOrValueMessage(string toCheck)
    {
        if (!string.IsNullOrWhiteSpace(toCheck))
        {
            // Check if it's a key without a value
            if (toCheck.EndsWith(':'))
            {
                return $"Tag **{toCheck}** is missing a value. This search excludes **{toCheck}**.";
            }

            // or a value without key
            else
            {
                return $"Unrecognized filter without key: **{toCheck}**. This search excludes **{toCheck}**.";
            }
        }

        return null;
    }

    /// <summary>
    /// Verifies that <paramref name="cleanKey"/> exists and is supported by the target table.
    /// </summary>
    /// <param name="cleanKey">The sanitized key, as it would appear in the set of accepted keys.</param>
    /// <param name="key">The literal key value supplied by the user.</param>
    /// <param name="result">The <see cref="SearchParseResult"/> object containing all relevant information about the search.</param>
    /// <returns>A value indicating whether the key was accepted.</returns>
    public static bool ValidateMatchKey(string cleanKey, string key, SearchParseResult result)
    {
        // Validate if key is supported by system
        if (!AllTags.Contains(cleanKey))
        {
            result.ErrorMessages.Add($"The tag **{key}** wasn't recognized. Try using the table and key options below the search bar.");
            return false;
        }

        // Validate if key is supported by the selected table
        HashSet<string> allowedKeys = new (GetSupportedKeysThisMode(result.CurrentType), StringComparer.OrdinalIgnoreCase);
        if (!allowedKeys.Contains(cleanKey))
        {
            result.ErrorMessages.Add($"The tag **{key}:** is not available when searching **{result.CurrentType}**. Try a different tag or search a table with that attribute.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Verifies that <paramref name="cleanKey"/> has not already appeared in the search query.
    /// If it has, overwrites the old value of this tag with the new <paramref name="value"/>.
    /// </summary>
    /// <param name="cleanKey">The sanitized key, as it would appear in the set of accepted keys.</param>
    /// <param name="key">The literal key value supplied by the user.</param>
    /// <param name="value">The value of the current tag (with which to overwrite).</param>
    /// <param name="result">The <see cref="SearchParseResult"/> object containing all relevant information about the search.</param>
    /// <returns>A value indicating whether <paramref name="key"/> is a duplicate.</returns>
    public static bool CheckDuplicateKey(string cleanKey, string key, string value, SearchParseResult result)
    {
        if (result.Filters.ContainsKey(cleanKey))
        {
            result.ErrorMessages.Add($"Duplicate tag detected: **{key}:**. This search is now '**{key}:{value}...**. The previous use of this key is *ignored*.");
            return true; // indicates it's a duplicate (but we still process it)
        }

        return false;
    }

    /// <summary>
    /// Verifies that there are no gaps between the last and current tags (i.e. the regex skipped non-tag conforming text).
    /// </summary>
    /// <param name="rawInput">The complete search query.</param>
    /// <param name="lastIndex">The index of <paramref name="rawInput"/> where the last tag ended.</param>
    /// <param name="currentIndex">The index of <paramref name="rawInput"/> where the current tag begins.</param>
    /// <param name="result">The <see cref="SearchParseResult"/> object containing relevant search information.</param>
    public static void CheckForGaps(string rawInput, int lastIndex, int currentIndex, SearchParseResult result)
    {
        string gap = rawInput[lastIndex..currentIndex].Trim();
        string? message = MissingKeyOrValueMessage(gap);
        if (!string.IsNullOrEmpty(message))
        {
            result.ErrorMessages.Add(message);
        }
    }
}
