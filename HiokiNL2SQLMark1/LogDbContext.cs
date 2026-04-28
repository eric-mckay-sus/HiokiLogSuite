// <copyright file="LogDbContext.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace HiokiNL2SQLMark1;

using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

// When creating new classes here for rows, use the [Column(columnNameInDb)] attribute to auto-link it (assuming you create the proper DbSet)

/// <summary>
/// Represents the state of the database in a way friendly to EFCore.
/// </summary>
/// <param name="options">The server details and login credentials.</param>
public class LogDbContext(DbContextOptions<LogDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Gets or sets the view for the group result table.
    /// </summary>
    public DbSet<GroupResult> GroupView { get; set; }

    /// <summary>
    /// Gets or sets the view for the step result table.
    /// </summary>
    public DbSet<StepResult> StepView { get; set; }

    /// <summary>
    /// Gets or sets the view for the FCT result table.
    /// </summary>
    public DbSet<FctResult> FctView { get; set; }
}

/// <summary>
/// The fields common between group, step, and FCT tables.
/// </summary>
public interface IHiokiLog
{
    /// <summary>
    /// Gets or sets the barcode for a Hioki log.
    /// </summary>
    string? Barcode { get; set; }

    /// <summary>
    /// Gets or sets the time for a Hioki log.
    /// </summary>
    DateTime? Time { get; set; }

    /// <summary>
    /// Gets or sets the group for a Hioki log.
    /// </summary>
    int? Group { get; set; }

    /// <summary>
    /// Gets or sets the result for a Hioki log.
    /// </summary>
    string? Result { get; set; }
}

/// <summary>
/// The fields common between the step and FCT tables only.
/// </summary>
public interface IStepFCT : IHiokiLog
{
    /// <summary>
    /// Gets or sets the step number for a step/FCT log.
    /// </summary>
    int? Step { get; set; }

    /// <summary>
    /// Gets or sets the position for a step/FCT log.
    /// </summary>
    public string? Position { get; set; }

    /// <summary>
    /// Gets or sets the test mode for a step/FCT log.
    /// </summary>
    public string? Mode { get; set; }

    /// <summary>
    /// Gets or sets the upper limit for a step/FCT log.
    /// </summary>
    public double? HighLim { get; set; }

    /// <summary>
    /// Gets or sets the lower limit for a step/FCT log.
    /// </summary>
    public double? LowLim { get; set; }

    /// <summary>
    /// Gets or sets the measurement unit for a step/FCT log.
    /// </summary>
    public char? MeasurementUnit { get; set; }

    /// <summary>
    /// Gets or sets the reference value for a step/FCT log.
    /// </summary>
    public double? RefVal { get; set; }

    /// <summary>
    /// Gets or sets the measured value for a step/FCT log.
    /// </summary>
    public double? MeasVal { get; set; }
}

/// <summary>
/// Defines the group table-specific columns.
/// </summary>
public interface IGroupSubResults : IHiokiLog
{
    /// <summary>
    /// Gets or sets the component sub-test result for a group result.
    /// </summary>
    public string? ComponentTest { get; set; }

    /// <summary>
    /// Gets or sets the short-circuit sub-test result for a group result.
    /// </summary>
    public string? ShortTest { get; set; }

    /// <summary>
    /// Gets or sets the open-circuit sub-test result for a group result.
    /// </summary>
    public string? OpenTest { get; set; }

    /// <summary>
    /// Gets or sets the IC sub-test result for a group result.
    /// </summary>
    public string? IcTest { get; set; }

    /// <summary>
    /// Gets or sets the macro sub-test result for a group result.
    /// </summary>
    public string? MacroTest { get; set; }

    /// <summary>
    /// Gets or sets the functional sub-test result for a group result.
    /// </summary>
    public string? FunctionTest { get; set; }
}

/// <summary>
/// Represents one row of GroupResults in the DB
/// NOTE: VERY SENSITIVE TO COL NAME CHANGES.
/// </summary>
[PrimaryKey(nameof(Barcode), nameof(Time), nameof(Group))]
public class GroupResult : IHiokiLog, IGroupSubResults
{
    /// <summary>
    /// Gets or sets the barcode for a <see cref="GroupResult"/>.
    /// </summary>
    [Column("Barcode")]
    public string? Barcode { get; set; }

    /// <summary>
    /// Gets or sets the test time for a <see cref="GroupResult"/>.
    /// </summary>
    [Column("Time")]
    public DateTime? Time { get; set; }

    /// <summary>
    /// Gets or sets the group number for a <see cref="GroupResult"/>.
    /// </summary>
    [Column("Group")]
    public int? Group { get; set; }

