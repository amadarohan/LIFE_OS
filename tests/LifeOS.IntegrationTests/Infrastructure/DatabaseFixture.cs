using LifeOS.Application;
using LifeOS.Application.Abstractions;
using LifeOS.Infrastructure;
using LifeOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOS.IntegrationTests.Infrastructure;

/// <summary>
/// Spins up a throwaway SQL Server database per test class, applies the real migrations, and
/// exposes a scoped service provider wired exactly like the application. Using the real provider
/// (not InMemory) is deliberate: the approval lifecycle depends on unique indexes, row versions,
/// and transaction behaviour that an in-memory store does not reproduce.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly string _databaseName = $"LifeOS_Test_{Guid.NewGuid():N}";
    private ServiceProvider _root = null!;

    public string ConnectionString { get; private set; } = string.Empty;

    public IServiceProvider Services => _root;

    public async Task InitializeAsync()
    {
        var masterConnection =
            Environment.GetEnvironmentVariable("LIFEOS_TEST_MASTER")
            ?? "Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True";

        ConnectionString =
            $"Server=localhost;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LifeOS"] = ConnectionString,
                ["LifeOS:TimeZone"] = "Asia/Tehran",
                ["LifeOS:StorageRoot"] = Path.Combine(Path.GetTempPath(), "lifeos-test-storage"),
                ["LifeOS:BackupDirectory"] = Path.Combine(Path.GetTempPath(), "lifeos-test-backups")
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddInfrastructure(configuration);
        services.AddApplication();

        _root = services.BuildServiceProvider();

        // Create the database by running the real migrations, never EnsureCreated.
        await using var scope = _root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LifeOsDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using (var scope = _root.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LifeOsDbContext>();
            await db.Database.EnsureDeletedAsync();
        }

        await _root.DisposeAsync();
    }

    /// <summary>Creates a fresh scope so each test gets its own DbContext and change tracker.</summary>
    public AsyncServiceScope CreateScope() => _root.CreateAsyncScope();
}
