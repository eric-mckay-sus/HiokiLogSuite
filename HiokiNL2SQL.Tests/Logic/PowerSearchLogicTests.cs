using Moq;
using HiokiNL2SQLMark1.Logic;
using HiokiNL2SQLMark1.Services;
using System.Diagnostics.CodeAnalysis;

namespace HiokiNL2SQL.Tests.Logic;
[ExcludeFromCodeCoverage]
public class PowerSearchLogicTests
{
    private readonly Mock<ILogTableLogic> _mockTable;
    private readonly SearchParserService _realParser;
    private readonly Mock<INavService> _mockNav;
    private readonly Mock<IJSService> _mockJs;
    private readonly PowerSearchLogic _logic;

    private Func<bool>? _capturedUIStale;

    public PowerSearchLogicTests()
    {
        _mockTable = new Mock<ILogTableLogic>();
        _mockTable.SetupAllProperties();
        _mockTable.Setup(t => t.TableName).Returns("group");

        _realParser = new SearchParserService(); // Concrete instance
        _mockNav = new Mock<INavService>();
        _mockJs = new Mock<IJSService>();

        _logic = new PowerSearchLogic([_mockTable.Object], _realParser, _mockNav.Object, _mockJs.Object);

        // capture delegate after construction (should have been assigned by ctor)
        _capturedUIStale = _mockTable.Object.UIIsStaleOverride;

        // capture delegate after construction (should have been assigned by ctor)
        _capturedUIStale = _mockTable.Object.UIIsStaleOverride;
    }

    [Fact]
    public void Constructor_Wires_UIIsStaleOverride()
    {
        // Delegate should be non-null and respond to mismatches between tab name and input.
        Assert.NotNull(_capturedUIStale);

        // default LastExecutedQuery is the welcome string; any non-empty commandInput should
        // produce a stale result
        _logic.CommandInput = "foo";
        Assert.True(_capturedUIStale());

        // if we pretend the tab already matches, it should return false
        _logic.LastExecutedQuery = "Search: foo";
        Assert.False(_capturedUIStale());
    }

