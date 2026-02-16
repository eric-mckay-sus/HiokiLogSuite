namespace HiokiNL2SQLMark1.Logic;
/// <summary>
/// Container for the value and polarity of a filter
/// </summary>
/// <typeparam name="T">One of string, int, or DateTime</typeparam>
/// <param name="key">The key associated with the filter</param>
/// <param name="value">The value used in filtering</param>
/// <param name="isActive">Whether the filter is active</param>
/// <param name="isNegated">Whether to filter out (or filter by)</param>
public class Filter<T> : IFilter
{
    public string Key { get; set; } // The name of this filter (for self-identification)
    public bool IsActive { get; set; } // Whether this filter is being used in the current query (thus its value should be used). Automatically updated on value change
    public bool IsNegated { get; set; } // Whether to filter by (or filter out)
    private T? _value; // The internal value held by the filter
    public T? Value // The methods of accessing and modifying the filter's value
    { 
        get => _value;
        set 
        {
            _value = value;
            IsActive = !IsDefault(value);
        }
    }

    /// <summary>
    /// Builds a filter using its key, value, and negation status (activity status is automatically determined)
    /// </summary>
    /// <param name="key">The name for the new filter</param>
    /// <param name="value">The value for which to filter</param>
    /// <param name="isNegated">The negation status of the new filter</param>
    public Filter(string key, T? value, bool isNegated=false)
    {
        Key = key;
        IsNegated = isNegated;
        Value = value;
    }

    // Return an object representing the generic type used by the value
    public object? GetValue() => Value;

    /// <summary>
    /// Determine if the user wishes to use this filter
    /// </summary>
    /// <param name="val">The value to check against default</param>
    /// <returns>Whether the value is its default (i.e. deactivated, and thus should not be used in a query)</returns>
    private static bool IsDefault(T? val) => val switch
    {
        null => true,
        string s => string.IsNullOrWhiteSpace(s),
        _ => false
    };
}

/// <summary>
/// Interface to bypass the complications of Filter's generic type
/// </summary>
public interface IFilter
{
    string Key { get; set; } // The filter's name
    bool IsNegated { get; set; } // The filter's negation status
    object? GetValue(); // A method to get the value associated with this filter, as a nullable object
}