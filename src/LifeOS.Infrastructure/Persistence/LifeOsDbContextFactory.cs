using LifeOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LifeOS.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c>. It reads the connection string from the
/// <c>LIFEOS_CONNECTION</c> environment variable so no credential is committed to source control.
/// </summary>
public sealed class LifeOsDbContextFactory : IDesignTimeDbContextFactory<LifeOsDbContext>
{
    public LifeOsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("LIFEOS_CONNECTION")
            ?? "Server=localhost;Database=LifeOS;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

        var options = new DbContextOptionsBuilder<LifeOsDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(LifeOsDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .Options;

        return new LifeOsDbContext(options, new SystemClock());
    }
}
