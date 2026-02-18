using Moq;
using HiokiNL2SQLMark1.Logic;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using HiokiNL2SQLMark1;
using System.Diagnostics.CodeAnalysis;

namespace HiokiNL2SQL.Tests.Logic;
[ExcludeFromCodeCoverage]
public class FakeNavigationManager : NavigationManager
{
    public FakeNavigationManager()
    {
        // This initializes the base class with the required URIs
        // so the Proxy doesn't explode.
        Initialize("http://localhost/", "http://localhost/");
    }

    protected override void NavigateToCore(string uri, bool forceLoad)
    {
        // Track navigation for assertions if needed
        Uri = uri; 
    }
}

[ExcludeFromCodeCoverage]
public class PowerSearchLogicTests
{
    private readonly Mock<ILogTableLogic> _mockTable;
    private readonly SearchParserService _realParser;
    private readonly FakeNavigationManager _fakeNav;
    private readonly Mock<IJSRuntime> _mockJs;
    private readonly PowerSearchLogic _logic;

    public PowerSearchLogicTests()
    {
        _mockTable = new Mock<ILogTableLogic>();
        _mockTable.SetupAllProperties();
        _mockTable.Setup(t => t.TableName).Returns("group");

        _realParser = new SearchParserService(); // Concrete instance
        _fakeNav = new FakeNavigationManager();
        // Initialize the internal state of the NavigationManager
        _mockJs = new Mock<IJSRuntime>();

        _logic = new PowerSearchLogic(
            [_mockTable.Object],
            _realParser,
            _fakeNav,
            _mockJs.Object
        );
    }

    [Fact]
    public async Task ExecutePowerSearch_Integration_UpdatesStateBasedOnRealParsing()
    {
        // Arrange
        // We use a string that the real parser will recognize
        _logic.commandInput = "barcode:A123 in:group";
        _logic.CurrentType = "all";

        // Act
        await _logic.ExecutePowerSearch(skipUrlUpdate: true);

        // Assert
        // 1. Verify the Logic class updated its internal state from the Real Parser
        Assert.Equal("group", _logic.CurrentType);
        Assert.Contains("barcode", _logic.Filters.Keys);
        
        // 2. Verify the preview was generated correctly by the real service
        Assert.Contains("A123", _logic.Preview);

        // 3. Verify the targeted table was actually called
        _mockTable.Verify(t => t.DictionaryToFilters(It.IsAny<Dictionary<string, IFilter>>(), false), Times.Once);
    }

    [Fact]
    public async Task ExecutePowerSearch_WithWarning_StillExecutesSearch()
    {
        // Arrange
        // Simulate a query that triggers a warning (containing "This search")
        // Ensure your SearchParserService is set up to return this specific string for this input
        _logic.commandInput = "result:"; 
        
        // Act
        await _logic.ExecutePowerSearch(skipUrlUpdate: true);

        // Assert
        // Verify that even with a warning, the search was NOT aborted
        _mockTable.Verify(t => t.DictionaryToFilters(It.IsAny<Dictionary<string, IFilter>>(), false), Times.Once);
    }

    [Fact]
    public async Task ExecutePowerSearch_WithFatalError_ClearsDataAndAborts()
    {
        // Arrange
        // Simulating an invalid tag that causes a fatal error in your Parser
        _logic.commandInput = "invalidtag:something"; 
        
        // Track if NotifyStateChanged was called
        bool refreshCalled = false;
        _logic.OnRefreshRequested += () => refreshCalled = true;

        // Act
        await _logic.ExecutePowerSearch(skipUrlUpdate: true);

        // Assert
        // 1. Logic should detect errors from the real ParserService
        Assert.NotEmpty(_logic.errorMessages);
        
        // 2. Since errors are fatal, IsSearching must be false
        Assert.False(_logic.IsSearching);

        // 3. ClearData() should be called on the table to prevent stale results
        _mockTable.Verify(t => t.ClearData(), Times.AtLeastOnce);

        // 4. DictionaryToFilters should NEVER be called on a fatal error
        _mockTable.Verify(t => t.DictionaryToFilters(It.IsAny<Dictionary<string, IFilter>>(), false), Times.Never);

        // 5. The UI should have been notified of the error state
        Assert.True(refreshCalled);
    }

