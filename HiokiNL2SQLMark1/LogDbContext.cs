using Microsoft.EntityFrameworkCore;

public class LogDbContext : DbContext
{
    public LogDbContext(DbContextOptions<LogDbContext> options) : base(options) { }

    // This represents your table of logs
    public DbSet<ResultType> ResultTypes { get; set; }
    public DbSet<TestMode> TestModes { get; set; }
}

public class ResultType
{
    public byte id { get; set; }
    public string resultType {get; set;}
}

public class TestMode
{
    public byte id { get; set; }
    public string testMode {get; set;}
}