// <copyright file="ILogTableLogic.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace HiokiNL2SQL.Logic;

/// <summary>
/// The data visible to the power search page from the true backend.
/// </summary>
public interface ILogTableLogic
{
    /// <summary>
    /// Gets this table's internal "type" as it would appear in currentType (e.g., "group", "step", "fct").
    /// </summary>
    string TableName { get; }

    /// <summary>
    /// Gets the label to apply to this table in the view (e.g., "Group Results").
    /// </summary>
    string DisplayName { get; }

    // Sorting parameters for URL control

    /// <summary>
    /// Gets or sets the column currently being sorted.
    /// </summary>
    string CurrentSortColumn { get; set; }

    /// <summary>
    /// Gets or sets the direction of the current sort.
    /// </summary>
    string SortDir { get; set; }

    // Shared UI state for the MasterTable

    /// <summary>
    /// Gets or sets the page number shown in the data view.
    /// </summary>
    int CurrentPage { get; set; }

    /// <summary>
    /// Gets or sets the number of results per page.
    /// </summary>
    int PageSize { get; set; }

    /// <summary>
    /// Gets a count of the records retrieved by this query.
    /// </summary>
    int TotalCount { get; }

    /// <summary>
    /// Sets the UI-only indicator used by MasterTable when deciding whether to fade the results (blinking inputs on power-search).
    /// This value is exclusively for rendering purposes and does not influence any query logic or caching.
    /// </summary>
    Func<bool>? UIIsStaleOverride { set; }

    // Actions to be invoked by an implementation of ILogTableLogic to properly update the view

    /// <summary>
    /// Sets the trigger accessible to an implementation of <see cref="ILogTableLogic"/> to prompt the power search page to refresh.
    /// </summary>
    Action? OnNotifyUI { set; }

    /// <summary>
    /// Sets the trigger to jump to the power search page for a barcode "drill-down".
    /// </summary>
    Action<string>? TriggerPowerSearch { set; }

    /// <summary>
    /// Sets the trigger to update the URL to match a power search query.
    /// </summary>
    Action? UpdatePSUrl { set; }

    // The core methods that must be accessible to the power search page

    /// <summary>
    /// Refreshes the view to show updated data.
    /// </summary>
    /// <param name="keepPage">Whether to keep the current page value (or let it reset).</param>
    /// <param name="force">Whether to skip the "hydration check" and guarantee a DB hit.</param>
    /// <returns>A Task representing that the refresh is complete.</returns>
    Task RefreshData(bool keepPage = false, bool force = false);

    /// <summary>
    /// Copies <paramref name="filterDict"/> to the logic's internal filter registry.
    /// </summary>
    /// <param name="filterDict">The filters to insert into the filter registry.</param>
    /// <param name="keepPage">Whether to keep the current page (or let it reset).</param>
    /// <returns>A Task representing that the filters have been copied.</returns>
    Task DictionaryToFilters(Dictionary<string, IFilter> filterDict, bool keepPage = false);

    /// <summary>
    /// Clears all filters and the data view to prepare for a new query.
    /// </summary>
    void ClearData();
}
