using System.Diagnostics.CodeAnalysis;
using HiokiNL2SQLMark1;
using HiokiNL2SQLMark1.Logic;

namespace HiokiNL2SQL.Tests.Logic;
[ExcludeFromCodeCoverage]
public class TestLogRecord : IHiokiLog
{
    public int Id { get; set; } // To help EF Core bc we don't necessarily have the info to uniquely identify
    public string? Barcode { get; set; }
    public DateTime? Time { get; set; }
    public int? Group { get; set; }
    public string? Result { get; set; }
}

[ExcludeFromCodeCoverage]
public class TestStepFct : TestLogRecord, IStepFCT
{
    public int? Step { get; set; }
    public string? Position { get; set; }
    public string? Mode { get; set; }
    public double? HighLim { get; set; }
    public double? LowLim { get; set; }
    public char? MeasurementValue { get; set; }
    public double? RefVal { get; set; }
    public double? MeasVal { get; set; }
}

/// <summary>
/// Covers all code in LogTableLogic except JSInterop & NavigationManager features of SaveToCSV and HandleBarcodeClick (and simple helpers)
/// </summary>
[ExcludeFromCodeCoverage]
public class LogTableLogicTests
{
    /// <summary>
    /// Builds a list of data to be used for testing
    /// </summary>
    /// <param name="includeDates">Whether to include date-bound filters</param>
    /// <returns></returns>
    public static TheoryData<string, IFilter, object> GetTestFilters(bool includeDates)
    {
        var data = new TheoryData<string, IFilter, object>
        {
            { "barcode", new Filter<string?>("barcode", "123"), "123" },
            { "result", new Filter<string?>("result", "PASS"), "PASS" },
            { "group", new Filter<int?>("group", 7), 7 }
        };

        if (includeDates)
        {
            data.Add("after", new Filter<DateTime?>("after", new DateTime(2024, 1, 1)), new DateTime(2024, 1, 1));
            data.Add("before", new Filter<DateTime?>("before", new DateTime(2025, 1, 1)), new DateTime(2025, 1, 1));
        }

        return data;
    }

    public static readonly TheoryData<string, IFilter, object> TestFiltersNoDates = GetTestFilters(false);
    public static readonly TheoryData<string, IFilter, object> TestFilters = GetTestFilters(true);

    [Fact]
    public void GetFilterStateHash_IsCaseAndOrderInsensitive()
    {
        var logic = TestLogicFactory.CreateLogic(new List<TestLogRecord>());
        
        var dict1 = new Dictionary<string, IFilter> { 
            { "barcode", new Filter<string?>("barcode", "A123") },
            { "result", new Filter<string?>("result", "PASS") }
        };
        var dict2 = new Dictionary<string, IFilter> { 
            { "RESULT", new Filter<string?>("result", "pass") },
            { "barcode", new Filter<string?>("barcode", "a123") }
        };

        Assert.Equal(logic.GetFilterStateHash(dict1), logic.GetFilterStateHash(dict2));
    }

    [Fact]
    public void IsStale_RespectsOverride()
    {
        var logic = TestLogicFactory.CreateLogic(new List<TestLogRecord>());
        logic.IsStaleOverride = () => true; 
        
        // Even if hashes match, it should be stale because of the override
        Assert.True(logic.IsStale);
    }

    [Fact]
    public async Task RefreshData_KeepPageFalse_ResetsToPageOne()
    {
        // Arrange
        var logic = TestLogicFactory.CreateLogic(new List<TestLogRecord>());
        logic.CurrentPage = 10;

        // Act
        await logic.RefreshData(keepPage: false);

        // Assert
        Assert.Equal(1, logic.CurrentPage);
    }

    [Fact]
    public async Task RefreshData_KeepPageTrue_PersistsCurrentPage()
    {
        // Arrange
        var logic = TestLogicFactory.CreateLogic(new List<TestLogRecord>());
        logic.CurrentPage = 10;
        // TotalPages must be high enough or it might be clamped (though logic doesn't clamp in RefreshData)
        logic.TotalCount = 200; 

        // Act
        await logic.RefreshData(keepPage: true);

        // Assert
        Assert.Equal(10, logic.CurrentPage);
    }

