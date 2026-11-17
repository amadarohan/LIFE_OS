using LifeOS.Application.Abstractions;
using LifeOS.Application.Common;
using LifeOS.Domain.Common;
using LifeOS.Domain.Entities.LifeDomains;
using LifeOS.Domain.Enums;

namespace LifeOS.Application.UseCases.LifeDomains;

public sealed record LifeDomainSummary(
    Guid Id,
    string Name,
    string? Description,
    string Color,
    string Icon,
    int SortOrder,
    bool IsActive,
    int MetricCount,
    int ProjectCount,
    int GoalCount);

public sealed record LifeAreaMetricView(
    Guid Id,
    string Name,
    string? Description,
    string Unit,
    decimal CurrentValue,
    decimal TargetValue,
    decimal? ProgressPercentage,
    MetricType MetricType,
    MeasurementFrequency MeasurementFrequency,
    DateTimeOffset? LastMeasuredAtUtc);

public sealed record LifeDomainDetail(
    Guid Id,
    string Name,
    string? Description,
    string Color,
    string Icon,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<LifeAreaMetricView> Metrics);

public sealed record CreateLifeDomainRequest(
    string Name,
    string? Description,
    string Color,
    string Icon,
    int SortOrder);

public sealed record UpdateLifeDomainRequest(
    Guid Id,
    string Name,
    string? Description,
    string Color,
    string Icon,
    int SortOrder);

public sealed record AddMetricRequest(
    Guid LifeDomainId,
    string Name,
    string? Description,
    string Unit,
    decimal CurrentValue,
    decimal TargetValue,
    MetricType MetricType,
    MeasurementFrequency Frequency);

/// <summary>
/// Life domains are the top of the alignment hierarchy: every project and goal points at one.
/// Deleting is not offered; domains are deactivated so historical alignment stays intact.
/// </summary>
public sealed class LifeDomainService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;

    public LifeDomainService(IApplicationDbContext db, IAuditWriter audit, IClock clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<LifeDomainSummary>> ListAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var domains = await _db.ToListAsync(
            _db.LifeDomains
                .Where(d => includeInactive || d.IsActive)
                .OrderBy(d => d.SortOrder)
                .ThenBy(d => d.Name),
            cancellationToken);

        var metricCounts = await _db.ToListAsync(
            _db.LifeAreaMetrics.GroupBy(m => m.LifeDomainId)
                .Select(g => new { LifeDomainId = g.Key, Count = g.Count() }),
            cancellationToken);

        var projectCounts = await _db.ToListAsync(
            _db.Projects.Where(p => p.LifeDomainId != null && !p.IsArchived)
                .GroupBy(p => p.LifeDomainId!.Value)
                .Select(g => new { LifeDomainId = g.Key, Count = g.Count() }),
            cancellationToken);

        var goalCounts = await _db.ToListAsync(
            _db.Goals.Where(g => g.LifeDomainId != null)
                .GroupBy(g => g.LifeDomainId!.Value)
                .Select(g => new { LifeDomainId = g.Key, Count = g.Count() }),
            cancellationToken);

        var metrics = metricCounts.ToDictionary(x => x.LifeDomainId, x => x.Count);
        var projects = projectCounts.ToDictionary(x => x.LifeDomainId, x => x.Count);
        var goals = goalCounts.ToDictionary(x => x.LifeDomainId, x => x.Count);

        return domains.Select(d => new LifeDomainSummary(
            d.Id,
            d.Name,
            d.Description,
            d.Color,
            d.Icon,
            d.SortOrder,
            d.IsActive,
            metrics.GetValueOrDefault(d.Id),
            projects.GetValueOrDefault(d.Id),
            goals.GetValueOrDefault(d.Id))).ToList();
    }

    public async Task<Result<LifeDomainDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var domain = await _db.FindLifeDomainAsync(id, cancellationToken);
        if (domain is null)
        {
            return Guard.NotFound<LifeDomainDetail>("حوزه زندگی", id);
        }

        return Result.Success(new LifeDomainDetail(
            domain.Id,
            domain.Name,
            domain.Description,
            domain.Color,
            domain.Icon,
            domain.SortOrder,
            domain.IsActive,
            domain.Metrics
                .OrderBy(m => m.Name)
                .Select(m => new LifeAreaMetricView(
                    m.Id,
                    m.Name,
                    m.Description,
                    m.Unit,
                    m.CurrentValue,
                    m.TargetValue,
                    m.ProgressPercentage,
                    m.MetricType,
                    m.MeasurementFrequency,
                    m.LastMeasuredAtUtc))
                .ToList()));
    }

    public Task<Result<Guid>> CreateAsync(
        CreateLifeDomainRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var domain = new LifeDomain(
                request.Name,
                request.Description,
                request.Color,
                request.Icon,
                request.SortOrder);

            _db.Add(domain);
            await _audit.WriteAsync(
                nameof(LifeDomain),
                domain.Id,
                AuditActionType.Created,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(domain.Id);
        });

    public Task<Result> UpdateAsync(
        UpdateLifeDomainRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var domain = await _db.FindLifeDomainAsync(request.Id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(LifeDomain), request.Id);

            domain.Update(
                request.Name,
                request.Description,
                request.Color,
                request.Icon,
                request.SortOrder);

            await _audit.WriteAsync(
                nameof(LifeDomain),
                domain.Id,
                AuditActionType.Updated,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> SetActiveAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var domain = await _db.FindLifeDomainAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(LifeDomain), id);

            domain.SetActive(isActive);

            await _audit.WriteAsync(
                nameof(LifeDomain),
                domain.Id,
                AuditActionType.Updated,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result<Guid>> AddMetricAsync(
        AddMetricRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var domain = await _db.FindLifeDomainAsync(request.LifeDomainId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(LifeDomain), request.LifeDomainId);

            var metric = domain.AddMetric(
                request.Name,
                request.Description,
                request.Unit,
                request.CurrentValue,
                request.TargetValue,
                request.MetricType,
                request.Frequency);

            await _audit.WriteAsync(
                nameof(LifeAreaMetric),
                metric.Id,
                AuditActionType.Created,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(metric.Id);
        });

    public Task<Result> RecordMetricAsync(
        Guid metricId,
        decimal value,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var metric = await _db.FirstOrDefaultAsync(
                _db.LifeAreaMetrics.Where(m => m.Id == metricId),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(LifeAreaMetric), metricId);

            metric.Record(value, _clock.UtcNow);

            await _audit.WriteAsync(
                nameof(LifeAreaMetric),
                metric.Id,
                AuditActionType.Updated,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        });
}
