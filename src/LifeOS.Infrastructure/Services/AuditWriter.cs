using LifeOS.Application.Abstractions;
using LifeOS.Domain.Entities.System;
using LifeOS.Domain.Enums;
using LifeOS.Infrastructure.Persistence;

namespace LifeOS.Infrastructure.Services;

/// <summary>
/// Writes audit rows through the same unit of work as the change being audited, so an audit
/// record can never be lost while the change commits.
/// </summary>
public sealed class AuditWriter : IAuditWriter
{
    private readonly LifeOsDbContext _dbContext;
    private readonly IClock _clock;

    public AuditWriter(LifeOsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public Task WriteAsync(
        string entityType,
        Guid entityId,
        AuditActionType actionType,
        string? oldValuesJson,
        string? newValuesJson,
        AuditSource source,
        CancellationToken cancellationToken = default)
    {
        var log = new AuditLog(
            entityType,
            entityId,
            actionType,
            oldValuesJson,
            newValuesJson,
            source,
            _clock.UtcNow);

        _dbContext.AuditLogs.Add(log);
        return Task.CompletedTask;
    }
}