    [Theory]
    [MemberData(nameof(TestFiltersNoDates))]
    public async Task ApplyFilters_Positive_FiltersBy(string key, IFilter filter, object expectedMatch)
    {
        // Arrange
        var data = new List<TestLogRecord> {
            new() { Barcode = "123", Result = "PASS", Group = 7, Time = DateTime.Now },
            new() { Barcode = "789", Result = "FAIL", Group = 9, Time = DateTime.Now }
        };
        var logic = TestLogicFactory.CreateLogic(data);

        // Act: Set the specific filter based on the key
        logic.Filters[key].CopyFrom(filter);
        await logic.RefreshData();

        // Assert
        Assert.Single(logic.DataView);
        var actualValue = typeof(TestLogRecord).GetProperty(char.ToUpper(key[0]) + key[1..])? // Use reflection to get the correct attribute
                        .GetValue(logic.DataView[0]);
    
        Assert.Equal(expectedMatch, actualValue);
    }

    [Theory]
    [MemberData(nameof(TestFiltersNoDates))]
    public async Task ApplyFilters_Negated_FiltersOut(string key, IFilter filter, object expectedNegativeMatch)
    {
        // Arrange
        var data = new List<TestLogRecord> {
            new() { Barcode = "123", Result = "PASS", Group = 7, Time = DateTime.Now },
            new() { Barcode = "789", Result = "FAIL", Group = 777, Time = DateTime.Now }
        };
        var logic = TestLogicFactory.CreateLogic(data);

        // Act: Set the specific filter based on the key
        var target = logic.Filters[key];
        target.CopyFrom(filter);
        target.IsNegated = true; // Ensure it is negated for this test
        await logic.RefreshData();

        // Assert
        Assert.Single(logic.DataView);
        var excludeValue = typeof(TestLogRecord).GetProperty(char.ToUpper(key[0]) + key[1..])? // Use reflection to get the correct attribute
                        .GetValue(logic.DataView[0]);
    
        Assert.NotEqual(expectedNegativeMatch, excludeValue);
    }

    [Fact]
    public async Task ApplyFilters_BeforeDate_AtMidnight_IsInclusiveOfThatDay()
    {
        // Arrange: User enters "before:2024-01-01" which parses to midnight
        var targetDate = new DateTime(2024, 1, 1, 0, 0, 0); 
        var data = new List<TestLogRecord> {
            new() { Time = new DateTime(2024, 1, 1, 10, 0, 0), Barcode = "Morning" },
            new() { Time = new DateTime(2024, 1, 1, 23, 59, 59), Barcode = "Night" },
            new() { Time = new DateTime(2024, 1, 2, 0, 0, 1), Barcode = "NextDay" }
        };
        var logic = TestLogicFactory.CreateLogic(data);
        logic.Filters["before"] = new Filter<DateTime?>("before", targetDate);

        // Act
        await logic.RefreshData();

        // Assert
        // Logic should add 1 day and use < (less than), capturing all of Jan 1st
        Assert.Equal(2, logic.DataView.Count);
        Assert.DoesNotContain(logic.DataView, x => x.Barcode == "NextDay");
    }

    [Fact]
    public async Task ApplyFilters_BeforeDate_WithSpecificTime_IsHardStop()
    {
        // Arrange: User enters "before:2024-01-01 12:00"
        var targetDate = new DateTime(2024, 1, 1, 12, 0, 0);
        var data = new List<TestLogRecord> {
            new() { Time = new DateTime(2024, 1, 1, 11, 59, 0), Barcode = "BeforeNoon" },
            new() { Time = new DateTime(2024, 1, 1, 12, 0, 1), Barcode = "AfterNoon" }
        };
        var logic = TestLogicFactory.CreateLogic(data);
        logic.Filters["before"] = new Filter<DateTime?>("before", targetDate);

        // Act
        await logic.RefreshData();

        // Assert
        // Logic should use <= targetDate without adding a day
        Assert.Single(logic.DataView);
        Assert.Equal("BeforeNoon", logic.DataView[0].Barcode);
    }

    [Fact]
    public async Task ApplyFilters_AfterDate_IsInclusive()
    {
        // Arrange
        var start = new DateTime(2024, 1, 1, 12, 0, 0);
        var data = new List<TestLogRecord> {
            new() { Time = start.AddSeconds(-1), Barcode = "TooEarly" },
            new() { Time = start, Barcode = "ExactlyOnTime" }
        };
        var logic = TestLogicFactory.CreateLogic(data);
        logic.Filters["after"] = new Filter<DateTime?>("after", start);

        // Act
        await logic.RefreshData();

        // Assert
        Assert.Single(logic.DataView);
        Assert.Equal("ExactlyOnTime", logic.DataView[0].Barcode);
    }

