using HiokiNL2SQLMark1.Services;
using System.Reflection;

namespace HiokiNL2SQL.Tests.Services;
public class DateParserTests
{
	private readonly MethodInfo _processDateValueInfo;
    private readonly MethodInfo _baseDateTimeFromAliasInfo;

    public DateParserTests()
    {
        // Access private methods via reflection
        _processDateValueInfo = typeof(SearchParserService).GetMethod("ProcessDateValue", BindingFlags.NonPublic | BindingFlags.Static)!;
        _baseDateTimeFromAliasInfo = typeof(SearchParserService).GetMethod("BaseDateTimeFromAlias", BindingFlags.NonPublic | BindingFlags.Static)!;
    }

    private DateTime? InvokeProcessDateValue(string key, string value, bool isInclusive) =>
        (DateTime?)_processDateValueInfo.Invoke(null, [key, value, isInclusive]);

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

    #region Inclusivity and Key Combinations

    [Theory]
    // AFTER (Start Boundary)
    [InlineData("after", "2026-02-20", true, "2026-02-20 00:00:00")]  // Inclusive: Start of day
    [InlineData("after", "2026-02-20", false, "2026-02-21 00:00:00")] // Exclusive: Start of NEXT day
    // BEFORE (End Boundary)
    [InlineData("before", "2026-02-20", true, "2026-02-20 23:59:59")]  // Inclusive: End of day (last tick)
    [InlineData("before", "2026-02-20", false, "2026-02-19 23:59:59")] // Exclusive: End of PREVIOUS day
    public void ProcessDateValue_HandlesInclusivityLogic(string key, string value, bool isInclusive, string expected)
    {
        var result = InvokeProcessDateValue(key, value, isInclusive);
        
        // Using a small tolerance for the "Last Tick" (-1 tick) logic
        var expectedDt = DateTime.Parse(expected);
        Assert.True(Math.Abs((result!.Value - expectedDt).TotalSeconds) < 1);
    }

    #endregion

    #region Value Types (Time vs Date vs Combined)

    [Fact]
    public void ProcessDateValue_SpecificTime_DisablesDateInclusivityNudge()
    {
        // When a specific time is provided, we don't jump whole days for exclusivity
        string val = "2026-02-20 08:30";
        
        // After + Exclusive + Specific Time should NOT jump to Feb 21
        var result = InvokeProcessDateValue("after", val, false);
        
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
        var result = InvokeProcessDateValue("after", "today shift2", true);
        
        var today = DateTime.Today;
        Assert.Equal(today.Date.AddHours(15), result);
    }

    [Fact]
    public void ProcessDateValue_ShiftAlias_HandlesExclusivity()
    {
        // Shift is 8 hours. After + Exclusive + Shift should jump 8 hours.
        var result = InvokeProcessDateValue("after", "shift1", false);
        var baseShift1 = InvokeBaseDateTimeFromAlias("shift1");
        
        Assert.Equal(baseShift1.AddHours(8), result);
    }

    #endregion

    #region Null/Empty Edge Cases

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ProcessDateValue_EmptyInput_ReturnsNull(string? input)
    {
        var result = InvokeProcessDateValue("after", input!, true);
        Assert.Null(result);
    }

    #endregion
}