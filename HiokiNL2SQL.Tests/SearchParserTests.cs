using HiokiNL2SQLMark1;
public class SearchParserTests
{
    private readonly SearchParserService _parser = new();

    [Fact]
    public void ParseQuery_ShouldCatchUnrecognizedTags()
    {
        // Arrange
        string input = "barcode:A123 unknownTag:value";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Contains(result.ErrorMessages, e => e.Contains("The tag 'unknowntag' wasn't recognized"));
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
        // Ensure the dangerous filter wasn't added to the dictionary
        Assert.False(result.Filters.ContainsValue(input.Split(":")[1]));
        Assert.False(result.Filters.ContainsKey("barcode"));
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

    [Fact]
    public void ParseQuery_ShouldCatchMismatchedTagsForMode()
    {
        // Arrange
        // 'comp' is a Group tag, but we are searching 'fct'
        string input = "in:fct comp:Resistor";

        // Act
        var result = _parser.ParseQuery(input, "all"); 

        // Assert
        Assert.Contains(result.ErrorMessages, e => e.Contains("is not available when searching 'fct'"));
    }

    [Theory]
    [InlineData("barcode: ")] // Trailing colon with no value
    [InlineData("barcode:A100 some_random_text")] // Text without a key
    public void ParseQuery_ShouldCatchMalformedInput(string input)
    {
        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.NotEmpty(result.ErrorMessages);
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
        Assert.Equal("B200", result.Filters["barcode"]); // Should keep the last one
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
        Assert.Contains(result.ErrorMessages, e => e.Contains("'boom' wasn't recognized"));
        Assert.Contains(result.ErrorMessages, e => e.Contains("Security Issue"));
    }

    [Fact]
    public void ParseQuery_ShouldCatchContextAndFormattingErrorsTogether()
    {
        // Arrange:
        // 1. Mismatched tag for mode (comp: is for groups, but we are in fct)
        // 2. Trailing garbage text (malformed input)
        string input = "in:fct comp:Resistor unexpected_junk";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Equal(2, result.ErrorMessages.Count);
        Assert.Contains(result.ErrorMessages, e => e.Contains("not available when searching 'fct'"));
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
        Assert.Contains(result.ErrorMessages, e => e.Contains("'xyz' wasn't recognized"));
        
        // Verify that logic still preserved the last valid value despite errors elsewhere
        Assert.Equal("A2", result.Filters["barcode"]);
    }
    
    [Fact]
    public void ParseQuery_ShouldIdentifyGapsBetweenValidTags()
    {
        // Arrange: "middle_junk" is between two valid tags
        string input = "barcode:A100 middle_junk result:PASS";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Single(result.ErrorMessages);
        Assert.Contains(result.ErrorMessages, e => e.Contains("Unrecognized input: 'middle_junk'"));
        
        // Verify valid tags were still parsed
        Assert.Equal("A100", result.Filters["barcode"]);
        Assert.Equal("PASS", result.Filters["result"]);
    }

    [Fact]
    public void ParseQuery_ShouldHandleNegativeTags()
    {
        // Arrange
        string input = "-barcode:A123 -result:FAIL";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Empty(result.ErrorMessages);
        Assert.True(result.Filters.ContainsKey("-barcode"));
        Assert.True(result.Filters.ContainsKey("-result"));
        Assert.Equal("A123", result.Filters["-barcode"]);
        Assert.Equal("FAIL", result.Filters["-result"]);
    }

    [Fact]
    public void ParseQuery_ShouldCatchNegatedInTag()
    {
        // Arrange
        string input = "-in:fct barcode:A123";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        // The parser should catch that 'in' cannot be negated
        Assert.Contains(result.ErrorMessages, e => e.Contains("The 'in' tag cannot be negated"));
        // It should still set the type correctly (fallback logic)
        Assert.Equal("fct", result.CurrentType);
    }

    [Fact]
    public void ParseQuery_ShouldNotAllowPositiveAndNegativeOfSameTag()
    {
        // Arrange
        string input = "barcode:A123 -barcode:B456";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Contains(result.ErrorMessages, e => e.Contains("Duplicate tag detected: '-barcode:'"));
        Assert.Equal("B456", result.Filters["-barcode"]);
    }

    [Fact]
    public void ParseQuery_ShouldGenerateCorrectNegativePreview()
    {
        // Arrange
        string input = "-barcode:A123";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        // Verify the Preview string (Markdown included)
        Assert.Contains("**BARCODE** is  NOT  'A123'", result.Preview);
    }

    [Fact]
    public void ParseQuery_ShouldCatchInvalidNegatedTag()
    {
        // Arrange - 'part' is not available in 'group' mode
        string input = "in:group -part:123-456";

        // Act
        var result = _parser.ParseQuery(input, "all");

        // Assert
        Assert.Contains(result.ErrorMessages, e => e.Contains("The tag '-part:' is not available when searching 'group'"));
    }
}