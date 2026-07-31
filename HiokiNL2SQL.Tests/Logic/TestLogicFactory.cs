using Moq;
using Microsoft.EntityFrameworkCore;
using HiokiNL2SQL.Logic;
using System.Diagnostics.CodeAnalysis;

namespace HiokiNL2SQL.Tests.Logic;
[ExcludeFromCodeCoverage]
public static class TestLogicFactory
{
    public static TLogic CreateLogic<T, TLogic>(List<T> initialData, IJSService? js = null, INavService? nav = null)
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
                        if (item is IStepFct sf && sf.Step == default)
                        {
                            sf.Step = new Random().Next(1, 100000);
                        }
                    }
                    context.Set<T>().AddRange(initialData);
                    context.SaveChanges();
                }
                return context;
            });

        var finalJs = js ?? new Mock<IJSService>().Object;
        var finalNav = nav ?? new Mock<INavService>().Object;

        // Direct construction avoids runtime constructor binding issues for the current generic model.
        if (typeof(TLogic) == typeof(GroupTableLogic))
        {
            return (TLogic)(object)new GroupTableLogic(mockFactory.Object, finalJs, finalNav)
            {
                DbFactory = mockFactory.Object,
                QuerySelector = db => db.GroupView,
                JS = finalJs,
                Nav = finalNav,
            };
        }
        else if (typeof(TLogic) == typeof(StepTableLogic))
        {
            return (TLogic)(object)new StepTableLogic(mockFactory.Object, finalJs, finalNav)
            {
                DbFactory = mockFactory.Object,
                QuerySelector = db => db.StepView,
                JS = finalJs,
                Nav = finalNav,
            };
        }
        else if (typeof(TLogic) == typeof(FctTableLogic))
        {
            return (TLogic)(object)new FctTableLogic(mockFactory.Object, finalJs, finalNav)
            {
                DbFactory = mockFactory.Object,
                QuerySelector = db => db.FctView,
                JS = finalJs,
                Nav = finalNav,
            };
        }

        // Default fallback for the base class.
        static IQueryable<T> selector(LogDbContext db) => db.Set<T>();
        return (TLogic)(object)new LogTableLogic<T>(mockFactory.Object, selector, finalJs, finalNav)
        {
            DbFactory = mockFactory.Object,
            QuerySelector = selector,
            JS = finalJs,
            Nav = finalNav,
        };
    }

    // Overload to keep existing single-generic calls working for TestLogRecord
    public static LogTableLogic<T> CreateLogic<T>(List<T> initialData, IJSService? js = null, INavService? nav = null) where T : class, IHiokiLog
        => CreateLogic<T, LogTableLogic<T>>(initialData, js, nav);
}
