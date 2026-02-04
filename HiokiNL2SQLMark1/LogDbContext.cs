using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace HiokiNL2SQLMark1;
/// <summary>
/// Represents the state of the database in a way friendly to EFCore
/// </summary>
/// <param name="options">The server details and login credentials</param>
public class LogDbContext(DbContextOptions<LogDbContext> options) : DbContext(options)
{
    // One set per table
    public DbSet<GroupResult> GroupView { get; set; }
    public DbSet<StepResult> StepView { get; set; }
    public DbSet<FctResult> FctView { get; set; }
}

/// <summary>
/// The fields common between group, step, and FCT tables
/// </summary>
public interface IHiokiLog
{
    string? Barcode { get; set; }
    DateTime? Time { get; set; }
    int? Group { get; set; }
    string? Result { get; set; }
}

/// <summary>
/// The fields common between the step and FCT tables only
/// </summary>
public interface IStepFCT : IHiokiLog
{
    int? Step { get; set; }
    public string? Position { get; set; }
    public string? Mode { get; set; }
    public double? HighLim { get; set; }
    public double? LowLim { get; set; }
    public double? RefVal { get; set; }
    public double? MeasVal { get; set; }
}

/// <summary>
/// Represents one row of GroupResults in the DB
/// NOTE: VERY SENSITIVE TO COL NAME CHANGES
/// </summary>
[PrimaryKey(nameof(Barcode), nameof(Time), nameof(Group))]
public class GroupResult : IHiokiLog
{
    [Column("Barcode")] // technically this doesn't do anything, but keeps consistency with ones that need renamed
    public string? Barcode { get; set; }

    [Column("Time")]
    public DateTime? Time { get; set; }

    [Column("Group")]
    public int? Group { get; set; }

    [Column("# of times tested")] // applies to field below
    public int? TimesTested { get; set; }

    [Column("allResult")]
    public string? Result { get; set; }

    [Column("componentTest")]
    public string? ComponentTest { get; set; }

    [Column("shortTest")]
    public string? ShortTest { get; set; }

    [Column("openTest")]
    public string? OpenTest { get; set; }

    [Column("icTest")]
    public string? IcTest { get; set; }

    [Column("macroTest")]
    public string? MacroTest { get; set; }

    [Column("functionTest")]
    public string? FunctionTest { get; set; }
}

/// <summary>
/// Represents one row of StepResults in the DB
/// NOTE: VERY SENSITIVE TO COL NAME CHANGES
/// </summary>
[PrimaryKey(nameof(Barcode), nameof(Time), nameof(Group), nameof(Step))]
public class StepResult : IStepFCT
{
    [Column("Barcode")]
    public string? Barcode { get; set; }

    [Column("Time")]
    public DateTime? Time { get; set; }
    
    [Column("Group")]
    public int? Group { get; set; }

    [Column("Step")]
    public int? Step { get; set; }

    [Column("Test times")]
    public int? TimesTested { get; set; }

    [Column("Result")]
    public string? Result { get; set; }

    [Column("Part Name")]
    public string? PartName { get; set; }

    [Column("High-pin")]
    public int? HighPin { get; set; }

    [Column("Low-pin")]
    public int? LowPin { get; set; }

    [Column("Part position")]
    public string? Position { get; set; }

    [Column("Mode")]
    public string? Mode { get; set; }

    [Column("Measurement range")]
    public int? MeasRange { get; set; }

    [Column("High limit")]
    public double? HighLim { get; set; }

    [Column("Low limit")]
    public double? LowLim { get; set; }

    [Column("Actual (mounted) value")]
    public double? ActualVal { get; set; }

    [Column("Reference value")]
    public double? RefVal { get; set; }

    [Column("Measurement value")]
    public double? MeasVal { get; set; }
}

/// <summary>
/// Represents one row of FctResults in the DB
/// NOTE: VERY SENSITIVE TO COL NAME CHANGES
/// </summary>
[PrimaryKey(nameof(Barcode), nameof(Time), nameof(Group), nameof(Step))]
public class FctResult : IStepFCT
{
    [Column("Barcode")]
    public string? Barcode { get; set; }

    [Column("Time")]
    public DateTime? Time { get; set; }

    [Column("Group")]
    public int? Group { get; set; }
    
    [Column("Step")]
    public int? Step { get; set; }

    [Column("Result")]
    public string? Result { get; set; }

    [Column("Measurement group")]
    public int? MeasGroup { get; set; }

    [Column("Step comment")]
    public string? Comment { get; set; }

    [Column("Part position")]
    public string? Position { get; set; }

    [Column("Test mode")]
    public string? Mode { get; set; }

    [Column("High-pin")]
    public string? HighPin { get; set; }

    [Column("Low-pin")]
    public string? LowPin { get; set; }

    [Column("Reference value")]
    public double? RefVal { get; set; }

    [Column("Measured value")]
    public double? MeasVal { get; set; }

    [Column("High limit")]
    public double? HighLim { get; set; }

    [Column("Low limit")]
    public double? LowLim { get; set; }

    [Column("ID 1")]
    public string? ID1 { get; set; }

    [Column("ID 2")]
    public string? ID2 { get; set; }

    [Column("ID 3")]
    public string? ID3 { get; set; }

    [Column("ID 4")]
    public string? ID4 { get; set; }

    [Column("Input voltage")]
    public double? InputVolt { get; set; }

    [Column("Communication standard")]
    public string? CommStand { get; set; }

    [Column("Execution mode")]
    public string? ExecMode { get; set; }

    [Column("Device address")]
    public string? DevAddress { get; set; }

    [Column("Target address")]
    public string? TarAddress { get; set; }

    [Column("Reference data")]
    public string? RefData { get; set; }

    [Column("Received data")]
    public string? ReceiveData { get; set; }

    [Column("Interface response")]
    public string? IFResponse { get; set; }
}