    /// <summary>
    /// Gets or sets the number of times a <see cref="GroupResult"/> has been tested.
    /// </summary>
    [Column("# of times tested")]
    public int? TimesTested { get; set; }

    /// <summary>
    /// Gets or sets the pass/fail status for a <see cref="GroupResult"/>.
    /// </summary>
    [Column("allResult")]
    public string? Result { get; set; }

    /// <summary>
    /// Gets or sets the component sub-test result for a <see cref="GroupResult"/>.
    /// </summary>
    [Column("componentTest")]
    public string? ComponentTest { get; set; }

    /// <summary>
    /// Gets or sets the short-circuit sub-test result for a <see cref="GroupResult"/>.
    /// </summary>
    [Column("shortTest")]
    public string? ShortTest { get; set; }

    /// <summary>
    /// Gets or sets the open-circuit sub-test result for a <see cref="GroupResult"/>.
    /// </summary>
    [Column("openTest")]
    public string? OpenTest { get; set; }

    /// <summary>
    /// Gets or sets the IC sub-test result for a <see cref="GroupResult"/>.
    /// </summary>
    [Column("icTest")]
    public string? IcTest { get; set; }

    /// <summary>
    /// Gets or sets the macro sub-test result for a <see cref="GroupResult"/>.
    /// </summary>
    [Column("macroTest")]
    public string? MacroTest { get; set; }

    /// <summary>
    /// Gets or sets the functional sub-test result for a <see cref="GroupResult"/>.
    /// </summary>
    [Column("functionTest")]
    public string? FunctionTest { get; set; }
}

/// <summary>
/// Represents one row of StepResults in the DB
/// NOTE: VERY SENSITIVE TO COL NAME CHANGES.
/// </summary>
[PrimaryKey(nameof(Barcode), nameof(Time), nameof(Group), nameof(Step))]
public class StepResult : IStepFCT
{
    /// <summary>
    /// Gets or sets the barcode for a <see cref="StepResult"/>.
    /// </summary>
    [Column("Barcode")]
    public string? Barcode { get; set; }

    /// <summary>
    /// Gets or sets the test time for a <see cref="StepResult"/>.
    /// </summary>
    [Column("Time")]
    public DateTime? Time { get; set; }

    /// <summary>
    /// Gets or sets the group number for a <see cref="StepResult"/>.
    /// </summary>
    [Column("Group")]
    public int? Group { get; set; }

    /// <summary>
    /// Gets or sets the step number for a <see cref="StepResult"/>.
    /// </summary>
    [Column("Step")]
    public int? Step { get; set; }

    /// <summary>
    /// Gets or sets the number of times a <see cref="StepResult"/> has been tested.
    /// </summary>
    [Column("Test times")]
    public int? TimesTested { get; set; }

    /// <summary>
    /// Gets or sets the pass/fail status for a <see cref="StepResult"/>.
    /// </summary>
    [Column("Result")]
    public string? Result { get; set; }

    /// <summary>
    /// Gets or sets the part name for a <see cref="StepResult"/>.
    /// </summary>
    [Column("Part Name")]
    public string? PartName { get; set; }

    /// <summary>
    /// Gets or sets the high pin identifier for a <see cref="StepResult"/>.
    /// </summary>
    [Column("High-pin")]
    public int? HighPin { get; set; }

    /// <summary>
    /// Gets or sets the low pin identifier for a <see cref="StepResult"/>.
    /// </summary>
    [Column("Low-pin")]
    public int? LowPin { get; set; }

    /// <summary>
    /// Gets or sets the part position for a <see cref="StepResult"/>.
    /// </summary>
    [Column("Part position")]
    public string? Position { get; set; }

    /// <summary>
    /// Gets or sets the test mode for a <see cref="StepResult"/>.
    /// </summary>
    [Column("Mode")]
    public string? Mode { get; set; }

    /// <summary>
    /// Gets or sets the measurement range for a <see cref="StepResult"/>.
    /// </summary>
    [Column("Measurement range")]
    public int? MeasRange { get; set; }

    /// <summary>
    /// Gets or sets the upper limit for a <see cref="StepResult"/>.
    /// </summary>
    [Column("High limit")]
    public double? HighLim { get; set; }

    /// <summary>
    /// Gets or sets the lower limit for a <see cref="StepResult"/>.
    /// </summary>
    [Column("Low limit")]
    public double? LowLim { get; set; }

    /// <summary>
    /// Gets or sets the measurement unit for a <see cref="StepResult"/>.
    /// </summary>
    [Column("Unit")]
    public char? MeasurementUnit { get; set; }

