// <copyright file="DateParsingService.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace HiokiNL2SQL.Services;

using System.Globalization;
using System.Text.RegularExpressions;

using HiokiNL2SQL.Logic;

/// <summary>
/// Service responsible for parsing datetimes (ranging from pure ISO datestrings to alias-only, and anything in between).
/// </summary>
public static class DateParsingService
{
    /// <summary>
    /// Gets the dictionary that associates each shift with its start time and duration.
    /// </summary>
    public static Dictionary<string, (TimeSpan Start, double Hours)> ShiftDetails { get; } = new ()
    {
        {
            "shift1", (new TimeSpan(7, 0, 0), 8.5)
        },
        {
            "shift2", (new TimeSpan(15, 30, 0), 7.0)
        },
        {
            "shift3", (new TimeSpan(-1, -30, 0), 8.5)
        },
    };

    /// <summary>
    /// Swaps the "before" and "after" filters if "before" occurs before "after".
    /// </summary>
    /// <param name="result">The parse result containing the filters.</param>
    public static void SwapDatesIfInverted(SearchParseResult result)
    {
        // Safely retrieve and cast the filters in a single guard clause
        if (!result.Filters.TryGetValue("before", out IFilter? beforeFilter) ||
            !result.Filters.TryGetValue("after", out IFilter? afterFilter) ||
            beforeFilter is not Filter<DateTime?> b ||
            afterFilter is not Filter<DateTime?> a)
        {
            return;
        }

        // Check if both have values and are inverted
        if (a.Value.HasValue && b.Value.HasValue && a.Value > b.Value)
        {
            // Swap the values inside the filter objects in the dictionary
            (b.Value, a.Value) = (a.Value, b.Value);

            result.ErrorMessages.Add($"Your start date is after your end date. This search is now **after:{a.Value:yyyy-MM-dd} before:{b.Value:yyyy-MM-dd}...**");
        }
    }

    /// <summary>
    /// Used by ParseQuery to separate and translate a datetime
    /// Splits and rebuilds string to handle combined date and time aliases like "today shift1".
    /// </summary>
    /// <param name="key">The key for which to get the datetime boundary (should be 'before' or 'after').</param>
    /// <param name="value">The value for the key, with optional aliases.</param>
    /// <param name="isInclusive">Whether the boundary datetime should be inclusive of the value provided.</param>
    /// <returns>The full ISO date string representing the boundary for the date filter.</returns>
    public static DateTime? ProcessDateValue(string key, string value, bool isInclusive = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        bool isBefore = key.Equals("before", StringComparison.OrdinalIgnoreCase);

        // Detect specific time to deactivate date-only inclusivity check (excluding time shouldn't skip entire day)
        bool hasSpecificTime = Regex.IsMatch(value, @"\d{1,2}:\d{2}", RegexOptions.IgnoreCase);
        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        DateTime baseDateTime;
        double offsetHours;

        if (parts.Length > 1)
        {
            (baseDateTime, offsetHours) = ParseCombinedAlias(parts);
        }
        else
        {
            (baseDateTime, offsetHours) = ParseSingleAlias(value, hasSpecificTime, isBefore);
        }

        // Verify that there actually was a date
        if (baseDateTime.Equals(DateTime.MinValue))
        {
            return DateTime.MinValue;
        }

        // Inclusivity logic (done inline to avoid excess parameter passing)
        // Terminology: unit refers to day or shift, whichever is relevant and smaller
        if (isBefore)
        {
            // Rule 1: isBefore && isInclusive (inclusive end point) -> 1 tick before start of next unit
            // Rule 2: isBefore && !isInclusive (exclusive end point) -> 1 tick before start of target unit
            if (!hasSpecificTime)
            { // Don't steal a tick when no alias provided
                if (isInclusive)
                {
                    baseDateTime = baseDateTime.AddHours(offsetHours);
                }

                baseDateTime = baseDateTime.AddTicks(-1);
            }
        }
        else
        {
            // Rule 3: !isBefore && isInclusive (inclusive start point) -> Start of target unit (Default)
            // Rule 4: !isBefore && !isInclusive (exclusive start point) -> Start of next unit
            if (!isInclusive && !hasSpecificTime)
            {
                baseDateTime = baseDateTime.AddHours(offsetHours);
            }
        }

        return baseDateTime;
    }

