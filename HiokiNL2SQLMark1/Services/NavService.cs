// <copyright file="NavService.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace HiokiNL2SQLMark1.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Primitives;

using HiokiNL2SQLMark1.Logic;

/// <summary>
/// An implementation of <see cref="INavService"/> that wires the built-in NavigationManager LocationChanged event to the custom action.
/// Builds a NavService using the specified NavigationManager.
/// </summary>
public class NavService(NavigationManager nav) : INavService, IDisposable
{
    private readonly NavigationManager navManager = nav;
    private bool isLocationChangedSubscribed = false;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public event Action<string>? OnLocationChanged;

    /// <summary>
    /// "Lazy subscription": because the NavigationManager doesn't actually exist at render time (we just reference it for the nav field), we have to check the subscription every time we wish to use it.
    /// </summary>
    /// <returns>Whether <see cref="nav"/>'s subscription was successfully verified.</returns>
    public bool EnsureSubscribed()
    {
        if (this.isLocationChangedSubscribed)
        {
            return true;
        }

        try
        {
            this.navManager.LocationChanged -= this.HandleLocationChanged; // Prevent double-subs
            this.navManager.LocationChanged += this.HandleLocationChanged;
            this.isLocationChangedSubscribed = true;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Encapsulates the specific URL structure for a trace.
    /// </summary>
    /// <param name="barcode">The barcode to trace.</param>
    public void NavigateToBarcodeTrace(string barcode)
    {
        if (this.EnsureSubscribed())
        {
            this.navManager.NavigateTo($"/?q=barcode:{barcode} in:all");
        }
    }

    /// <summary>
    /// Updates the URL to match the page details.
    /// </summary>
    /// <param name="query">The filters to add under the q? param.</param>
    /// <param name="page">The page number to add under the p? param.</param>
    /// <param name="pageSize">The page size to add under the ps? param.</param>
    /// <param name="sort">The sort column name to add under the s? param.</param>
    /// <param name="dir">The sort direction to add under the d? param.</param>
    /// <param name="replaceHistory">Whether to overwrite (or append) the new history frame.</param>
    public void UpdateSearchState(string query, int? page = null, int? pageSize = null, string? sort = null, string? dir = null, bool replaceHistory = false)
    {
        string uri = "/";

        // For each parameter, if default, remove from URL, otherwise add/update it
        var parameters = new Dictionary<string, string?>
        {
            { "q", string.IsNullOrWhiteSpace(query) ? null : query },
            { "p", (page.HasValue && page > 1) ? page.ToString() : null },
            { "ps", (pageSize.HasValue && pageSize != 50) ? pageSize.ToString() : null },
            { "s", string.IsNullOrWhiteSpace(sort) ? null : sort },
            { "d", (string.IsNullOrEmpty(dir) || dir == "none") ? null : dir },
        };

        string newUri = QueryHelpers.AddQueryString(uri, parameters);

        // Only try navigation if subscribed
        if (this.EnsureSubscribed())
        {
            this.navManager.NavigateTo(newUri, replace: replaceHistory);
        }
    }

    /// <summary>
    /// Gets the q? parameter from the address (search bar contents).
    /// </summary>
    /// <returns>The query of the current page.</returns>
    public string GetCurrentQuery()
    {
        if (this.EnsureSubscribed())
        {
            Uri uri = this.navManager.ToAbsoluteUri(this.navManager.Uri);
            if (QueryHelpers.ParseQuery(uri.Query).TryGetValue("q", out StringValues value))
            {
                return value.ToString();
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Gets all the parameters from the URL and returns them in one record.
    /// </summary>
    /// <returns>A <see cref="UrlState"/> represesnting the query, page number & size, and sort column & direction.</returns>
    public UrlState GetFullStateFromUrl()
    {
        if (this.EnsureSubscribed())
        {
            Uri uri = this.navManager.ToAbsoluteUri(this.navManager.Uri);
            Dictionary<string, StringValues> q = QueryHelpers.ParseQuery(uri.Query);

            return new UrlState(
                query: q.TryGetValue("q", out StringValues query) ? query.ToString() : string.Empty,
                page: q.TryGetValue("p", out StringValues p) && int.TryParse(p, out int pi) ? pi : null,
                pageSize: q.TryGetValue("ps", out StringValues ps) && int.TryParse(ps, out int psi) ? psi : null,
                sortCol: q.TryGetValue("s", out StringValues s) ? s.ToString() : null,
                sortDir: q.TryGetValue("d", out StringValues d) ? d.ToString() : null);
        }

        // NavigationManager not initialized yet - return default empty state
        return new UrlState(query: string.Empty, page: null, pageSize: null, sortCol: null, sortDir: null);
    }

    /// <summary>
    /// Determines if the user is currently on the power search page.
    /// A trailing slash ("/") and the empty path are treated as equivalent.
    /// </summary>
    /// <returns>Whether the current page is power search.</returns>
    public bool IsOnPowerSearchPage()
    {
        if (!this.EnsureSubscribed())
        {
            return false;
        }

        Uri uri = this.navManager.ToAbsoluteUri(this.navManager.Uri);

        // Only compare the base path, the query string is irrelevant here
        string path = uri.AbsolutePath.TrimEnd('/');
        return string.IsNullOrEmpty(path);
    }

    /// <summary>
    /// Upon navigating away from this page, unsubscribe from the URL monitor.
    /// </summary>
    public void Dispose()
    {
        if (this.isLocationChangedSubscribed)
        {
            this.navManager.LocationChanged -= this.HandleLocationChanged;
            this.isLocationChangedSubscribed = false;
        }
    }

    /// <summary>
    /// Hook for OnLocationChanged with the necessary signature for subscription.
    /// </summary>
    /// <param name="sender">The source of the location change.</param>
    /// <param name="e">The event containing the location change information.</param>
    private void HandleLocationChanged(object? sender, LocationChangedEventArgs e)
        => this.OnLocationChanged?.Invoke(e.Location);
}
