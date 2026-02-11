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
    public string Key { get; set; }
    public bool IsActive { get; set; }
    public bool IsNegated { get; set; }
    private T? _value;
    public T? Value 
    { 
        get => _value;
        set 
        {
            _value = value;
            IsActive = !IsDefault(value);
        }
    }

    public Filter(string key, T? value, bool isNegated=false)
    {
        Key = key;
        IsNegated = isNegated;
        Value = value;
    }

    public object? GetValue() => Value;
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
    string Key { get; set; }
    bool IsNegated { get; set; }
    object? GetValue();
}