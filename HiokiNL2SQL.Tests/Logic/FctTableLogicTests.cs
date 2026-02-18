using System.Diagnostics.CodeAnalysis;
using HiokiNL2SQLMark1;
using HiokiNL2SQLMark1.Logic;

namespace HiokiNL2SQL.Tests.Logic;
[ExcludeFromCodeCoverage]
public class FctTableLogicTests
{
    public static readonly TheoryData<string, IFilter, string, object?> FctFilterData = 
    new()
    {
        // Key, Filter Object, Property Name, Expected Value
        { "mode", new Filter<string?>("mode", "C-CC"), "Mode", "C-CC" },
        { "step", new Filter<int?>("step", 7), "Step", 7 }
    };
    [Theory]
    [MemberData(nameof(FctFilterData))]
    public async Task ApplyFilters_FctSpecific_FiltersCorrectly(string key, IFilter filter, string propName, object? value)
    {
        // Arrange
        var matchRecord = new FctResult();
        typeof(FctResult).GetProperty(propName)?.SetValue(matchRecord, value);
        
        var otherRecord = new FctResult();
        // Set otherRecord to a different value to ensure filtering happens
        object otherValue = value is int i ? i + 1 : "DIFFERENT";
        typeof(FctResult).GetProperty(propName)?.SetValue(otherRecord, otherValue);

        var data = new List<FctResult> { matchRecord, otherRecord };
        var logic = TestLogicFactory.CreateLogic<FctResult, FctTableLogic>(data);

        // Act
        var dict = new Dictionary<string, IFilter> { { key, filter } };
        await logic.DictionaryToFilters(dict);

        // Assert
        Assert.Single(logic.DataView);
        var actualValue = typeof(FctResult).GetProperty(propName)?.GetValue(logic.DataView[0]);
        Assert.Equal(value, actualValue);
    }

    [Theory]
    [MemberData(nameof(FctFilterData))]
    public async Task ApplyFilters_FctSpecific_Negated_FiltersOut(string key, IFilter filter, string propName, object? value)
    {
        // Arrange
        var matchRecord = new FctResult();
        typeof(FctResult).GetProperty(propName)?.SetValue(matchRecord, value);
        
        var otherRecord = new FctResult();
        object otherValue = value is int i ? i + 1 : "REMAINING";
        typeof(FctResult).GetProperty(propName)?.SetValue(otherRecord, otherValue);

        var data = new List<FctResult> { matchRecord, otherRecord };
        var logic = TestLogicFactory.CreateLogic<FctResult, FctTableLogic>(data);

        // Act: Create a negated version of the filter
        IFilter negatedFilter = filter switch {
            Filter<string?> s => new Filter<string?>(s.Key, s.Value, isNegated: true),
            Filter<int?> n => new Filter<int?>(n.Key, n.Value, isNegated: true),
            _ => throw new ArgumentException("Unsupported filter type")
        };

        var dict = new Dictionary<string, IFilter> { { key, negatedFilter } };
        await logic.DictionaryToFilters(dict);

        // Assert
        Assert.Single(logic.DataView);
        var actualValue = typeof(FctResult).GetProperty(propName)?.GetValue(logic.DataView[0]);
        Assert.Equal(otherValue, actualValue);
    }

    [Fact]
    public void ResetFilterState_ClearsFctSpecificFilters()
    {
        // Arrange
        var logic = TestLogicFactory.CreateLogic<FctResult, FctTableLogic>([]);
        logic.Filters["step"].SetValue(10);
        logic.Filters["mode"].SetValue("FLASH ROM");
        logic.Filters["barcode"].SetValue("ABC"); 

        // Act
        logic.ResetFilterState();

        // Assert
        Assert.Null(logic.Filters["step"].GetValue());
        Assert.Null(logic.Filters["mode"].GetValue());
        Assert.Null(logic.Filters["barcode"].GetValue());
    }

    [Fact]
    public async Task DictionaryToFilters_AssignsFctFilterCorrectly()
    {
        // Arrange
        var logic = TestLogicFactory.CreateLogic<FctResult, FctTableLogic>([]);
        var stepFilter = new Filter<int?>("step", 5);
        var dict = new Dictionary<string, IFilter> { { "step", stepFilter } };

        // Act
        await logic.DictionaryToFilters(dict);

        // Assert
        Assert.Equal(5, logic.Filters["step"].GetValue());
        Assert.True(logic.Filters["step"].IsActive);
    }
}