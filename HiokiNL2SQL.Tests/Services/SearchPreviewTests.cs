using System.Diagnostics.CodeAnalysis;
using HiokiNL2SQL.Logic;
using static HiokiNL2SQL.Tests.TestUtils;
using static HiokiNL2SQL.Services.SearchPreviewService;

namespace HiokiNL2SQL.Tests.Services;

/// <summary>
/// Tests for <see cref="HiokiNL2SQL.Services.SearchPreviewService"/>
/// </summary>
[ExcludeFromCodeCoverage]
public class GeneratePreviewTests
{
    [Fact]
    public void GeneratePreview_EmptyInput_ReturnsDefaultTablePreview()
    {
        // Arrange
        Dictionary<string, IFilter>  filters = [];

        // Act
        string result = GeneratePreview("fct", filters);

        // Assert
        Assert.Equal("Showing all results from **FCT**", result);
    }

    [Fact]
    public void GeneratePreview_StringFilter_GeneratesContainsLabel()
    {
        // Arrange
        Dictionary<string, IFilter>  filters = new ()
        {
            { "barcode", new Filter<string>("barcode", "12345")}
        };

        // Act
        string result = GeneratePreview("all", filters);

        // Assert
        Assert.Contains("Searching **ALL** where **BARCODE** contains '12345'", result);
    }

    [Fact]
    public void GeneratePreview_IntegerFilter_GeneratesIsLabel()
    {
        // Arrange
        Dictionary<string, IFilter>  filters = new ()
        {
            { "group", new Filter<int?>("group", 101)}
        };

        // Act
        string result = GeneratePreview("group", filters);

        // Assert
        Assert.Contains("**GROUP** is '101'", result);
    }

    [Fact]
    public void GeneratePreview_NegativeStringFilter_GeneratesDoesNotContainLabel()
    {
        // Arrange
        Dictionary<string, IFilter>  filters = new ()
        {
            { "result", new Filter<string?>("result", "FAIL", isNegated:true)}
        };

        // Act
        string result = GeneratePreview("all", filters);

        // Assert
        Assert.Contains("**RESULT** does NOT contain 'FAIL'", result);
    }

    [Fact]
    public void GeneratePreview_DateTimeFilterNoTime_FormatsCorrectly()
    {
        // Arrange
        Dictionary<string, IFilter>  filters = new ()
        {
            { "after", new Filter<DateTime?>("after", new (2026, 02, 11))}
        };

        // Act
        string result = GeneratePreview("all", filters);

        // Assert
        // The service uses yyyy-MM-dd HH:mm format in GeneratePreview
        Assert.Contains("DATE is **AFTER** '2026-02-11 00:00:00'", result);
    }

    [Fact]
    public void GeneratePreview_DateTimeFilterWithTime_FormatsCorrectly()
    {
        // Arrange
        Dictionary<string, IFilter>  filters = new ()
        {
            { "before", new Filter<DateTime?>("before", new (2026, 02, 11, 5, 22, 57, DateTimeKind.Local))}
        };

        // Act
        string result = GeneratePreview("all", filters);

        // Assert
        // The service uses yyyy-MM-dd HH:mm format in GeneratePreview
        Assert.Contains("DATE is **BEFORE** '2026-02-11 05:22:57'", result);
    }

    [Fact]
    public void GeneratePreview_MultipleFilters_JoinsWithAnd()
    {
        // Arrange
        Dictionary<string, IFilter>  filters = new ()
        {
            { "barcode", new Filter<string?>("barcode", "432960000020A25xx1558822072914382903")},
            { "step", new Filter<int?>("step", 99, isNegated:true)},
            { "part", new Filter<string?>("part", "R331")},
        };

        // Act
        string result = GeneratePreview("step", filters);

        // Assert
        // Order is determined by the order matches are added to the dictionary
        Assert.StartsWith("Searching **STEP** where", result);
        Assert.Contains("**BARCODE** contains '432960000020A25xx1558822072914382903'", result);
        AssertContainsXTimes("and", result, 2);
        Assert.Contains("**STEP** is NOT '99'", result);
        Assert.Contains("**PART** contains 'R331'", result);
    }

    [Fact]
    public void GeneratePreview_UnrecognizedFilterType_ShowsIncomplete()
    {
        // Arrange
        Dictionary<string, IFilter>  filters = new ()
        {
            { "flibber", new Filter<bool?>("flibber", value:true)},
            { "flobber", new Filter<double?>("flobber", 99.1)},
        };

        // Act
        string result = GeneratePreview("all", filters);

        // Assert
        Assert.Contains($"**FLIBBER** (incomplete value...)", result);
        Assert.Contains($"**FLOBBER** (incomplete value...)", result);
    }
}
