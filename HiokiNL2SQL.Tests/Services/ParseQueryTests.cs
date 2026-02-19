using System.Diagnostics.CodeAnalysis;
using HiokiNL2SQLMark1.Services;
using HiokiNL2SQLMark1.Logic;

namespace HiokiNL2SQL.Tests.Services;
[ExcludeFromCodeCoverage]
public class SearchParserTests
{
    private readonly SearchParserService _parser = new();

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ParseQuery_ShouldHandleEmptyOrNullInput(string? input)
    {
        // Act
        var result = _parser.ParseQuery(input, "group");

        // Assert
        Assert.Empty(result.Filters);
        Assert.Empty(result.ErrorMessages);
        Assert.Equal("group", result.CurrentType);
        // Ensure GeneratePreview was called for the default state
        Assert.NotNull(result.Preview); 
    }

    [Fact]
    public void ParseQuery_ShouldHandleDuplicateInTags()
    {
        // Arrange
        string input = "in:fct in:group";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Equal("group", result.CurrentType);
        Assert.Contains(result.ErrorMessages, e => e.Contains("Duplicate **in** tag"));
    }

    [Fact]
    public void ParseQuery_ShouldCatchInvalidTableTarget()
    {
        // Arrange
        string input = "in:garbage_table barcode:A100";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Contains(result.ErrorMessages, e => e.Contains("is not a valid table"));
    }

    [Theory]
    [InlineData("random_prefix barcode:bar", "random_prefix", "barcode:bar", false)]
    [InlineData("barcode:bc sandwiched result:PASS", "sandwiched", "barcode:bc,result:PASS", false)]
    [InlineData("barcode:code a_sad_suffix", "a_sad_suffix", "barcode:code", false)]
    [InlineData("random_key: barcode:bar", "random_key:", "barcode:bar", true)]
    [InlineData("barcode:bc sandwiched_key: result:PASS", "sandwiched_key:", "barcode:bc,result:PASS", true)]
    [InlineData("barcode:code final_key:", "final_key:", "barcode:code", true)]
    public void ParseQuery_ShouldIdentifyMissingKeysAndValues(string input, string gap, string patternHits, bool isKey)
    {
        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Single(result.ErrorMessages);
        Assert.Contains(result.ErrorMessages, e => e.Contains(isKey ? $"Tag **{gap}** is missing a value." : $"Unrecognized filter without key: **{gap}**"));
        
        // Verify valid tags were still parsed
        foreach (var filterPart in patternHits.Split(','))
        {
            var kvp = filterPart.Split(':');
            Assert.Equal(kvp[1], result.Filters[kvp[0]].GetValue());
        }
    }

    [Theory]
    // Scenario 1: User puts hyphen on value instead of key
    [InlineData("barcode:-A123", "barcode", "A123", true, "This search is now **-barcode:A123...**")]
    // Scenario 2: Literal hyphen in quotes should NOT trigger auto-negation
    [InlineData("barcode:\"-A123\"", "barcode", "-A123", false, "")] 
    // Scenario 3: Double negation (hyphen on both) should probably just result in negation
    [InlineData("-barcode:-A123", "barcode", "A123", true, "This search is now **-barcode:A123...**")]
    public void ParseQuery_ShouldHandleHyphenOnValue(string input, string key, string expectedValue, bool expectedNegation, string expectedErrorSnippet)
    {
        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.True(result.Filters.TryGetValue(key, out var filter));
        Assert.Equal(expectedValue, filter.GetValue());
        Assert.Equal(expectedNegation, filter.IsNegated);

        if (!string.IsNullOrEmpty(expectedErrorSnippet))
        {
            Assert.Contains(result.ErrorMessages, e => e.Contains(expectedErrorSnippet));
        }
    }

    [Theory]
    [InlineData("-barcode:A123", "barcode", "A123")]
    [InlineData("-result:FAIL", "result", "FAIL")]
    [InlineData("-group:1", "group", 1)]
    [InlineData("in:group -comp:UN-T", "comp", "UN-T")]
    [InlineData("in:step -step:5", "step", 5)]
    public void ParseQuery_ShouldHandleNegativeTags(string input, string expectedKey, object expectedValue) // use object to allow any datatype
    {
        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Empty(result.ErrorMessages);
        Assert.True(result.Filters.TryGetValue(expectedKey, out var filter), $"Filter should contain {expectedKey}");
        Assert.True(filter.IsNegated, "Filter should be marked as negated");
        Assert.Equal(expectedValue, filter.GetValue());
    }

    [Fact]
    public void ParseQuery_ShouldCatchSQLInjectionKeywords()
    {
        // Arrange
        string input = "barcode:\"A123' OR 1=1; DROP TABLE Users;--\"";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Contains(result.ErrorMessages, e => e.Contains("Security Issue"));
        // Ensure the dangerous filter wasn't added
        Assert.False(result.Filters.ContainsKey("barcode"));
    }

    [Fact]
    public void ParseQuery_ShouldCatchUnrecognizedTags()
    {
        // Arrange
        string input = "barcode:A123 unknownTag:value";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Contains(result.ErrorMessages, e => e.Contains("The tag **unknowntag** wasn't recognized"));
    }

    [Theory]
    [InlineData("group:NotANumber")]
    [InlineData("before:InvalidDate")]
    public void ParseQuery_ShouldCatchInvalidValueTypes(string input)
    {
        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Contains(result.ErrorMessages, e => e.Contains("Invalid value for"));
    }

