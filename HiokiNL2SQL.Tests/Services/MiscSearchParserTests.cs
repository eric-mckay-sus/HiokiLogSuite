using HiokiNL2SQLMark1.Services;

namespace HiokiNL2SQL.Tests.Services;
public class MiscSearchParserServiceTests
{
    
    [Theory]
    [InlineData("today", true, true)]   // before:today (inclusive) -> End of today
    [InlineData("today", false, true)]  // after:today (inclusive)  -> Start of today
    [InlineData("yesterday", true, true)] // before:yesterday (inclusive) -> End of yesterday
    public void TranslateDateAlias_StandardAliases_ResolveToExpectedBoundaries(string alias, bool isBefore, bool isInclusive)
    {
        // Act
        var result = SearchParserService.TranslateDateAlias(alias, false, isBefore, isInclusive);
        DateTime parsed = DateTime.Parse(result);

        // Assert
        if (alias == "today" && isBefore)
        {
            // Should be 23:59:59 of the current day
            Assert.Equal(DateTime.Today.AddDays(1).AddTicks(-1).ToString("yyyy-MM-dd HH:mm:ss"), result);
        }
        else if (alias == "today" && !isBefore)
        {
            // Should be 00:00:00 of the current day
            Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss"), result);
        }
    }

    [Fact]
    public void TranslateDateAlias_ExclusiveInversion_SwapsBoundaries()
    {
        // Arrange: before:today (exclusive) should behave like after:today (inclusive) 
        // essentially setting the boundary to the START of the day.
        bool isBefore = true;
        bool isInclusive = false;

        // Act
        var result = SearchParserService.TranslateDateAlias("today", false, isBefore, isInclusive);

        // Assert
        Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss"), result);
    }

    [Theory]
    [InlineData("shift1", "07:00:00")]
    [InlineData("shift2", "15:00:00")]
    public void TranslateDateAlias_ShiftTimePart_ReturnsCorrectTimeOnly(string alias, string expectedTime)
    {
        // Act
        var result = SearchParserService.TranslateDateAlias(alias, true, false, true);

        // Assert
        Assert.Equal(expectedTime, result);
    }

    [Theory]
    [InlineData("all", "barcode")]   // Universal
    [InlineData("group", "comp")]    // Group specific
    [InlineData("step", "part")]     // Step specific
    public void GetSupportedKeysThisMode_ReturnsCorrectKeysForContext(string mode, string expectedKey)
    {
        // Act
        var keys = SearchParserService.GetSupportedKeysThisMode(mode);

        // Assert
        Assert.Contains(expectedKey, keys);
    }

    [Fact]
    public void GetTagTooltip_IncludesScope_WhenRequested()
    {
        // Act
        var tooltip = SearchParserService.GetTagTooltip("comp", showScope: true);

        // Assert
        Assert.Contains("Works in: Group", tooltip);
    }

    [Fact]
    public void GetTagTooltip_Fallback_HandlesUnknownKey()
    {
        // Act
        var tooltip = SearchParserService.GetTagTooltip("mysterykey", showScope: false);

        // Assert
        Assert.Equal("Filter by mysterykey", tooltip);
    }
}