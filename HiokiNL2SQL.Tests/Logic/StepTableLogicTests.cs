using System.Diagnostics.CodeAnalysis;
using HiokiNL2SQLMark1;
using HiokiNL2SQLMark1.Logic;

namespace HiokiNL2SQL.Tests.Logic;
[ExcludeFromCodeCoverage]
public class StepTableLogicTests
{
    public static readonly TheoryData<string, IFilter, string, object?> StepFilterData = 
    new()
    {
        // Key, Filter Object, Property Name, Expected Value
        { "part", new Filter<string?>("part", "R101"), "PartName", "R101" },
        { "mode", new Filter<string?>("mode", "C-CC"), "Mode", "C-CC" },
        { "step", new Filter<int?>("step", 7), "Step", 7 }
    };
    [Theory]
    [MemberData(nameof(StepFilterData))]
    public async Task ApplyFilters_StepSpecific_FiltersCorrectly(string key, IFilter filter, string propName, object? value)
    {
        // Arrange
        var matchRecord = new StepResult();
        typeof(StepResult).GetProperty(propName)?.SetValue(matchRecord, value);
        
        var otherRecord = new StepResult();
        // Set otherRecord to a different value to ensure filtering happens
        object otherValue = value is int i ? i + 1 : "DIFFERENT";
        typeof(StepResult).GetProperty(propName)?.SetValue(otherRecord, otherValue);

        var data = new List<StepResult> { matchRecord, otherRecord };
        var logic = TestLogicFactory.CreateLogic<StepResult, StepTableLogic>(data);

        // Act
        var dict = new Dictionary<string, IFilter> { { key, filter } };
        await logic.DictionaryToFilters(dict);

        // Assert
        Assert.Single(logic.DataView);
        var actualValue = typeof(StepResult).GetProperty(propName)?.GetValue(logic.DataView[0]);
        Assert.Equal(value, actualValue);
    }

    [Theory]
    [MemberData(nameof(StepFilterData))]
    public async Task ApplyFilters_StepSpecific_Negated_FiltersOut(string key, IFilter filter, string propName, object? value)
    {
        // Arrange
        var matchRecord = new StepResult();
        typeof(StepResult).GetProperty(propName)?.SetValue(matchRecord, value);
        
        var otherRecord = new StepResult();
        object otherValue = value is int i ? i + 1 : "REMAINING";
        typeof(StepResult).GetProperty(propName)?.SetValue(otherRecord, otherValue);

        var data = new List<StepResult> { matchRecord, otherRecord };
        var logic = TestLogicFactory.CreateLogic<StepResult, StepTableLogic>(data);

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
        var actualValue = typeof(StepResult).GetProperty(propName)?.GetValue(logic.DataView[0]);
        Assert.Equal(otherValue, actualValue);
    }

    [Fact]
    public void ResetFilterState_ClearsStepSpecificFilters()
    {
        // Arrange
        var logic = TestLogicFactory.CreateLogic<StepResult, StepTableLogic>([]);
        logic.FilterPartName.Value = "R1";
        logic.FilterStep.Value = 10;
        logic.FilterMode.Value = "R-AC160";
        logic.FilterBarcode.Value = "ABC"; 

        // Act
        logic.ResetFilterState();

        // Assert
        Assert.Null(logic.FilterPartName.Value);
        Assert.Null(logic.FilterStep.Value);
        Assert.Null(logic.FilterMode.Value);
        Assert.Null(logic.FilterBarcode.Value);
    }

    [Fact]
    public async Task DictionaryToFilters_AssignsStepFilterCorrectly()
    {
        // Arrange
        var logic = TestLogicFactory.CreateLogic<StepResult, StepTableLogic>([]);
        var stepFilter = new Filter<int?>("step", 5);
        var dict = new Dictionary<string, IFilter> { { "step", stepFilter } };

        // Act
        await logic.DictionaryToFilters(dict);

        // Assert
        Assert.Equal(5, logic.FilterStep.Value);
        Assert.True(logic.FilterStep.IsActive);
    }
}