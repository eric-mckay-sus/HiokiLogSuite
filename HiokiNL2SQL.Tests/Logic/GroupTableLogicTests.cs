using HiokiNL2SQLMark1;
using HiokiNL2SQLMark1.Logic;

namespace HiokiNL2SQL.Tests.Logic;
public class GroupTableLogicTests
{
    public static readonly TheoryData<string, string, string> GroupFilterData = 
    new()
    {
        // Key, PropertyName on GroupResult, TestValue
        { "comp", "ComponentTest", "C1" },
        { "short", "ShortTest", "S1" },
        { "macro", "MacroTest", "M1" },
        { "ic", "IcTest", "I1" },
        { "function", "FunctionTest", "F1" }
    };
    
    [Theory]
    [MemberData(nameof(GroupFilterData))]
    public async Task ApplyFilters_GroupSpecific_FiltersCorrectly(string key, string propName, string value)
    {
        // Arrange: Create two records, one that matches the specific test value
        var matchRecord = new GroupResult();
        typeof(GroupResult).GetProperty(propName)?.SetValue(matchRecord, value);
        
        var nonMatchRecord = new GroupResult();
        typeof(GroupResult).GetProperty(propName)?.SetValue(nonMatchRecord, "DIFFERENT");
        
        var data = new List<GroupResult> { matchRecord, nonMatchRecord };
        
        var logic = TestLogicFactory.CreateLogic<GroupResult, GroupTableLogic>(data);

        // Act: Set the specific filter using the key
        // We use the AssignTableSpecific logic indirectly via DictionaryToFilters
        var dict = new Dictionary<string, IFilter> { { key, new Filter<string?>(key, value) } };
        await logic.DictionaryToFilters(dict);

        // Assert
        Assert.Single(logic.DataView);
        var actualValue = typeof(GroupResult).GetProperty(propName)?.GetValue(logic.DataView[0]);
        Assert.Equal(value, actualValue);
    }

    [Theory]
    [MemberData(nameof(GroupFilterData))]
    public async Task ApplyFilters_GroupSpecific_Negated_FiltersOut(string key, string propName, string value)
    {
        // Arrange
        var matchRecord = new GroupResult();
        typeof(GroupResult).GetProperty(propName)?.SetValue(matchRecord, value);
        
        var otherRecord = new GroupResult();
        typeof(GroupResult).GetProperty(propName)?.SetValue(otherRecord, "REMAINING");

        var data = new List<GroupResult> { matchRecord, otherRecord };
        var logic = TestLogicFactory.CreateLogic<GroupResult, GroupTableLogic>(data);

        // Act: Set negated filter
        var dict = new Dictionary<string, IFilter> { { key, new Filter<string?>(key, value, isNegated: true) } };
        await logic.DictionaryToFilters(dict);

        // Assert
        Assert.Single(logic.DataView);
        var actualValue = typeof(GroupResult).GetProperty(propName)?.GetValue(logic.DataView[0]);
        Assert.Equal("REMAINING", actualValue);
    }

    [Fact]
    public void ResetFilterState_ClearsGroupSpecificFilters()
    {
        // Arrange
        var logic = TestLogicFactory.CreateLogic<GroupResult, GroupTableLogic>(new List<GroupResult>());
        logic.FilterComp.Value = "Test";
        logic.FilterShort.Value = "Test";
        logic.FilterBarcode.Value = "BaseTest"; // Testing that base call also happens

        // Act
        logic.ResetFilterState();

        // Assert
        Assert.Null(logic.FilterComp.Value);
        Assert.Null(logic.FilterShort.Value);
        Assert.Null(logic.FilterBarcode.Value);
    }

    [Fact]
    public async Task DictionaryToFilters_HandlesMixedBaseAndSpecificFilters()
    {
        // Arrange: Test that GroupTableLogic correctly siphons its own tags 
        // while letting LogTableLogic handle the barcode.
        var data = new List<GroupResult> { 
            new() { Barcode = "ABC", ComponentTest = "C1" },
            new() { Barcode = "ABC", ComponentTest = "C2" },
            new() { Barcode = "XYZ", ComponentTest = "C1" }
        };
        var logic = TestLogicFactory.CreateLogic<GroupResult, GroupTableLogic>(data);
        
        var filterDict = new Dictionary<string, IFilter> {
            { "barcode", new Filter<string?>("barcode", "ABC") },
            { "comp", new Filter<string?>("comp", "C1") }
        };

        // Act
        await logic.DictionaryToFilters(filterDict);

        // Assert
        Assert.Single(logic.DataView);
        Assert.Equal("ABC", logic.DataView[0].Barcode);
        Assert.Equal("C1", logic.DataView[0].ComponentTest);
    }
}