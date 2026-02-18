using Moq;
using HiokiNL2SQLMark1.Logic;
using Microsoft.JSInterop;
using HiokiNL2SQLMark1;
using System.Diagnostics.CodeAnalysis;

namespace HiokiNL2SQL.Tests;
[ExcludeFromCodeCoverage]
public class PowerSearchNavServiceTests
{
    private readonly Mock<ILogTableLogic> _mockTable;
    private readonly SearchParserService _realParser;
    private readonly Mock<INavService> _mockNav;
    private readonly Mock<IJSRuntime> _mockJs;
    private readonly PowerSearchLogic _logic;

    public PowerSearchNavServiceTests()
    {
        _mockTable = new Mock<ILogTableLogic>();
        _mockTable.SetupAllProperties();
        _mockTable.Setup(t => t.TableName).Returns("group");

        _realParser = new SearchParserService();
        _mockNav = new Mock<INavService>();
        _mockJs = new Mock<IJSRuntime>();

        _logic = new PowerSearchLogic(new[] { _mockTable.Object }, _realParser, _mockNav.Object, _mockJs.Object);
    }

    [Fact]
    public void SyncUrl_CallsNavService_WithCorrectParameters()
    {
        // Arrange
        _logic.commandInput = "barcode:123";
        _logic.CurrentType = "group";
        _mockTable.Object.CurrentPage = 2;
        _mockTable.Object.PageSize = 100;
        _mockTable.Object.CurrentSortColumn = "Time";
        _mockTable.Object.SortDir = "descending";

        // Act
        _logic.SyncUrl();

        // Assert
        _mockNav.Verify(n => n.UpdateSearchState(
            "barcode:123",
            2,
            100,
            "Time",
            "descending",
            true), Times.Once);
    }

    [Fact]
    public void TableTrigger_InvokesNavServiceUpdateSearchState()
    {
        // Arrange
        const string query = "barcode:999";

        // Act: invoke the trigger the logic wired up on construction
        _mockTable.Object.TriggerPowerSearch?.Invoke(query);

        // Assert
        _mockNav.Verify(n => n.UpdateSearchState(query, null, null, null, null, false), Times.Once);
    }
}
