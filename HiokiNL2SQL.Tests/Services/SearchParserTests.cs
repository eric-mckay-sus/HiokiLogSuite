using System.Diagnostics.CodeAnalysis;
using HiokiNL2SQL.Services;
using static HiokiNL2SQL.Services.SearchParserService;
using HiokiNL2SQL.Logic;

namespace HiokiNL2SQL.Tests.Services;

/// <summary>
/// Unit tests for sub-methods called by <see cref="SearchParserService.ParseQuery"/>
/// </summary>
[ExcludeFromCodeCoverage]
public class SearchParserTests
{
    #region ProcessInTag

    [Fact]
    public void ProcessInTag_ResolvesDuplicateInTag_ToLatestInstance()
    {
        // Arrange
        string input = "in: group in:step";
        SearchParseResult result = new ();

        // Act
        ProcessInTag(input, result);

        // Assert
        Assert.Single(result.ErrorMessages);
        Assert.Contains("Duplicate **in** tag", result.ErrorMessages[0]);
    }

    [Fact]
    public void ProcessInTag_RevokesInTagNegation()
    {
        // Arrange
        string input = "-in: group";
        SearchParseResult result = new ();

        // Act
        ProcessInTag(input, result);

        // Assert
        Assert.Single(result.ErrorMessages);
        Assert.Contains("The **in** tag cannot be negated", result.ErrorMessages[0]);
    }

    [Fact]
    public void ProcessInTag_RevokesInTargetNegation()
    {
        // Arrange
        string input = "in: -fct";
        SearchParseResult result = new ();

        // Act
        ProcessInTag(input, result);

        // Assert
        Assert.Single(result.ErrorMessages);
        Assert.Contains("Negation should be applied to the key instead of value, but the **in** tag cannot be negated anyway", result.ErrorMessages[0]);
    }

    [Fact]
    public void ProcessInTag_DetectsInvalidTarget()
    {
        // Arrange
        string input = "in: flobber";
        SearchParseResult result = new ();

        // Act
        ProcessInTag(input, result);

        // Assert
        Assert.Single(result.ErrorMessages);
        Assert.Contains("**flobber** is not a valid table", result.ErrorMessages[0]);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("group")]
    [InlineData("step")]
    [InlineData("fct")]
    public void ProcessInTag_AcceptsValidTarget(string targetTable)
    {
        // Arrange
        string input = $"in: {targetTable}";
        SearchParseResult result = new ();

        // Act
        ProcessInTag(input, result);

        // Assert
        Assert.Empty(result.ErrorMessages);
    }

    #endregion

    #region ValidateAndProcessValue

    [Fact]
    public void ValidateAndProcessValue_WarnsValueNegation()
    {
        // Arrange
        SearchParseResult result = new () { CurrentType="step" };

        // Act
        ValidateAndProcessValue("step", "step", "-R-CV", result);

        // Assert
        Assert.Single(result.ErrorMessages);
        Assert.Contains("The value for **mode** started with a hyphen", result.ErrorMessages[0]);
    }

    [Fact]
    public void ValidateAndProcessValue_RestrictsSqlBlacklist()
    {
        // Arrange
        SearchParseResult result = new ();

        // Act
        ValidateAndProcessValue("barcode", "barcode", "\"A123' OR 1=1; DROP TABLE Users;--\"", result);

        // Assert
        Assert.Single(result.ErrorMessages);
        Assert.Contains("Security Issue:", result.ErrorMessages[0]);
    }

    [Theory]
    [InlineData("group", "flobber", "**flobber** is not a whole number.")]
    [InlineData("after", "flibber", "**flibber** (read as **1900-1-1**) is not a valid date or alias. Please use \"YYYY-MM-DD HH:mm:ss\" (ISO formatting) or a shortcut below.")]
    public void ValidateAndProcessValue_DetectsDatatypeMismatch(string key, string value, string expectedMessage)
    {
        // Arrange
        SearchParseResult result = new ();

        // Act
        ValidateAndProcessValue(key, key, value, result);

        // Assert
        Assert.Single(result.ErrorMessages);
        Assert.Contains(expectedMessage, result.ErrorMessages[0]);
    }

    #endregion
}
