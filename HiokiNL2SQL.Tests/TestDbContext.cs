using HiokiNL2SQL;
using HiokiNL2SQL.Tests.Logic;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;

namespace HiokiNL2SQL.Tests;
[ExcludeFromCodeCoverage]

// This context "tricks" EF into accepting TestLogRecord and other test record classes
public class TestDbContext(DbContextOptions<LogDbContext> options) : LogDbContext(options)
{
    // Inherit each of the DbSets from LogDbContext

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Dynamically register each entity with its primary key
        modelBuilder.Entity<TestLogRecord>(); // EF Core auto-detects TestLogRecord's Id attribute and uses it as PK
        modelBuilder.Entity<TestStepFct>(); // TestStepFct inherits from TestLogRecord, so it has an Id too
        // These three have PKs defined in LogDbContext.cs, which this class inherits from, so it doesn't matter that they're missing an Id
        modelBuilder.Entity<GroupResult>();
        modelBuilder.Entity<StepResult>();
        modelBuilder.Entity<FctResult>();
    }
}
