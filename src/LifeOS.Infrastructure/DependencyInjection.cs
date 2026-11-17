using LifeOS.Application.Abstractions;
using LifeOS.Application.Abstractions.Ai;
using LifeOS.Infrastructure.Ai;
using LifeOS.Infrastructure.Persistence;
using LifeOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOS.Infrastructure;

/// <summary>
/// Composition root for the Infrastructure layer. The Web project calls this once; nothing else
/// needs to know which concrete implementations exist.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LifeOS")
            ?? throw new InvalidOperationException(
                "Connection string 'LifeOS' is missing. Set ConnectionStrings:LifeOS in appsettings or user secrets.");

        services.AddDbContext<LifeOsDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(LifeOsDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), null);
                sql.CommandTimeout(60);
            });
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<LifeOsDbContext>());

        services.AddSingleton<IClock, SystemClock>();

        services.AddSingleton<IUserTimeZone>(_ =>
            new UserTimeZone(configuration["LifeOS:TimeZone"]));

        var storageRoot = configuration["LifeOS:StorageRoot"]
            ?? Path.Combine(AppContext.BaseDirectory, "storage");

        services.AddSingleton<IFileStorage>(_ => new LocalFileStorage(storageRoot));

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        }
        else
        {
            throw new PlatformNotSupportedException(
                "Life OS currently targets Windows for secret protection (DPAPI). " +
                "Provide an alternative ISecretProtector implementation for other platforms.");
        }

        services.AddScoped<IAuditWriter, AuditWriter>();

        var backupDirectory = configuration["LifeOS:BackupDirectory"]
            ?? Path.Combine(AppContext.BaseDirectory, "backups");

        services.AddSingleton<IDatabaseBackupStore>(_ =>
            new SqlServerBackupStore(connectionString, backupDirectory));

        AddAi(services);

        return services;
    }

    /// <summary>
    /// AI wiring. Providers are created per request from settings, so nothing here caches an
    /// endpoint or an API key. The fake provider is deliberately absent: it exists for tests only
    /// and must never be reachable in a running application.
    /// </summary>
    private static void AddAi(IServiceCollection services)
    {
        services.AddHttpClient(AiHttpClients.Name);

        services.AddSingleton<IAiProviderHealthTracker, AiProviderHealthTracker>();
        services.AddSingleton<IAiProviderRegistry, AiProviderRegistry>();
        services.AddSingleton<IPromptInjectionGuard, PromptInjectionGuard>();

        // Reads provider configuration from the settings table; scoped because SettingsService is.
        services.AddScoped<IAiProviderOptionsProvider, SettingsAiProviderOptionsProvider>();
        services.AddScoped<IAiProviderRouter, AiProviderRouter>();
    }
}