    [Fact]
    public async Task ExecutePowerSearch_Integration_UpdatesStateBasedOnRealParsing()
    {
        // Arrange
        // We use a string that the real parser will recognize
        _logic.CommandInput = "barcode:A123 in:group";
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
        _mockTable.Verify(t => t.DictionaryToFilters(It.IsAny<Dictionary<string, IFilter>>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public async Task ExecutePowerSearch_WithWarning_StillExecutesSearch()
    {
        // Arrange
        // Simulate a query that triggers a warning (containing "This search")
        // Ensure your SearchParserService is set up to return this specific string for this input
        _logic.CommandInput = "result:";

        // Act
        await _logic.ExecutePowerSearch(skipUrlUpdate: true);

        // Assert
        // Verify that even with a warning, the search was NOT aborted
        _mockTable.Verify(t => t.DictionaryToFilters(It.IsAny<Dictionary<string, IFilter>>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public async Task ExecutePowerSearch_WithFatalError_ClearsDataAndAborts()
    {
        // Arrange
        // Simulating an invalid tag that causes a fatal error in your Parser
        _logic.CommandInput = "invalidtag:something";

        // Track if NotifyStateChanged was called
        bool refreshCalled = false;
        _logic.OnRefreshRequested += () => refreshCalled = true;

        // Act
        await _logic.ExecutePowerSearch(skipUrlUpdate: true);

        // Assert
        // 1. Logic should detect errors from the real ParserService
        Assert.NotEmpty(_logic.ErrorMessages);

        // 2. Since errors are fatal, IsSearching must be false
        Assert.False(_logic.IsSearching);

        // 3. ClearData() should be called on the table to prevent stale results
        _mockTable.Verify(t => t.ClearData(), Times.AtLeastOnce);

        // 4. DictionaryToFilters should NEVER be called on a fatal error
        _mockTable.Verify(t => t.DictionaryToFilters(It.IsAny<Dictionary<string, IFilter>>(), It.IsAny<bool>()), Times.Never);

        // 5. The UI should have been notified of the error state
        Assert.True(refreshCalled);
    }

    [Fact]
    public async Task ExecutePowerSearch_DoesNotRunTables_WhenNoInput()
    {
        // Arrange: start with empty input (default) and a prepared mock
        _logic.CommandInput = "";

        // Act
        await _logic.ExecutePowerSearch(skipUrlUpdate: true);

        // Assert: we should not call DictionaryToFilters and tables cleared
        _mockTable.Verify(t => t.DictionaryToFilters(It.IsAny<Dictionary<string, IFilter>>(), It.IsAny<bool>()),
            Times.Never);
        _mockTable.Verify(t => t.ClearData(), Times.AtLeastOnce);
        Assert.Equal("Hioki ICT Power Search", _logic.LastExecutedQuery);
    }

    [Fact]
    public async Task ExecutePowerSearch_BypassesDB_WhenHashMatches()
    {
        // Arrange
        _logic.CommandInput = "barcode:123 in:group";

        // Setup the mock table to look like it already has this data
        _mockTable.Setup(t => t.TableName).Returns("group");
        _mockTable.Setup(t => t.TotalCount).Returns(10);

        // Act
        await _logic.ExecutePowerSearch();

        // Assert
        // Verify we never touched the database (via RefreshData)
        _mockTable.Verify(t => t.RefreshData(It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Theory]
    [InlineData("", "today", "after:today ")] // Empty input
    [InlineData("barcode:123", "yesterday", "barcode:123 after:yesterday ")] // Appending with space
    [InlineData("after:", "today", "after:today ")] // Completing a partial tag
    public async Task AppendShortcut_BuildsCorrectString(string initial, string shortcut, string expected)
    {
        // Arrange
        _logic.CommandInput = initial;

        // Act
        await _logic.AppendShortcut("after", shortcut);

        // Assert
        Assert.Equal(expected, _logic.CommandInput);
    }

    [Fact]
    public async Task SetType_InsertsNewInTagIfMissing()
    {
        // Arrange
        _logic.CommandInput = "barcode:123";

        // Act
        await _logic.SetType("fct");

        // Assert
        // It should replace "in:group" with "in:step" and NOT duplicate it
        Assert.Equal("barcode:123 in:fct", _logic.CommandInput);
        Assert.Equal("fct", _logic.CurrentType);
    }

    [Fact]
    public async Task SetType_ReplacesExistingInTag()
    {
        // Arrange
        _logic.CommandInput = "barcode:123 in:group result:fail";

        // Act
        await _logic.SetType("step");

        // Assert
        // It should replace "in:group" with "in:step" and NOT duplicate it
        Assert.Equal("barcode:123 in:step result:fail", _logic.CommandInput);
        Assert.Equal("step", _logic.CurrentType);
    }

    [Fact]
    public async Task SetType_ReplacesInTag_CaseInsensitive()
    {
        // Arrange
        _logic.CommandInput = "barcode:123 IN:OLDTYPE";

        // Act
        await _logic.SetType("newtype");

        // Assert
        // Verify it didn't just append a second 'in' tag
        Assert.Contains("in:newtype", _logic.CommandInput.ToLower());
        Assert.DoesNotContain("in:oldtype", _logic.CommandInput.ToLower());
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
        _logic.CommandInput = input;

        // Act
        _logic.RemoveTagFromQuery(tagToRemove);

        // Assert
        // We trim both to ensure we aren't failing on simple trailing whitespace
        Assert.Equal(expected.Trim(), _logic.CommandInput.Trim());
    }

    [Fact]
    public void RemoveTagFromQuery_WhenKeyIsIn_ResetsCurrentTypeToAll()
    {
        // Arrange
        _logic.CommandInput = "in:group barcode:123";
        _logic.CurrentType = "group";

        // Act
        _logic.RemoveTagFromQuery("in");

        // Assert
        Assert.Equal("all", _logic.CurrentType);
        Assert.Equal("barcode:123", _logic.CommandInput);
    }

    [Fact]
    public void RemoveTagFromQuery_WhenEmpty_ClearsTargetTables()
    {
        // Arrange
        _logic.CommandInput = "barcode:A123";

        // Act
        _logic.RemoveTagFromQuery("barcode");

        // Assert
        Assert.Equal("", _logic.CommandInput);
        // Verify that data was cleared because the query became empty
        _mockTable.Verify(t => t.ClearData(), Times.Once);
    }

    [Fact]
    public async Task AppendKey_AddsKeyWithSpace_WhenInputNotEmpty()
    {
        // Arrange
        _logic.CommandInput = "barcode:A123";
        string keyToAppend = "result";

        // Act
        await _logic.AppendKey(keyToAppend, fromTableTab: false);

        // Assert
        Assert.Equal("barcode:A123 result:", _logic.CommandInput);
        // Verify JS focus was called because fromTableTab is false
        _mockJs.Verify(js => js.FocusElement("searchBar"), Times.Once);
    }

    [Fact]
    public async Task AppendKey_DoesNotDuplicate_IfKeyExists()
    {
        // Arrange
        _logic.CommandInput = "result:fail";
        string keyToAppend = "result";

        // Act
        var result = await _logic.AppendKey(keyToAppend, fromTableTab: false);

        // Assert
        Assert.False(result); // Method returns false if it skipped appending
        Assert.Equal("result:fail", _logic.CommandInput); // String remains unchanged
    }

    [Fact]
    public async Task AppendKey_NoLeadingSpace_WhenInputIsEmpty()
    {
        // Arrange
        _logic.CommandInput = "";

        // Act
        await _logic.AppendKey("in", fromTableTab: true);

        // Assert
        Assert.Equal("in:", _logic.CommandInput);
    }
}
