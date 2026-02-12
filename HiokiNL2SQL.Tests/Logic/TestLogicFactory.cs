using Moq;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using HiokiNL2SQLMark1.Logic;
using HiokiNL2SQLMark1;

namespace HiokiNL2SQL.Tests.Logic;
public static class TestLogicFactory
{
    public static TLogic CreateLogic<T, TLogic>(List<T> initialData, IJSRuntime? js = null, NavigationManager? nav = null) 
        where T : class, IHiokiLog
        where TLogic : LogTableLogic<T>
    {
        var options = new DbContextOptionsBuilder<LogDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var mockFactory = new Mock<IDbContextFactory<LogDbContext>>();
        mockFactory.Setup(f => f.CreateDbContextAsync(default))
            .ReturnsAsync(() => {
                var context = new TestDbContext(options);
                if (!context.Set<T>().Any()) {
                    foreach (var item in initialData) // Ensure all PK fields are not null
                    {
                        if (item is IHiokiLog log)
                        {
                            log.Barcode ??= Guid.NewGuid().ToString();
                            if (log.Time == default) log.Time = DateTime.Now;
                            if (log.Group == default) log.Group = new Random().Next(1, 100000);
                        }
                        if (item is IStepFCT sf)
                        {
                            if (sf.Step == default) sf.Step = new Random().Next(1, 100000);
                        }
                    }
                    context.Set<T>().AddRange(initialData);
                    context.SaveChanges();
                }
                return context;
            });

        var finalJs = js ?? new Mock<IJSRuntime>().Object;
        var finalNav = nav ?? new Mock<NavigationManager>().Object;

        // Determine if we are creating GroupTableLogic or the base LogTableLogic
        if (typeof(TLogic) == typeof(GroupTableLogic))
        {
            return (TLogic)Activator.CreateInstance(typeof(GroupTableLogic), mockFactory.Object, finalJs, finalNav)!;
        } else if (typeof(TLogic) == typeof(StepTableLogic))
        {
            return (TLogic)Activator.CreateInstance(typeof(StepTableLogic), mockFactory.Object, finalJs, finalNav)!;
        } else if (typeof(TLogic) == typeof(FctTableLogic))
        {
            return (TLogic)Activator.CreateInstance(typeof(FctTableLogic), mockFactory.Object, finalJs, finalNav)!;
        }

        // Default fallback for the base class
        static IQueryable<T> selector(LogDbContext db) => db.Set<T>();
        return (TLogic)Activator.CreateInstance(typeof(TLogic), mockFactory.Object, (Func<LogDbContext, IQueryable<T>>)selector, finalJs, finalNav)!;
    }

    // Overload to keep existing single-generic calls working for TestLogRecord
    public static LogTableLogic<T> CreateLogic<T>(List<T> initialData, IJSRuntime? js = null, NavigationManager? nav = null) where T : class, IHiokiLog
        => CreateLogic<T, LogTableLogic<T>>(initialData, js, nav);
}