// <copyright file="LogTableBase.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace HiokiNL2SQL;

using Microsoft.AspNetCore.Components;

using HiokiNL2SQL.Logic;
using Parser = Services.SearchParserService;

/// <summary>
/// This abstract class forms the interface between a page and LogTableLogic and holds all shared information between VQ pages.
/// </summary>
/// <typeparam name="T">An implementation of IHiokiLog (defined in LogDbContext).</typeparam>
public abstract class LogTableBase<T> : ComponentBase
    where T : class, IHiokiLog
{
    /// <summary>
    /// Gets or sets the date part of the start datetime.
    /// </summary>
    public DateTime? StartDatePart { get; set; }

    /// <summary>
    /// Gets or sets the time part of the start datetime.
    /// </summary>
    public string? StartTimePart { get; set; }

    /// <summary>
    /// Gets or sets the date part of the end datetime.
    /// </summary>
    public DateTime? EndDatePart { get; set; }

    /// <summary>
    /// Gets or sets the time part of the end datetime.
    /// </summary>
    public string? EndTimePart { get; set; }

    /// <summary>
    /// Gets a value indicating whether date filter mode is inclusive (i.e. date filters should include the target).
    /// </summary>
    public bool IsInclusive { get; private set; } = true;

    /// <summary>
    /// Gets the barcode filter from the registry.
    /// </summary>
    protected Filter<string?> FilterBarcode => this.Logic.GetFilter<string?>("barcode");

    /// <summary>
    /// Gets the start date filter from the registry.
    /// </summary>
    protected Filter<DateTime?> FilterStartDate => this.Logic.GetFilter<DateTime?>("after");

    /// <summary>
    /// Gets the end date filter from the registry.
    /// </summary>
    protected Filter<DateTime?> FilterEndDate => this.Logic.GetFilter<DateTime?>("before");

    /// <summary>
    /// Gets the result type filter from the registry.
    /// </summary>
    protected Filter<string?> FilterResult => this.Logic.GetFilter<string?>("result");

    /// <summary>
    /// Gets the group number filter from the registry.
    /// </summary>
    protected Filter<int?> FilterGroup => this.Logic.GetFilter<int?>("group");

    /// <summary>
    /// Gets or sets the particular instance of LogTableLogic used to operate this page.
    /// </summary>
    protected LogTableLogic<T> Logic { get; set; } = default!;

    /// <summary>
    /// Gets the human-readable query preview.
    /// </summary>
    protected string Preview { get; private set; } = string.Empty;

    /// <summary>
    /// Calls Logic.RefreshData to update table, then tells the child the state has changed.
    /// </summary>
    /// <param name="keepPage">Whether to keep the current page.</param>
    /// <param name="withScroll">Whether to scroll to the table after refresh (bind to Execute button).</param>
    /// <param name="force">Whether to override the hydration check and guarantee a DB hit.</param>
    /// <returns>A Task representing that the data has been updated.</returns>
    public virtual async Task RefreshData(bool keepPage = false, bool withScroll = false, bool force = false)
    {
        await this.Logic.RefreshData(keepPage, force);
        this.Preview = Parser.GeneratePreview(this.Logic.TableName, this.Logic.Filters.Where(kvp => kvp.Value.GetValue() != null).ToDictionary()).Replace("Searching", "Showing");
        this.StateHasChanged();
        if (withScroll)
        {
            await Task.Delay(100);
            await this.Logic.JS.ScrollToElement("table-results-area");
        }
    }

    /// <summary>
    /// Updates the start date filter ('after' tag) from its constituent parts.
    /// Call whenever the start date or time are updated.
    /// </summary>
    public void UpdateStart()
    {
        string datePart = this.StartDatePart?.ToString("yyyy-MM-dd") ?? string.Empty;

        // Combine parts: date only, time only, or both
        string combined = $"{datePart} {this.StartTimePart}".Trim();

        this.FilterStartDate.Value = Parser.ProcessDateValue("after", combined, this.IsInclusive);
    }

    /// <summary>
    /// Updates the end date filter ('before' tag) from its constituent parts.
    /// Call whenever the end date or time are updated.
    /// </summary>
    public void UpdateEnd()
    {
        string datePart = this.EndDatePart?.ToString("yyyy-MM-dd") ?? string.Empty;

        string combined = $"{datePart} {this.EndTimePart}".Trim();

        this.FilterEndDate.Value = Parser.ProcessDateValue("before", combined, this.IsInclusive);
    }

    /// <summary>
    /// Re-process date filters when inclusivity changes.
    /// </summary>
    /// <param name="toSet">New value of inclusivity.</param>
    public void SetInclusivity(bool toSet)
    {
        this.IsInclusive = toSet;

        if (this.StartDatePart != null || !string.IsNullOrEmpty(this.StartTimePart))
        {
            this.UpdateStart();
        }

        if (this.EndDatePart != null || !string.IsNullOrEmpty(this.EndTimePart))
        {
            this.UpdateEnd();
        }
    }

    /// <summary>
    /// Clears all filters on a query, plus internal fields backing date filters.
    /// </summary>
    /// <returns>A Task representing that the filters have been cleared.</returns>
    public virtual async Task ClearFilters()
    {
        this.StartDatePart = this.EndDatePart = null;
        this.StartTimePart = this.EndTimePart = null;
        await this.Logic.ClearFilters();
        this.Preview = Parser.GeneratePreview(this.Logic.TableName, this.Logic.Filters.Where(kvp => kvp.Value.GetValue() != null).ToDictionary()).Replace("Searching", "Showing");
        this.StateHasChanged();
    }

    /// <summary>
    /// When a log table page is created, initialize the caches and perform a refresh to load the table.
    /// This method runs on the assumption that <see cref="ComponentBase.OnInitialized"/> has been overridden to instantiate <see cref="Logic"/>.
    /// </summary>
    /// <returns>A Task representing that the page has been initialized.</returns>
    protected override async Task OnInitializedAsync()
    {
        await this.Logic.InitializeCaches();
        await this.RefreshData();
    }
}
