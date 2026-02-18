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
    
    // Gets the current query string from the URL
    string GetCurrentQuery();

    UrlState GetFullStateFromUrl();
}

public record UrlState(string Query, int? Page, int? PageSize, string? SortCol, string? SortDir);