    /// <summary>
    /// Parses a datetime containing multiple parts.
    /// Example: "today shift1" or "2026-02-20 08:30" (or any similar combination of date and time, alias or not).
    /// </summary>
    /// <param name="parts">The array of strings representing the complete datetime.</param>
    /// <returns>The base datetime, and computed offset hours to be applied when inclusivity is considered.</returns>
    private static (DateTime dateTime, double offsetHours) ParseCombinedAlias(string[] parts)
    {
        double offsetHours = 24;

        foreach (string part in parts)
        {
            // If there's a shift number offset in either part, grab its offset
            if (ShiftDetails.TryGetValue(part.ToLower(), out (TimeSpan Start, double Hours) detail))
            {
                offsetHours = detail.Hours;
                break;
            }
        }

        DateTime datePart = BaseDateTimeFromAlias(parts[0]);
        DateTime timePart = BaseDateTimeFromAlias(parts[1]);

        // If either was unexpected, return immediately
        if (datePart.Equals(DateTime.MinValue) || timePart.Equals(DateTime.MinValue))
        {
            return (DateTime.MinValue, offsetHours);
        }

        // Shift 3's negative offset was overwritten by the date part, so reapply it.
        if (parts[1].Equals("shift3", StringComparison.OrdinalIgnoreCase))
        {
            datePart = datePart.AddDays(-1);
        }

        // First part always provides the date, second part always provides the time
        DateTime baseDateTime = datePart.Date.Add(timePart.TimeOfDay);
        return (baseDateTime, offsetHours);
    }

    /// <summary>
    /// Parses a datetime containing only one part (e.g., "07:43", "yesterday", or "2026-07-04").
    /// </summary>
    /// <param name="value">The string representing the datetime.</param>
    /// <param name="hasSpecificTime">Whether <paramref name="value"/> is a literal time (i.e. not alias).</param>
    /// <param name="isBefore">Whether this date is for the "before" tag.</param>
    /// <returns>The base datetime, and computed offset hours to be applied when inclusivity is considered.</returns>
    private static (DateTime DateTime, double OffsetHours) ParseSingleAlias(string value, bool hasSpecificTime, bool isBefore)
    {
        double offsetHours = 24;
        if (ShiftDetails.TryGetValue(value.ToLower(), out (TimeSpan Start, double Hours) detail))
        {
            offsetHours = detail.Hours;
        }

        DateTime baseDateTime = BaseDateTimeFromAlias(value);

        // If an after tag targets a future date, the search cannot possibly have results
        // For shift-only, this is just an expansion of auto-detection (we'll handle user-specified out-of-range dates later)
        if ((baseDateTime > DateTime.Now) && ((!isBefore && offsetHours < 24) || hasSpecificTime))
        {
            baseDateTime = baseDateTime.AddDays(-1);
        }

        return (baseDateTime, offsetHours);
    }

    /// <summary>
    /// Generates a datetime boundary for aliases like "today", "last24h", and "shift2".
    /// </summary>
    /// <param name="alias">The alias for which to get the bounding datetime.</param>
    /// <returns>A datetime representing the starting boundary for this alias.</returns>
    private static DateTime BaseDateTimeFromAlias(string alias)
    {
        DateTime now = DateTime.Now;
        DateTime today = now.Date; // Could use DateTime.Today, but if this parser were set up to run automatically at midnight, that would become unstable
        string lowerAlias = alias.ToLower();

        return lowerAlias switch
        {
            "today" => today,
            "yesterday" => today.AddDays(-1),
            "lastweek" => today.AddDays(-7),
            "last24h" => now.AddHours(-24), // Ensure 24 hours since this moment, not just since this morning.

            // Shift aliases use the local helper to get the right end of the shift
            "shift1" => GetShiftStartOnDay(ShiftDetails["shift1"].Start),
            "shift2" => GetShiftStartOnDay(ShiftDetails["shift2"].Start),
            "shift3" => GetShiftStartOnDay(ShiftDetails["shift3"].Start),
            _ => DateTime.TryParse(alias, CultureInfo.CurrentCulture, out DateTime p) ? p : DateTime.MinValue // If it's not an alias, hopefully it's already a datetime, but default to min value
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
