using Microsoft.AspNetCore.Components;
using HiokiNL2SQLMark1.Logic;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Components.Routing;

namespace HiokiNL2SQLMark1.Services;

/// <summary>
/// Builds a NavService using the specified NavigationManager.
/// Wires the built-in NavigationManager LocationChanged to the custom action
/// </summary>
public class NavService : INavService, IDisposable
{
    private readonly NavigationManager _nav;
    public event Action<string>? OnLocationChanged;
    private bool _isLocationChangedSubscribed = false;
    private string? _pendingNavigateUri;
    private bool _pendingNavigateReplace;

    /// <summary>
    /// Constructor - try to subscribe now but fall back to lazy subscription
    /// if NavigationManager isn't initialized yet (e.g., during prerendering).
    /// </summary>
    public NavService(NavigationManager nav)
    {
        _nav = nav;
        try
        {
            _nav.LocationChanged += HandleLocationChanged;
            _isLocationChangedSubscribed = true;
        }
        catch (InvalidOperationException)
        {
            // NavigationManager hasn't been initialized yet, subscribe lazily
        }
    }

    public bool EnsureSubscribed()
    {
        if (_isLocationChangedSubscribed) return true;
        try
        {
            _nav.LocationChanged += HandleLocationChanged;
            _isLocationChangedSubscribed = true;

            // If a navigation was attempted before NavigationManager was initialized,
            // perform it now.
            if (!string.IsNullOrEmpty(_pendingNavigateUri))
            {
                var uri = _pendingNavigateUri!;
                var replace = _pendingNavigateReplace;
                _pendingNavigateUri = null;
                try
                {
                    _nav.NavigateTo(uri, replace: replace);
                }
                catch (InvalidOperationException)
                {
                    // If it still fails, put it back for a future attempt
                    _pendingNavigateUri = uri;
                    _pendingNavigateReplace = replace;
                }
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            // NavigationManager hasn't been initialized yet; try again later
            return false;
        }
    }
    

    /// <summary>
    /// Encapsulates the specific URL structure for a trace
    /// </summary>
    /// <param name="barcode">The barcode to trace</param>
    public void NavigateToBarcodeTrace(string barcode) =>_nav.NavigateTo($"/?q=barcode:{barcode} in:all");

    /// <summary>
    /// Updates the URL to match the page details
    /// </summary>
    /// <param name="query">The filters to add under the q? param</param>
    /// <param name="page">The page number to add under the p? param</param>
    /// <param name="pageSize">The page size to add under the ps? param</param>
    /// <param name="sort">The sort column name to add under the s? param</param>
    /// <param name="dir">The sort direction to add under the d? param</param>
    /// <param name="replaceHistory">Whether to overwrite (or append) the new history frame</param>
    public void UpdateSearchState(string query, int? page=null, int? pageSize=null, string? sort=null, string? dir=null, bool replaceHistory=false)
    {
        string uri = "/"; 
    
        // For each parameter, if default, remove from URL, otherwise add/update it
        var parameters = new Dictionary<string, string?>
        {
            { "q", string.IsNullOrWhiteSpace(query) ? null : query },
            { "p", (page.HasValue && page > 1) ? page.ToString() : null },
            { "ps", (pageSize.HasValue && pageSize != 50) ? pageSize.ToString() : null },
            { "s", string.IsNullOrWhiteSpace(sort) ? null : sort },
            { "d", (string.IsNullOrEmpty(dir) || dir == "none") ? null : dir }
        };

        string newUri = QueryHelpers.AddQueryString(uri, parameters);

        // Try to subscribe and perform navigation. If NavigationManager is not yet
        // initialized, queue the navigation to perform once it becomes available.
        EnsureSubscribed();
        try
        {
            _nav.NavigateTo(newUri, replace: replaceHistory);
        }
        catch (InvalidOperationException)
        {
            _pendingNavigateUri = newUri;
            _pendingNavigateReplace = replaceHistory;
        }
    }

    /// <summary>
    /// Gets the q? parameter from the address (search bar contents)
    /// </summary>
    /// <returns>The query of the current page</returns>
    public string GetCurrentQuery()
    {
        try
        {
            var uri = _nav.ToAbsoluteUri(_nav.Uri);
            if (QueryHelpers.ParseQuery(uri.Query).TryGetValue("q", out var value))
            {
                return value.ToString();
            }
        }
        catch (InvalidOperationException)
        {
            // NavigationManager not initialized yet
        }

        return string.Empty;
    }

    /// <summary>
    /// Gets all the parameters from the URL and returns them in one record
    /// </summary>
    /// <returns>A record represesnting the query, page number & size, and sort column & direction</returns>
    public UrlState GetFullStateFromUrl()
    {
        try
        {
            var uri = _nav.ToAbsoluteUri(_nav.Uri);
            var q = QueryHelpers.ParseQuery(uri.Query);

            return new UrlState(
                Query: q.TryGetValue("q", out var query) ? query.ToString() : "",
                Page: q.TryGetValue("p", out var p) && int.TryParse(p, out var pi) ? pi : null,
                PageSize: q.TryGetValue("ps", out var ps) && int.TryParse(ps, out var psi) ? psi : null,
                SortCol: q.TryGetValue("s", out var s) ? s.ToString() : null,
                SortDir: q.TryGetValue("d", out var d) ? d.ToString() : null
            );
        }
        catch (InvalidOperationException)
        {
            // NavigationManager not initialized yet - return default empty state
            return new UrlState(Query: "", Page: null, PageSize: null, SortCol: null, SortDir: null);
        }
    }

    /// <summary>
    /// Hook for OnLocationChanged with the necessary signature for subscription
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void HandleLocationChanged(object? sender, LocationChangedEventArgs e) 
        => OnLocationChanged?.Invoke(e.Location);

    /// <summary>
    /// Upon navigating away from this page, unsubscribe from the URL monitor
    /// </summary>
    public void Dispose()
    {
        if (_isLocationChangedSubscribed)
        {
            _nav.LocationChanged -= HandleLocationChanged;
            _isLocationChangedSubscribed = false;
        }
    }
}