    /// <summary>
    /// Gets or sets the actual value for a <see cref="StepResult"/>.
    /// </summary>
    [Column("Actual (mounted) value")]
    public double? ActualVal { get; set; }

    /// <summary>
    /// Gets or sets the reference value for a <see cref="StepResult"/>.
    /// </summary>
    [Column("Reference value")]
    public double? RefVal { get; set; }

    /// <summary>
    /// Gets or sets the measured value for a <see cref="StepResult"/>.
    /// </summary>
    [Column("Measured value")]
    public double? MeasVal { get; set; }
}

/// <summary>
/// Represents one row of FctResults in the DB
/// NOTE: VERY SENSITIVE TO COL NAME CHANGES.
/// </summary>
[PrimaryKey(nameof(Barcode), nameof(Time), nameof(Group), nameof(Step))]
public class FctResult : IStepFCT
{
    /// <summary>
    /// Gets or sets the barcode for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Barcode")]
    public string? Barcode { get; set; }

    /// <summary>
    /// Gets or sets the test time for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Time")]
    public DateTime? Time { get; set; }

    /// <summary>
    /// Gets or sets the group number for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Group")]
    public int? Group { get; set; }

    /// <summary>
    /// Gets or sets the step number for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Step")]
    public int? Step { get; set; }

    /// <summary>
    /// Gets or sets the pass/fail status for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Result")]
    public string? Result { get; set; }

    /// <summary>
    /// Gets or sets the measurement group for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Measurement group")]
    public int? MeasGroup { get; set; }

    /// <summary>
    /// Gets or sets the step comment for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Step comment")]
    public string? Comment { get; set; }

    /// <summary>
    /// Gets or sets the part position for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Part position")]
    public string? Position { get; set; }

    /// <summary>
    /// Gets or sets the test mode for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Test mode")]
    public string? Mode { get; set; }

    /// <summary>
    /// Gets or sets the high pin identifier for an <see cref="FctResult"/>.
    /// </summary>
    [Column("High-pin")]
    public string? HighPin { get; set; }

    /// <summary>
    /// Gets or sets the low pin identifier for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Low-pin")]
    public string? LowPin { get; set; }

    /// <summary>
    /// Gets or sets the reference value for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Reference value")]
    public double? RefVal { get; set; }

    /// <summary>
    /// Gets or sets the measured value for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Measured value")]
    public double? MeasVal { get; set; }

    /// <summary>
    /// Gets or sets the upper limit for an <see cref="FctResult"/>.
    /// </summary>
    [Column("High limit")]
    public double? HighLim { get; set; }

    /// <summary>
    /// Gets or sets the lower limit for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Low limit")]
    public double? LowLim { get; set; }

    /// <summary>
    /// Gets or sets the measurement unit for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Unit")]
    public char? MeasurementUnit { get; set; }

    /// <summary>
    /// Gets or sets the ID1 for an <see cref="FctResult"/>.
    /// </summary>
    [Column("ID 1")]
    public string? ID1 { get; set; }

    /// <summary>
    /// Gets or sets the ID2 for an <see cref="FctResult"/>.
    /// </summary>
    [Column("ID 2")]
    public string? ID2 { get; set; }

    /// <summary>
    /// Gets or sets the ID3 for an <see cref="FctResult"/>.
    /// </summary>
    [Column("ID 3")]
    public string? ID3 { get; set; }

    /// <summary>
    /// Gets or sets the ID4 for an <see cref="FctResult"/>.
    /// </summary>
    [Column("ID 4")]
    public string? ID4 { get; set; }

    /// <summary>
    /// Gets or sets the input voltage for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Input voltage")]
    public double? InputVolt { get; set; }

    /// <summary>
    /// Gets or sets the communication standard for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Communication standard")]
    public string? CommStand { get; set; }

    /// <summary>
    /// Gets or sets the execution mode for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Execution mode")]
    public string? ExecMode { get; set; }

    /// <summary>
    /// Gets or sets the device address for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Device address")]
    public string? DevAddress { get; set; }

    /// <summary>
    /// Gets or sets the target address for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Target address")]
    public string? TarAddress { get; set; }

    /// <summary>
    /// Gets or sets the reference data for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Reference data")]
    public string? RefData { get; set; }

    /// <summary>
    /// Gets or sets the received data for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Received data")]
    public string? ReceiveData { get; set; }

    /// <summary>
    /// Gets or sets the interface response for an <see cref="FctResult"/>.
    /// </summary>
    [Column("Interface response")]
    public string? IFResponse { get; set; }
}
