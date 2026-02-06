namespace HiokiNL2SQLMark1.Logic;
/// <summary>
/// Container for the value and polarity of a filter
/// </summary>
/// <typeparam name="T">One of string, int, or DateTime</typeparam>
/// <param name="value">The value used in filtering</param>
/// <param name="isNegated">Whether to filter out (or filter by)</param>
public class Filter<T>(T? value, bool isNegated = false)
{
    public T? Value = value;
    public bool IsNegated = isNegated;
}