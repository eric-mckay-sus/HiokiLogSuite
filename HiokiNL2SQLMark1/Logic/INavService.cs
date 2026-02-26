namespace HiokiNL2SQLMark1.Logic;

/// <summary>
/// Abstracts the use of NavigationManager to keep the model classes pure
/// </summary>
public interface INavService
{
    /// <summary>
    /// The action to perform when the URL changes
    /// </summary>
    event Action<string>? OnLocationChanged;

    /// <summary>
    /// Subscribes to the location change event
    /// </summary>
    bool EnsureSubscribed();

    /// <summary>
    /// Navigates to the search page with a specific barcode filter
    /// </summary>
    /// <param name="barcode">The barcode to trace</param>
    void NavigateToBarcodeTrace(string barcode);

    /// <summary>
    /// Updates the current URL with the search query and details without a full reload
    /// If a field is null, clears it from the 
    /// </summary>
    /// <param name="query">The filters to add under the q? param</param>
    /// <param name="page">The page number to add under the p? param</param>
    /// <param name="pageSize">The page size to add under the ps? param</param>
    /// <param name="sort">The sort column name to add under the s? param</param>
    /// <param name="dir">The sort direction to add under the d? param</param>
    /// <param name="replaceHistory">Whether to overwrite (or append) the new history frame</param>
    void UpdateSearchState(string query, int? page = null, int? pageSize = null, 
                         string? sort = null, string? dir = null, bool replaceHistory = false);
    
    /// <summary>
    /// Gets the q? parameter from the address (search bar contents)
    /// </summary>
    /// <returns>The query of the current page</returns>
    string GetCurrentQuery();

    /// <summary>
    /// Upon navigating away from this page, unsubscribe from the URL monitor
    /// </summary>
    UrlState GetFullStateFromUrl();
}

/// <summary>
/// A record representing the parameters from the URL of the power search page
/// </summary>
/// <param name="Query">Filters</param>
/// <param name="Page">Page number</param>
/// <param name="PageSize">Entries per page</param>
/// <param name="SortCol">Column to apply sort to</param>
/// <param name="SortDir">Direction to sort in</param>
public record UrlState(string Query, int? Page, int? PageSize, string? SortCol, string? SortDir);