    [Fact]
    public void ParseQuery_ShouldCatchMismatchedTagsForMode()
    {
        // Arrange
        // 'comp' is a Group tag, but we are searching 'fct'
        string input = "in:fct comp:UN-T";

        // Act
        var result = _parser.ParseQuery(input, "all"); 

        // Assert
        Assert.Contains(result.ErrorMessages, e => e.Contains("is not available when searching **fct**"));
    }

    [Fact]
    public void ParseQuery_ShouldCatchInvalidNegatedTag()
    {
        // Arrange - 'part' is not available in 'group' mode
        string input = "in:group -part:123-456";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Contains(result.ErrorMessages, e => e.Contains("The tag **-part:** is not available when searching **group**"));
    }

    [Fact]
    public void ParseQuery_ShouldHandleDuplicateTagsWithWarning()
    {
        // Arrange
        string input = "barcode:A100 barcode:B200";
        
        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Contains(result.ErrorMessages, e => e.Contains("Duplicate tag detected"));
        Assert.Equal("B200", result.Filters["barcode"].GetValue());
    }

    [Fact]
    public void ParseQuery_ShouldNotAllowPositiveAndNegativeOfSameTag()
    {
        // Arrange
        string input = "barcode:A123 -barcode:B456";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Contains(result.ErrorMessages, e => e.Contains("Duplicate tag detected: **-barcode:**"));
        Assert.Equal("B456", result.Filters["barcode"].GetValue());
    }

    [Theory]
    [InlineData("-in:fct", "in")]
    [InlineData("-after:2025-01-01", "after")]
    [InlineData("-before:2025-01-01", "before")]
    public void ParseQuery_ShouldCatchNegatedNonNegatableTag(string input, string key)
    {
        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        // The parser should catch that these tags cannot be negated
        Assert.Contains(result.ErrorMessages, e => e.Contains($"The **{key}** tag cannot be negated"));
        // It should then perform the non-negated action (fallback logic)
        if(key == "in")
        {
            Assert.Equal("fct", result.CurrentType);
        } else
        {
            Assert.True(result.Filters.ContainsKey(key));
            Assert.False(result.Filters[key].IsNegated, $"The **{key}** tag should have had negation revoked.");
        }
    }

    [Fact]
    public void ParseQuery_DateSwap_ForMismatchedBeforeAfter()
    {
        // Testing that "after" > "before" triggers a swap
        string input = "after:2025-01-01 before:2024-01-01";

        var result = _parser.ParseQuery(input, "all");

        var after = result.Filters["after"] as Filter<DateTime?>;
        var before = result.Filters["before"] as Filter<DateTime?>;

        Assert.True(after.Value < before.Value); 
        Assert.Contains(result.ErrorMessages, e => e.Contains("Your start date is after your end date"));
    }

    [Fact]
    public void ParseQuery_DateSwap_ShouldNotTriggerOnEqualDates()
    {
        string input = "after:2025-01-01 before:2025-01-01";
        var result = _parser.ParseQuery(input, "all");

        Assert.DoesNotContain(result.ErrorMessages, e => e.Contains("start date is after"));
    }

    [Fact]
    public void ParseQuery_ShouldCatchMultipleDifferentErrors()
    {
        // Arrange: 
        // 1. Invalid 'in' target
        // 2. Unrecognized tag 'boom'
        // 3. SQL injection in 'barcode'
        string input = "in:invalidTable boom:value barcode:\"DROP TABLE\"";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Equal(3, result.ErrorMessages.Count);
        Assert.Contains(result.ErrorMessages, e => e.Contains("not a valid table"));
        Assert.Contains(result.ErrorMessages, e => e.Contains("**boom** wasn't recognized"));
        Assert.Contains(result.ErrorMessages, e => e.Contains("Security Issue"));
    }

    [Fact]
    public void ParseQuery_ShouldCatchContextAndFormattingErrorsTogether()
    {
        // Arrange:
        // 1. Mismatched tag for mode (comp: is for groups, but we are in fct)
        // 2. Trailing garbage text (malformed input)
        string input = "in:fct comp:UN-T unexpected_junk";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Equal(2, result.ErrorMessages.Count);
        Assert.Contains(result.ErrorMessages, e => e.Contains("not available when searching **fct**"));
        Assert.Contains(result.ErrorMessages, e => e.Contains("Unrecognized filter without key"));
    }

    [Fact]
    public void ParseQuery_ShouldHandleDuplicateTagsAndInvalidTagsSimultaneously()
    {
        // Arrange:
        // 1. Duplicate barcode tags
        // 2. An invalid tag 'xyz'
        string input = "barcode:A1 barcode:A2 xyz:123";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Equal(2, result.ErrorMessages.Count);
        Assert.Contains(result.ErrorMessages, e => e.Contains("Duplicate tag detected"));
        Assert.Contains(result.ErrorMessages, e => e.Contains("**xyz** wasn't recognized"));
        
        // Verify that logic still preserved the last valid value despite errors elsewhere
        Assert.Equal("A2", result.Filters["barcode"].GetValue());
    }

    [Fact]
    public void ParseQuery_ShouldHandleQuotesAndCaseInsensitivity()
    {
        // Arrange
        string input = "BARCODE:\"Part Number 123\"";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.True(result.Filters.ContainsKey("barcode"));
        Assert.Equal("Part Number 123", result.Filters["barcode"].GetValue());
    }
}