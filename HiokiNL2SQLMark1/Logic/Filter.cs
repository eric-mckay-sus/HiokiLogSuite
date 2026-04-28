// <copyright file="Filter.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace HiokiNL2SQLMark1.Logic;

/// <summary>
/// Container for the value and polarity of a filter.
/// </summary>
/// <typeparam name="T">One of string, int, or DateTime.</typeparam>
public class Filter<T> : IFilter
{
    /// <summary>
    /// Internal value governing whether to filter by (or filter out).
    /// </summary>
    private bool isNegated;

    /// <summary>
    /// The internal value with the contents of the filter.
    /// </summary>
    private T? value;

    /// <summary>
    /// Initializes a new instance of the <see cref="Filter{T}"/> class using its key, value, and negation status.
    /// Activity status is automatically determined.
    /// </summary>
    /// <param name="key">The name for the new filter.</param>
    /// <param name="value">The value for which to filter.</param>
    /// <param name="isNegated">The negation status of the new filter.</param>
    public Filter(string key, T? value, bool isNegated = false)
    {
        this.Key = key;
        this.IsNegated = isNegated;
        this.Value = value;
    }

    /// <summary>
    /// Gets or sets the name of this filter (for self-identification).
    /// </summary>
    public string Key { get; set; }

    /// <summary>
    /// Gets or sets the action to perform when this filter is updated.
    /// </summary>
    public Action? OnChanged { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this filter is being used in the current query (thus its value should be applied).
    /// Automatically updated on <see cref="Value"/> change.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to filter by (or filter out).
    /// </summary>
    public bool IsNegated
    {
        get => this.isNegated;
        set
        {
            this.isNegated = value;
            this.OnChanged?.Invoke();
        }
    }

    /// <summary>
    /// Gets or sets the filter's contents.
    /// </summary>
    public T? Value
    {
        get => this.value;
        set
        {
            this.value = value;
            this.IsActive = !IsDefault(value);
            this.OnChanged?.Invoke();
        }
    }

    /// <summary>
    /// Gets the value of this filter as a nullable object.
    /// </summary>
    /// <returns>An object representing the generic type used by the value.</returns>
    public object? GetValue() => this.Value;

    /// <summary>
    /// Assigns a new value to this filter. Successfully triggers.
    /// </summary>
    /// <param name="val">The value to assign to this filter.</param>
    public void SetValue(object? val) => this.Value = (T?)val;

    /// <summary>
    /// Copies the state of another filter to this one.
    /// </summary>
    /// <param name="other">The IFilter instance to copy from.</param>
    public void CopyFrom(IFilter other)
    {
        this.IsNegated = other.IsNegated;
        this.Value = (T?)other.GetValue(); // Have to use GetValue because we don't technically know the type of other.Value (working with an IFilter)
    }

    /// <summary>
    /// Sets this filter's value and negation.
    /// </summary>
    public void Reset()
    {
        this.Value = default!;
        this.IsNegated = false;
    }

    /// <summary>
    /// Determine if the user wishes to use this filter.
    /// </summary>
    /// <param name="val">The value to check against default.</param>
    /// <returns>Whether the value is its default (i.e. deactivated, and thus should not be used in a query).</returns>
    private static bool IsDefault(T? val) => val switch
    {
        null => true,
        string s => string.IsNullOrWhiteSpace(s),
        _ => false
    };
}

/// <summary>
/// Interface to bypass the complications of Filter's generic type.
/// </summary>
public interface IFilter
{
    /// <summary>
    /// Gets or sets the name of this filter (for self-identification).
    /// </summary>
    string Key { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to filter by (or filter out).
    /// In other words, the filter's negation status.
    /// </summary>
    bool IsNegated { get; set; }

    /// <summary>
    /// Gets a value indicating whether this filter is being used in the current query (thus its value should be applied).
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Gets the contents of the filter.
    /// </summary>
    /// <returns>A nullable object representing the value held by the filter.</returns>
    object? GetValue();

    /// <summary>
    /// Sets the contents of the filter.
    /// </summary>
    /// <param name="val">The value to assign.</param>
    void SetValue(object? val);

    /// <summary>
    /// Sets the state of this filter to that of the input IFilter instance.
    /// </summary>
    /// <param name="other">The <see cref="IFilter"/> from which to copy the state.</param>
    void CopyFrom(IFilter other);

    /// <summary>
    /// Deactivates this IFilter instance (value=null, isNegated=false).
    /// </summary>
    void Reset();
}
