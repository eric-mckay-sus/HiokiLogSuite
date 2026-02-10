namespace HiokiNL2SQLMark1.Logic;
/// <summary>
/// Container for the value and polarity of a filter
/// </summary>
/// <typeparam name="T">One of string, int, or DateTime</typeparam>
/// <param name="key">The key associated with the filter</param>
/// <param name="value">The value used in filtering</param>
/// <param name="isActive">Whether the filter is active</param>
/// <param name="isNegated">Whether to filter out (or filter by)</param>
public class Filter<T>(string key, T value, bool isActive=false, bool isNegated = false) : IFilter
{
    public bool IsActive { get; set; } = isActive;
    public string Key { get; set; } = key;
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
    public bool IsNegated { get; set; } = isNegated;
    public object? GetValue() => Value;
    private static bool IsDefault(T? val) => val switch
    {
        null => true,
        string s => string.IsNullOrWhiteSpace(s),
        DateTime d => d == DateTime.MinValue,
        int i => i == 0,
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