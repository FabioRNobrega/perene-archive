using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Data;
using WebApp.Services;

namespace WebApp.Tests;

/// <summary>A real, migrated SQLite file with the durable job store and naming counters wired the way the host wires them.</summary>
internal sealed class JobTestDb : IDisposable
{
    private readonly TemporaryDirectory _root = new();
    private readonly ServiceProvider _services;

    public JobTestDb()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options
            .UseSqlite($"Data Source={Path.Combine(_root.Path, "jobs.db")};Foreign Keys=True;Pooling=False")
            .AddInterceptors(new SqliteConnectionInterceptor()));
        _services = services.BuildServiceProvider();
        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
        var factory = _services.GetRequiredService<IServiceScopeFactory>();
        Store = new SqliteJobStore(factory);
        Counters = new NamingCounterService(factory);
        Scopes = factory;
    }

    /// <summary>One process-wide instance for tests that only need a counter service; each test uses its own directory, so series never collide.</summary>
    public static JobTestDb Shared { get; } = new();

    public SqliteJobStore Store { get; }
    public NamingCounterService Counters { get; }
    public IServiceScopeFactory Scopes { get; }
    public ICompositionJobStatusStore Composition => Store;
    public IArchiveMutationJobStatusStore Mutation => Store;
    public IVideoConversionJobStatusStore Conversion => Store;
    public ICutJobRecorder Cuts => Store;

    public AppDbContext NewContext() => _services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    public void Dispose()
    {
        _services.Dispose();
        _root.Dispose();
    }
}
