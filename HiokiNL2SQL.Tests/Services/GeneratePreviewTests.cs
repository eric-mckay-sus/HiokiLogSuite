using System.Diagnostics.CodeAnalysis;
using HiokiNL2SQL.Services;

namespace HiokiNL2SQL.Tests.Services;
[ExcludeFromCodeCoverage]

// GeneratePreview is private, so we use the preview from ParseQuery
public class GeneratePreviewTests
{
    private readonly SearchParserService _parser = new();

    [Fact]
    public void ParseQuery_EmptyInput_ReturnsDefaultTablePreview()
    {
        // Arrange
        string input = "";

        // Act
        var result = _parser.ParseQuery(input, "fct");

        // Assert
        Assert.Equal("Showing all results from **FCT**", result.Preview);
    }

    [Fact]
    public void ParseQuery_StringFilter_GeneratesContainsLabel()
    {
        // Arrange
        string input = "barcode:12345";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Contains("Searching **ALL** where **BARCODE** contains '12345'", result.Preview);
    }

    [Fact]
    public void ParseQuery_IntegerFilter_GeneratesIsLabel()
    {
        // Arrange
        // 'group' is mapped to ValType.Int in TagTypeMap
        string input = "group:101";

        // Act
        var result = _parser.ParseQuery(input, "group");

        // Assert
        Assert.Contains("**GROUP** is '101'", result.Preview);
    }

    [Fact]
    public void ParseQuery_NegativeStringFilter_GeneratesDoesNotContainLabel()
    {
        // Arrange
        string input = "-result:FAIL";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Contains("**RESULT** does NOT contain 'FAIL'", result.Preview);
    }

    [Fact]
    public void ParseQuery_DateTimeFilterNoTime_FormatsCorrectly()
    {
        // Arrange
        // Testing "after" tag which is ValType.DateTime
        string input = "after:2026-02-11";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        // The service uses yyyy-MM-dd HH:mm format in GeneratePreview
        Assert.Contains("DATE is **AFTER** '2026-02-11 00:00:00'", result.Preview);
    }

    [Fact]
    public void ParseQuery_DateTimeFilterWithTime_FormatsCorrectly()
    {
        // Arrange
        // Testing "after" tag which is ValType.DateTime
        string input = "after:\"2026-02-11 05:00\"";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        // The service uses yyyy-MM-dd HH:mm format in GeneratePreview
        Assert.Contains("DATE is **AFTER** '2026-02-11 05:00:00'", result.Preview);
    }

    [Fact]
    public void ParseQuery_MultipleFilters_JoinsWithAnd()
    {
        // Arrange
        string input = "in:step barcode:PCB99 part:Resistor";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        // Order is determined by the order matches are added to the dictionary
        Assert.StartsWith("Searching **STEP** where", result.Preview);
        Assert.Contains("**BARCODE** contains 'PCB99'", result.Preview);
        Assert.Contains("and", result.Preview);
        Assert.Contains("**PART** contains 'Resistor'", result.Preview);
    }

    [Fact]
    public void ParseQuery_DateAlias_GeneratesTranslatedPreview()
    {
        // Arrange
        string input = "before:today";
        string expectedDate = DateTime.Today.AddDays(1).AddTicks(-1).ToString("yyyy-MM-dd HH:mm:ss");

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Contains($"DATE is **BEFORE** '{expectedDate}'", result.Preview);
    }
}