    [Fact]
    public async Task ExecutePowerSearch_BypassesDB_WhenHashMatches()
    {
        // Arrange
        _logic.commandInput = "barcode:123 in:group";
        int expectedHash = 12345;

        // Setup the mock table to look like it already has this data
        _mockTable.Setup(t => t.TableName).Returns("group");
        _mockTable.Setup(t => t.LastQueryHash).Returns(expectedHash);
        _mockTable.Setup(t => t.TotalCount).Returns(10);
        _mockTable.Setup(t => t.GetFilterStateHash(It.IsAny<Dictionary<string, IFilter>>()))
                .Returns(expectedHash);

        // Act
        await _logic.ExecutePowerSearch();

        // Assert
        // Verify we never touched the database (via DictionaryToFilters)
        _mockTable.Verify(t => t.DictionaryToFilters(It.IsAny<Dictionary<string, IFilter>>(), It.IsAny<bool>()), 
            Times.Never);
    }

    [Fact]
    public async Task UpdateSearchState_TableMatched_SetsAllParameters()
    {
        // Arrange
        _logic.CurrentType = "group"; // Matches _mockTable setup in constructor
        string testFilters = "barcode:123 in:group";
        
        // Act
        await _logic.UpdateSearchState(testFilters, pageSize: 50, page: 3, sortCol: "Time", sortDir: "desc");

        // Assert
        Assert.Equal(testFilters, _logic.commandInput);
        Assert.Equal(50, _mockTable.Object.PageSize);
        Assert.Equal(3, _mockTable.Object.CurrentPage);
        Assert.Equal("Time", _mockTable.Object.CurrentSortColumn);
        Assert.Equal("desc", _mockTable.Object.SortDir);
        
        // Verify ExecutePowerSearch was called (via DictionaryToFilters)
        // keepPage should be true because page.HasValue was true
        _mockTable.Verify(t => t.DictionaryToFilters(It.IsAny<Dictionary<string, IFilter>>(), true), Times.Once);
    }

    [Fact]
    public async Task UpdateSearchState_TableMatched_IgnoresNullParameters()
    {
        // Arrange
        _logic.CurrentType = "group";
        _mockTable.Object.PageSize = 20; // Original value
        
        // Act
        await _logic.UpdateSearchState("in:group", pageSize: null, page: null, sortCol: null);

        // Assert
        Assert.Equal(20, _mockTable.Object.PageSize); // Should not have changed
        
        // keepPage should be false because page.HasValue was false
        _mockTable.Verify(t => t.DictionaryToFilters(It.IsAny<Dictionary<string, IFilter>>(), false), Times.Once);
    }

    [Fact]
    public async Task UpdateSearchState_NoTableMatched_UpdatesInputOnly()
    {
        // Arrange
        _logic.CurrentType = "none_matching";
        
        // Act
        await _logic.UpdateSearchState("barcode:xyz", page: 5);

        // Assert
        Assert.Equal("barcode:xyz", _logic.commandInput);
        // Ensure no calls were made to the mock table because it wasn't the active one
        _mockTable.VerifySet(t => t.CurrentPage = It.IsAny<int>(), Times.Never);
    }

    [Fact]
    public async Task UpdateSearchState_SortProvidedWithoutDirection_DefaultsToNone()
    {
        // Arrange
        _logic.CurrentType = "group";

        // Act
        await _logic.UpdateSearchState("in:group", sortCol: "Barcode", sortDir: null);

        // Assert
        Assert.Equal("Barcode", _mockTable.Object.CurrentSortColumn);
        Assert.Equal("none", _mockTable.Object.SortDir);
    }

    [Theory]
    [InlineData("", "today", "after:today ")] // Empty input
    [InlineData("barcode:123", "yesterday", "barcode:123 after:yesterday ")] // Appending with space
    [InlineData("after:", "today", "after:today ")] // Completing a partial tag
    public async Task AppendShortcut_BuildsCorrectString(string initial, string shortcut, string expected)
    {
        // Arrange
        _logic.commandInput = initial;

        // Act
        await _logic.AppendShortcut("after", shortcut);

        // Assert
        Assert.Equal(expected, _logic.commandInput);
    }

    [Fact]
    public async Task SetType_InsertsNewInTagIfMissing()
    {
        // Arrange
        _logic.commandInput = "barcode:123";
        
        // Act
        await _logic.SetType("fct");

        // Assert
        // It should replace "in:group" with "in:step" and NOT duplicate it
        Assert.Equal("barcode:123 in:fct", _logic.commandInput);
        Assert.Equal("fct", _logic.CurrentType);
    }

    [Fact]
    public async Task SetType_ReplacesExistingInTag()
    {
        // Arrange
        _logic.commandInput = "barcode:123 in:group result:fail";
        
        // Act
        await _logic.SetType("step");

        // Assert
        // It should replace "in:group" with "in:step" and NOT duplicate it
        Assert.Equal("barcode:123 in:step result:fail", _logic.commandInput);
        Assert.Equal("step", _logic.CurrentType);
    }