    [Theory]
    [MemberData(nameof(TestFilters))]
    public async Task DictionaryToFilters_MapsNewFilterAndClearsAllOthers(string key, IFilter newFilter, object expectedValue)
    {
        // Arrange
        var logic = TestLogicFactory.CreateLogic(new List<TestLogRecord>());
        
        // Fill all filters with "dirty" data to ensure ResetFilterState() is working
        logic.Filters["barcode"] = new Filter<string?>("barcode", "DIRTY");
        logic.Filters["result"] = new Filter<string?>("result", "DIRTY");
        logic.Filters["group"] = new Filter<int?>("group", -1);
        logic.Filters["after"] = new Filter<DateTime?>("after", DateTime.MinValue);
        logic.Filters["before"] = new Filter<DateTime?>("before", DateTime.MinValue);

        var filterDict = new Dictionary<string, IFilter> { { key, newFilter } };

        // Act
        await logic.DictionaryToFilters(filterDict);

        // Assert: Verify the target filter was mapped correctly
        object? actualValue = key switch
        {
            "barcode" => logic.Filters["barcode"].GetValue(),
            "result"  => logic.Filters["result"].GetValue(),
            "group"   => logic.Filters["group"].GetValue(),
            "after"   => logic.Filters["after"].GetValue(),
            "before"  => logic.Filters["before"].GetValue(),
            _ => throw new ArgumentException("Unknown key")
        };
        Assert.Equal(expectedValue, actualValue);

        // Assert: Verify that a filter other than current was cleared
        if (key != "result")
        {
            Assert.Null(logic.Filters["result"].GetValue());
            Assert.False(logic.Filters["result"].IsActive);
        }
        else
        {
            Assert.Null(logic.Filters["barcode"].GetValue());
            Assert.False(logic.Filters["barcode"].IsActive);
        }
    }

    [Fact]
    public async Task ApplySorting_DefaultsToTimeDescending()
    {
        // Arrange
        var data = new List<TestLogRecord> {
            new() { Group = 1, Time = new DateTime(2026, 12, 12) },
            new() { Group = 3, Time = new DateTime(2023, 11, 1) },
            new() { Group = 2, Time = new DateTime(2025, 1, 12) }
        };
        var logic = TestLogicFactory.CreateLogic(data);
        
        // Don't toggle any sort

        // Act
        await logic.RefreshData();

        // Assert
        Assert.Equal(new DateTime(2026, 12, 12), logic.DataView[0].Time);
        Assert.Equal(new DateTime(2023, 11, 1), logic.DataView[2].Time);
    }

    [Fact]
    public async Task ApplySorting_SortsByGroupDescending()
    {
        // Arrange
        var data = new List<TestLogRecord> {
            new() { Group = 1, Time = DateTime.Now },
            new() { Group = 3, Time = DateTime.Now },
            new() { Group = 2, Time = DateTime.Now }
        };
        var logic = TestLogicFactory.CreateLogic(data);
        
        // Set state to Sort Descending on "Group"
        await logic.ToggleSort("Group"); // Asc
        await logic.ToggleSort("Group"); // Desc

        // Act
        await logic.RefreshData();

        // Assert
        Assert.Equal(3, logic.DataView[0].Group);
        Assert.Equal(1, logic.DataView[2].Group);
    }

    [Fact]
    public async Task HandleBarcodeClick_UsesTrigger_WhenUIBound()
    {
        var logic = TestLogicFactory.CreateLogic(new List<TestLogRecord>());
        string? triggeredQuery = null;
        logic.OnNotifyUI = () => { }; // Simulate bound UI
        logic.TriggerPowerSearch = (q) => triggeredQuery = q;

        logic.HandleBarcodeClick("ABC");

        Assert.Equal("in:all barcode:ABC", triggeredQuery);
    }

    [Fact]
    public async Task ChangePage_ValidPage_UpdatesCurrentPageAndReloadsData()
    {
        // Arrange
        // Create 15 records. Assuming PageSize is 5, this creates 3 pages.
        var data = Enumerable.Range(1, 15)
            .Select(i => new TestLogRecord { Id = i, Barcode = $"Item{i}", Time = new DateTime(2026, 1, 16-i) }) // day as 16-i to put item numbers in ascending order
            .ToList();
        
        var logic = TestLogicFactory.CreateLogic(data);
        logic.PageSize = 5;

        // Initial load to establish TotalPages/TotalCount
        await logic.RefreshData(); 
        Assert.Equal(1, logic.CurrentPage);
        Assert.Equal("Item1", logic.DataView[0].Barcode);

        // Act
        await logic.ChangePage(2);

        // Assert
        Assert.Equal(2, logic.CurrentPage);
        // Verify we are seeing the second "slice" of data (Items 6-10)
        Assert.Equal(5, logic.DataView.Count);
        Assert.Equal("Item6", logic.DataView[0].Barcode);
    }

