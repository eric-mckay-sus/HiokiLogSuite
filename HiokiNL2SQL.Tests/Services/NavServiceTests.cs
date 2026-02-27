using HiokiNL2SQLMark1.Services;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;

namespace HiokiNL2SQL.Tests.Services;

[ExcludeFromCodeCoverage]
public class NavServiceTests
{
    private readonly FakeNavigationManager _fakeNav;
    private readonly NavService _service;

    public NavServiceTests()
    {
        _fakeNav = new FakeNavigationManager();
        _service = new NavService(_fakeNav);
    }

    [Fact]
    public void NavigateToBarcodeTrace_ShouldBuildCorrectQueryString()
    {
        // Arrange
        var barcode = "12345";
        var expected = "http://localhost/?q=barcode:12345 in:all";

        // Act
        _service.NavigateToBarcodeTrace(barcode);

        // Assert
        Assert.Equal(expected, _fakeNav.Uri);
    }

    [Theory]
    [InlineData("test-query", 2, 25, "Name", "asc", "http://localhost/?q=test-query&p=2&ps=25&s=Name&d=asc")]
    [InlineData("", null, 50, null, "none", "http://localhost/")] // Defaults should be stripped
    public void UpdateSearchState_ShouldFormatUriCorrectly(string q, int? p, int? ps, string? s, string? d, string expected)
    {
        // Act
        _service.UpdateSearchState(q, p, ps, s, d);

        // Assert
        Assert.Equal(expected, _fakeNav.Uri);
    }

    [Fact]
    public void GetCurrentQuery_ShouldReturnDecodedValue_WhenParamExists()
    {
        // Arrange
        // Testing with encoded characters to ensure QueryHelpers handles them
        _fakeNav.NavigateTo("http://localhost/?q=barcode:123%20in:all");

        // Act
        var result = _service.GetCurrentQuery();

        // Assert
        Assert.Equal("barcode:123 in:all", result);
    }

    [Fact]
    public void GetCurrentQuery_ShouldReturnEmpty_WhenParamIsMissing()
    {
        // Arrange
        _fakeNav.NavigateTo("http://localhost/search?other=hidden");

        // Act
        var result = _service.GetCurrentQuery();

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData("http://localhost/?q=test&p=3&ps=10&s=Date&d=asc", "test", 3, 10, "Date", "asc")]
    [InlineData("http://localhost/?q=onlyquery", "onlyquery", null, null, null, null)]
    [InlineData("http://localhost/", "", null, null, null, null)]
    public void GetFullStateFromUrl_ShouldParseVariousUrlStructures(
        string url, string expectedQ, int? expectedP, int? expectedPS, string? expectedS, string? expectedD)
    {
        // Arrange
        _fakeNav.NavigateTo(url);

        // Act
        var state = _service.GetFullStateFromUrl();

        // Assert
        Assert.Equal(expectedQ, state.Query);
        Assert.Equal(expectedP, state.Page);
        Assert.Equal(expectedPS, state.PageSize);
        Assert.Equal(expectedS, state.SortCol);
        Assert.Equal(expectedD, state.SortDir);
    }

    [Fact]
    public void GetFullStateFromUrl_ShouldHandleMalformedIntegersGracefully()
    {
        // Arrange
        // Pass strings where integers are expected (p and ps)
        _fakeNav.NavigateTo("http://localhost/?p=notanumber&ps=invalid");

        // Act
        var state = _service.GetFullStateFromUrl();

        // Assert
        // int.TryParse should fail and return the nulls specified in your logic
        Assert.Null(state.Page);
        Assert.Null(state.PageSize);
    }

    [Fact]
    public void GetFullStateFromUrl_ShouldParseParametersCorrectly()
    {
        // Arrange
        _fakeNav.NavigateTo("http://localhost/?q=findme&p=5&s=Date");

        // Act
        var state = _service.GetFullStateFromUrl();

        // Assert
        Assert.Equal("findme", state.Query);
        Assert.Equal(5, state.Page);
        Assert.Equal("Date", state.SortCol);
        Assert.Null(state.SortDir); // Not in URL
    }

    [Fact]
    public void OnLocationChanged_ShouldFireWhenNavigationOccurs()
    {
        // Arrange
        string? capturedLocation = null;
        _service.EnsureSubscribed();
        _service.OnLocationChanged += (loc) => capturedLocation = loc;
        var newUri = "http://localhost/new-page";

        // Act
        _fakeNav.NavigateTo(newUri);

        // Assert
        Assert.Equal(newUri, capturedLocation);
    }

    [Fact]
    public void Dispose_ShouldSuccessfullyUnsubscribe()
    {
        // Arrange
        int callCount = 0;
        _service.OnLocationChanged += (loc) => callCount++;

        // Act
        _service.Dispose();
        _fakeNav.NavigateTo("http://localhost/test-after-dispose");

        // Assert
        Assert.Equal(0, callCount); 
    }

    [Theory]
    [InlineData("http://localhost/", true)]
    [InlineData("http://localhost", true)]
    [InlineData("http://localhost/?q=test", true)] // query params shouldn't matter
    [InlineData("http://localhost/group", false)]
    [InlineData("http://localhost/step", false)]
    public void IsOnPowerSearchPage_ShouldDetectRootPaths(string url, bool expected)
    {
        // Arrange
        _fakeNav.NavigateTo(url);

        // Act
        var result = _service.IsOnPowerSearchPage();

        // Assert
        Assert.Equal(expected, result);
    }
}

/// <summary>
/// A minimal implementation of NavigationManager for testing purposes
/// </summary>
[ExcludeFromCodeCoverage]
public class FakeNavigationManager : NavigationManager
{
    public FakeNavigationManager()
    {
        Initialize("http://localhost/", "http://localhost/");
    }

    protected override void NavigateToCore(string uri, bool forceLoad)
    {
        // NavigationManager handles the logic of joining base URIs and relative URIs
        var absoluteUri = ToAbsoluteUri(uri).ToString();
        Uri = absoluteUri;
        
        // Trigger the event so the Service can react
        NotifyLocationChanged(false);
    }
}
