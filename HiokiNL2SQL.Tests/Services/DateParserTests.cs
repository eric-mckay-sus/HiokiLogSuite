using Parser = HiokiNL2SQLMark1.Services.SearchParserService;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace HiokiNL2SQL.Tests.Services;
[ExcludeFromCodeCoverage]
public class DateParserTests
{
    private readonly MethodInfo _baseDateTimeFromAliasInfo;

    public DateParserTests()
    {
        // Access private method via reflection
        _baseDateTimeFromAliasInfo = typeof(Parser).GetMethod("BaseDateTimeFromAlias", BindingFlags.NonPublic | BindingFlags.Static)!;
    }

    private DateTime InvokeBaseDateTimeFromAlias(string alias) =>
        (DateTime)_baseDateTimeFromAliasInfo.Invoke(null, [alias])!;

    #region BaseDateTimeFromAlias Tests

    [Theory]
    [InlineData("today")]
    [InlineData("yesterday")]
    [InlineData("last24h")]
    [InlineData("2026-01-01")]
    public void BaseDateTimeFromAlias_RecognizesFormats(string alias)
    {
        var result = InvokeBaseDateTimeFromAlias(alias);
        Assert.NotEqual(DateTime.MinValue, result);
    }

    [Fact]
    public void BaseDateTimeFromAlias_InvalidValue_ReturnsMinValue()
    {
        var result = InvokeBaseDateTimeFromAlias("not-a-date");
        Assert.Equal(DateTime.MinValue, result);
    }

    #endregion

    #region Value Types (Time vs Date vs Combined)

    [Fact]
    public void ProcessDateValue_SpecificTime_DisablesDateInclusivityNudge()
    {
        // When a specific time is provided, we don't jump whole days for exclusivity
        string val = "2026-02-20 08:30";
        
        // After + Exclusive + Specific Time should NOT jump to Feb 21
        var result = Parser.ProcessDateValue("after", val, false);
        
        Assert.Equal(2026, result!.Value.Year);
        Assert.Equal(2, result!.Value.Month);
        Assert.Equal(20, result!.Value.Day);
        Assert.Equal(8, result!.Value.Hour);
    }

    [Fact]
    public void ProcessDateValue_CombinedAlias_WorksCorrectly()
    {
        // Test "today shift2" (Date Alias + Time Alias)
        // Shift 2 starts at 15:00
        var result = Parser.ProcessDateValue("after", "today shift2", true);
        
        var today = DateTime.Today;
        Assert.Equal(today.Date.AddHours(15.5), result);
    }

    [Fact]
    public void ProcessDateValue_ShiftAlias_HandlesExclusivity()
    {
        // Shift is 8 hours. After + Exclusive + Shift should jump 8.5 hours.
        var result = Parser.ProcessDateValue("after", "shift1", false);
        var baseShift1 = InvokeBaseDateTimeFromAlias("shift1");
        
        Assert.Equal(baseShift1.AddHours(8.5), result);
    }

    #endregion

    #region Null/Empty Edge Cases

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ProcessDateValue_EmptyInput_ReturnsNull(string? input)
    {
        var result = Parser.ProcessDateValue("after", input!, true);
        Assert.Null(result);
    }

    #endregion

    #region Shift Logic and Future Correction

    [Fact]
    public void ProcessDateValue_ShiftAlias_AutoCorrectsFutureToYesterday()
    {
        // Setup: If it's currently morning, shift2 (15:30) is in the future.
        // The logic should subtract 1 day so 'after:shift2' shows yesterday's shift.
        var result = Parser.ProcessDateValue("after", "shift2", true);
        
        if (DateTime.Now.TimeOfDay < new TimeSpan(15, 30, 0))
        {
            Assert.True(result < DateTime.Today, "Should have shifted to yesterday because shift hasn't started yet.");
        }
    }

    [Fact]
    public void ProcessDateValue_Shift3_AppliesNegativeDateOffset()
    {
        // Shift 3 starts at 22:30 (-1.5 hours from midnight)
        // 'today shift3' should result in Yesterday at 22:30
        var result = Parser.ProcessDateValue("after", "today shift3", true);
        var expected = DateTime.Today.AddDays(-1).Add(new TimeSpan(22, 30, 0));
        
        Assert.Equal(expected, result);
    }

    #endregion

    #region Inclusivity and "Today" Safety

    [Theory]
    // Verification that the 'before:today' inclusive bug is fixed
    [InlineData("before", "today", true)] 
    public void ProcessDateValue_BeforeTodayInclusive_IsEndOfToday(string key, string value, bool inclusive)
    {
        var result = Parser.ProcessDateValue(key, value, inclusive);
        var expectedEndofToday = DateTime.Today.AddDays(1).AddTicks(-1);

        // It should be the very last tick of today, NOT yesterday.
        Assert.Equal(expectedEndofToday, result);
        Assert.True(result > DateTime.Now.Date); 
    }

    [Theory]
    [InlineData("after", "2026-02-20", true, "2026-02-20 00:00:00")]
    [InlineData("after", "2026-02-20", false, "2026-02-21 00:00:00")]
    [InlineData("before", "2026-02-20", true, "2026-02-20 23:59:59")]
    [InlineData("before", "2026-02-20", false, "2026-02-19 23:59:59")]
    public void ProcessDateValue_HandlesInclusivityLogic(string key, string value, bool isInclusive, string expected)
    {
        var result = Parser.ProcessDateValue(key, value, isInclusive);
        var expectedDt = DateTime.Parse(expected);
        
        Assert.True(Math.Abs((result!.Value - expectedDt).TotalSeconds) < 1);
    }

    #endregion

    #region Edge Cases and Combined Aliases

    [Fact]
    public void ProcessDateValue_InvalidAlias_ReturnsMinValue()
    {
        var result = Parser.ProcessDateValue("after", "not-a-date", true);
        Assert.Equal(DateTime.MinValue, result);
    }

    [Fact]
    public void ProcessDateValue_Last24h_IgnoresInclusivityNudge()
    {
        // last24h uses 'now', so it shouldn't be nudged by day/shift offsets
        var result = Parser.ProcessDateValue("after", "last24h", true);
        var expected = DateTime.Now.AddHours(-24);

        // Tolerance of 2 seconds for execution time
        Assert.True(Math.Abs((result!.Value - expected).TotalSeconds) < 2);
    }

    #endregion
}