    [Fact]
    public async Task ChangePage_InvalidPage_DoesNothing()
    {
        // Arrange
        var data = new List<TestLogRecord> { new() { Id = 1 } };
        var logic = TestLogicFactory.CreateLogic(data);
        logic.PageSize = 5;
        
        await logic.RefreshData(); // TotalPages will be 1
        logic.CurrentPage = 1;

        // Act
        // Attempt to go to page 2 when only 1 page exists
        await logic.ChangePage(2);

        // Assert
        Assert.Equal(1, logic.CurrentPage);
    }

    [Fact]
    public async Task ToggleSort_CyclesThroughDirections()
    {
        // Arrange
        var logic = TestLogicFactory.CreateLogic(new List<TestLogRecord>());
        string col = "Barcode";

        // Act 1: None -> Asc
        await logic.ToggleSort(col);
        Assert.Equal(col, logic.CurrentSortColumn);
        Assert.Equal("▲", logic.GetSortIcon(col));

        // Act 2: Asc -> Desc
        await logic.ToggleSort(col);
        Assert.Equal("▼", logic.GetSortIcon(col));

        // Act 3: Desc -> None
        await logic.ToggleSort(col);
        Assert.Equal("", logic.CurrentSortColumn);
        Assert.Equal("↕", logic.GetSortIcon(col));
    }

    [Fact]
    public async Task InitializeCaches_OnlyRunsForStepFCTTypes()
    {
        // Arrange - Scenario 1: Standard Log
        var logicStandard = TestLogicFactory.CreateLogic(new List<TestLogRecord>());
        
        // Act
        await logicStandard.InitializeCaches();

        // Assert
        Assert.Empty(logicStandard.modeCache);

        // Arrange - Scenario 2: Step Log
        var stepData = new List<TestStepFct> { new() { Mode = "C-CV" }, new() { Mode = "R-AC160" } };
        var logicStep = TestLogicFactory.CreateLogic(stepData);

        // Act
        await logicStep.InitializeCaches();

        // Assert
        Assert.Contains("C-CV", logicStep.modeCache);
        Assert.Equal(2, logicStep.modeCache.Count);
    }

    [Fact]
    public void ResetFilterState_KeepsDataButClearsInputs()
    {
        // Arrange
        var logic = TestLogicFactory.CreateLogic(new List<TestLogRecord>());
        var record = new TestLogRecord { Id = 1 };
        logic.DataView = [record];
        logic.Filters["result"].SetValue("PASS");
        logic.CurrentPage = 3;

        // Act
        logic.ResetFilterState();

        // Assert
        Assert.Null(logic.Filters["result"].GetValue());
        Assert.Equal(1, logic.CurrentPage);
        // The DataView should remain until RefreshData is actually called
        Assert.Single(logic.DataView); 
    }

    [Fact]
    public async Task ClearFilters_ResetsStateAndReloadsDatabase()
    {
        // Arrange
        var data = new List<TestLogRecord> 
        { 
            new() { Id = 1, Barcode = "A" },
            new() { Id = 2, Barcode = "B" }
        };
        var logic = TestLogicFactory.CreateLogic(data);
        
        // Apply a filter that limits results
        logic.Filters["barcode"].SetValue("A");
        await logic.RefreshData();
        Assert.Single(logic.DataView);

        // Act
        await logic.ClearFilters();

        // Assert
        Assert.Null(logic.Filters["barcode"].GetValue());
        // Should have re-queried and found both records
        Assert.Equal(2, logic.DataView.Count);
    }

    [Fact]
    public void ClearData_PurgesAllState()
    {
        // Arrange
        var logic = TestLogicFactory.CreateLogic(new List<TestLogRecord>());
        logic.DataView = [new() { Id = 1, Barcode = "Test" }];
        logic.TotalCount = 1;
        logic.CurrentPage = 5;
        logic.Filters["barcode"].SetValue("SomeFilter");

        // Act
        logic.ClearData();

        // Assert
        Assert.Empty(logic.DataView);
        Assert.Equal(0, logic.TotalCount);
        Assert.Equal(1, logic.CurrentPage);
        Assert.Null(logic.Filters["barcode"].GetValue());
    }
}