using Microsoft.AspNetCore.Components;
using HiokiNL2SQLMark1.Logic;

namespace HiokiNL2SQLMark1.Services;

public class NavService : INavService, IDisposable
{
    private readonly NavigationManager _nav;
    public event Action<string>? OnLocationChanged;

    /// <summary>
    /// Builds a NavService using the specified NavigationManager.
    /// Wires the built-in NavigationManager LocationChanged to the custom action
    /// </summary>
    /// <param name="nav">The NavigationManager to use</param>
    public NavService(NavigationManager nav)
    {
        _nav = nav;
        _nav.LocationChanged += (s, e) => OnLocationChanged?.Invoke(e.Location);
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

        string newUri = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(uri, parameters);

        _nav.NavigateTo(newUri, replace: replaceHistory);
    }

    /// <summary>
    /// Gets the 
    /// </summary>
    /// <returns></returns>
    public string GetCurrentQuery()
    {
        var uri = _nav.ToAbsoluteUri(_nav.Uri);
        if (Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query).TryGetValue("q", out var value))
        {
            return value.ToString();
        }
        return string.Empty;
    }

    public UrlState GetFullStateFromUrl()
    {
        var uri = _nav.ToAbsoluteUri(_nav.Uri);
        var q = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);

        return new UrlState(
            Query: q.TryGetValue("q", out var query) ? query.ToString() : "",
            Page: q.TryGetValue("p", out var p) && int.TryParse(p, out var pi) ? pi : null,
            PageSize: q.TryGetValue("ps", out var ps) && int.TryParse(ps, out var psi) ? psi : null,
            SortCol: q.TryGetValue("s", out var s) ? s.ToString() : null,
            SortDir: q.TryGetValue("d", out var d) ? d.ToString() : null
        );
    }

    public void Dispose() => _nav.LocationChanged -= (s, e) => OnLocationChanged?.Invoke(e.Location);
}