    [Fact]
    public async Task SetType_ReplacesInTag_CaseInsensitive()
    {
        // Arrange
        _logic.commandInput = "barcode:123 IN:OLDTYPE";

        // Act
        await _logic.SetType("newtype");

        // Assert
        // Verify it didn't just append a second 'in' tag
        Assert.Contains("in:newtype", _logic.commandInput.ToLower());
        Assert.DoesNotContain("in:oldtype", _logic.commandInput.ToLower());
    }

    [Fact]
    public async Task HandleInput_DebouncesCorrectlyAndReplacesOldTimer()
    {
        // Arrange
        var inputEvent = new ChangeEventArgs { Value = "searching..." };
        _logic.DebounceTimer = new System.Timers.Timer(300);

        // Act
        _logic.HandleInput(inputEvent);
        
        // Immediate check: Preview shouldn't be updated yet because of 300ms debounce
        Assert.NotEqual("searching...", _logic.Preview);

        // Wait for timer (300ms + buffer)
        await Task.Delay(400);

        // Assert
        Assert.Equal("searching...", _logic.commandInput);
        // The Preview property is updated inside OnUserStoppedTyping
        Assert.NotEmpty(_logic.Preview);
    }

    [Theory]
    // Standard removal
    [InlineData("barcode:A123 result:fail", "barcode", "result:fail")]
    // Removal of negative tags
    [InlineData("-barcode:A123 result:fail", "barcode", "result:fail")]
    // Handling quoted values (should remove the whole quoted phrase)
    [InlineData("barcode:\"asdkjlgh asd\" group:30", "barcode", "group:30")]
    // Removing the middle tag (verifies space cleanup)
    [InlineData("tag1:val1 tag2:val2 tag3:val3", "tag2", "tag1:val1 tag3:val3")]
    // Removing a tag that doesn't exist (should do nothing)
    [InlineData("barcode:A123", "status", "barcode:A123")]
    // Removing a tag that is a value elsewhere (should not remove the wrong one)
    [InlineData("group:1 in:group", "group", "in:group")]
    public void RemoveTagFromQuery_CleansStringAndMaintainsIntegrity(string input, string tagToRemove, string expected)
    {
        // Arrange
        _logic.commandInput = input;

        // Act
        _logic.RemoveTagFromQuery(tagToRemove);

        // Assert
        // We trim both to ensure we aren't failing on simple trailing whitespace
        Assert.Equal(expected.Trim(), _logic.commandInput.Trim());
    }

    [Fact]
    public void RemoveTagFromQuery_WhenKeyIsIn_ResetsCurrentTypeToAll()
    {
        // Arrange
        _logic.commandInput = "in:group barcode:123";
        _logic.CurrentType = "group";

        // Act
        _logic.RemoveTagFromQuery("in");

        // Assert
        Assert.Equal("all", _logic.CurrentType);
        Assert.Equal("barcode:123", _logic.commandInput);
    }

    [Fact]
    public void RemoveTagFromQuery_WhenEmpty_ClearsTargetTables()
    {
        // Arrange
        _logic.commandInput = "barcode:A123";
        
        // Act
        _logic.RemoveTagFromQuery("barcode");

        // Assert
        Assert.Equal("", _logic.commandInput);
        // Verify that data was cleared because the query became empty
        _mockTable.Verify(t => t.ClearData(), Times.Once);
    }

    [Fact]
    public async Task AppendKey_AddsKeyWithSpace_WhenInputNotEmpty()
    {
        // Arrange
        _logic.commandInput = "barcode:A123";
        string keyToAppend = "result";

        // Act
        await _logic.AppendKey(keyToAppend, fromTableTab: false);

        // Assert
        Assert.Equal("barcode:A123 result:", _logic.commandInput);
        // Verify JS focus was called because fromTableTab is false
        _mockJs.Verify(js => js.InvokeAsync<object>("focusElement", 
            It.Is<object[]>(args => args.Contains("searchBar"))), Times.Once);
    }

    [Fact]
    public async Task AppendKey_DoesNotDuplicate_IfKeyExists()
    {
        // Arrange
        _logic.commandInput = "result:fail";
        string keyToAppend = "result";

        // Act
        var result = await _logic.AppendKey(keyToAppend, fromTableTab: false);

        // Assert
        Assert.False(result); // Method returns false if it skipped appending
        Assert.Equal("result:fail", _logic.commandInput); // String remains unchanged
    }

    [Fact]
    public async Task AppendKey_NoLeadingSpace_WhenInputIsEmpty()
    {
        // Arrange
        _logic.commandInput = "";

        // Act
        await _logic.AppendKey("in", fromTableTab: true);

        // Assert
        Assert.Equal("in:", _logic.commandInput);
    }
}