using LifeOS.Application.Abstractions;
using LifeOS.Application.Common;
using LifeOS.Domain.Common;
using LifeOS.Domain.Entities.Goals;
using LifeOS.Domain.Enums;

namespace LifeOS.Application.UseCases.Goals;

public sealed record KeyResultView(
    Guid Id,
    string Name,
    string? Description,
    string Unit,
    decimal InitialValue,
    decimal CurrentValue,
    decimal TargetValue,
    decimal Weight,
    decimal? ProgressPercentage,
    DateTimeOffset? LastUpdatedAtUtc);

public sealed record GoalSummary(
    Guid Id,
    string Name,
    Guid? LifeDomainId,
    string? LifeDomainName,
    GoalType GoalType,
    GoalStatus Status,
    PriorityLevel Priority,
    DateOnly? TargetDate,
    decimal ProgressPercentage,
    bool HasMeasurableProgress,
    int LinkedProjectCount,
    bool IsOverdue);

public sealed record GoalDetail(
    Guid Id,
    string Name,
    string? Description,
    Guid? LifeDomainId,
    GoalType GoalType,
    GoalStatus Status,
    PriorityLevel Priority,
    DateOnly? StartDate,
    DateOnly? TargetDate,
    DateTimeOffset? CompletedAtUtc,
    string? WhyItMatters,
    string? SuccessCriteria,
    decimal ProgressPercentage,
    bool HasMeasurableProgress,
    IReadOnlyList<KeyResultView> KeyResults,
    IReadOnlyList<Guid> LinkedProjectIds);

public sealed record CreateGoalRequest(
    string Name,
    Guid? LifeDomainId,
    GoalType GoalType,
    string? Description,
    PriorityLevel Priority,
    DateOnly? StartDate,
    DateOnly? TargetDate,
    string? WhyItMatters,
    string? SuccessCriteria);

public sealed record UpdateGoalRequest(
    Guid Id,
    string Name,
    Guid? LifeDomainId,
    GoalType GoalType,
    string? Description,
    PriorityLevel Priority,
    DateOnly? StartDate,
    DateOnly? TargetDate,
    string? WhyItMatters,
    string? SuccessCriteria);

public sealed record GoalFilter(
    GoalStatus? Status = null,
    Guid? LifeDomainId = null,
    GoalType? GoalType = null);

/// <summary>
/// Goal use cases. Progress is derived from key results whenever any exist; a manual percentage is
/// only accepted for goals without key results, so the number always has a traceable source.
/// </summary>
public sealed class GoalService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;

    public GoalService(IApplicationDbContext db, IAuditWriter audit, IClock clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<GoalSummary>> ListAsync(
        GoalFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Goals.AsQueryable();

        if (filter.Status.HasValue)
        {
            query = query.Where(g => g.Status == filter.Status.Value);
        }

        if (filter.LifeDomainId.HasValue)
        {
            query = query.Where(g => g.LifeDomainId == filter.LifeDomainId.Value);
        }

        if (filter.GoalType.HasValue)
        {
            query = query.Where(g => g.GoalType == filter.GoalType.Value);
        }

        var goals = await _db.ToListAsync(
            query.OrderBy(g => g.Status).ThenBy(g => g.TargetDate).ThenBy(g => g.Name),
            cancellationToken);

        var domainNames = await _db.ToListAsync(
            _db.LifeDomains.Select(d => new { d.Id, d.Name }),
            cancellationToken);

        var keyResultCounts = await _db.ToListAsync(
            _db.GoalKeyResults.GroupBy(k => k.GoalId).Select(g => new { GoalId = g.Key, Count = g.Count() }),
            cancellationToken);

        var projectCounts = await _db.ToListAsync(
            _db.GoalProjects.GroupBy(l => l.GoalId).Select(g => new { GoalId = g.Key, Count = g.Count() }),
            cancellationToken);

        var domains = domainNames.ToDictionary(d => d.Id, d => d.Name);
        var keyResults = keyResultCounts.ToDictionary(k => k.GoalId, k => k.Count);
        var projects = projectCounts.ToDictionary(p => p.GoalId, p => p.Count);
        var today = _clock.TodayUtc;

        return goals.Select(g => new GoalSummary(
            g.Id,
            g.Name,
            g.LifeDomainId,
            g.LifeDomainId.HasValue ? domains.GetValueOrDefault(g.LifeDomainId.Value) : null,
            g.GoalType,
            g.Status,
            g.Priority,
            g.TargetDate,
            g.ProgressPercentage,
            keyResults.GetValueOrDefault(g.Id) > 0,
            projects.GetValueOrDefault(g.Id),
            g.TargetDate.HasValue
                && g.TargetDate.Value < today
                && g.Status is not GoalStatus.Completed and not GoalStatus.Abandoned)).ToList();
    }

    public async Task<Result<GoalDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var goal = await _db.FindGoalAsync(id, cancellationToken);
        if (goal is null)
        {
            return Guard.NotFound<GoalDetail>("هدف", id);
        }

        var linkedProjects = await _db.ToListAsync(
            _db.GoalProjects.Where(l => l.GoalId == id).Select(l => l.ProjectId),
            cancellationToken);

        return Result.Success(new GoalDetail(
            goal.Id,
            goal.Name,
            goal.Description,
            goal.LifeDomainId,
            goal.GoalType,
            goal.Status,
            goal.Priority,
            goal.StartDate,
            goal.TargetDate,
            goal.CompletedAtUtc,
            goal.WhyItMatters,
            goal.SuccessCriteria,
            goal.ProgressPercentage,
            goal.HasMeasurableProgress,
            goal.KeyResults
                .OrderBy(k => k.Name)
                .Select(k => new KeyResultView(
                    k.Id,
                    k.Name,
                    k.Description,
                    k.Unit,
                    k.InitialValue,
                    k.CurrentValue,
                    k.TargetValue,
                    k.Weight,
                    k.ProgressPercentage,
                    k.LastUpdatedAtUtc))
                .ToList(),
            linkedProjects));
    }

    public Task<Result<Guid>> CreateAsync(
        CreateGoalRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var goal = new Goal(request.Name, request.LifeDomainId, request.GoalType, request.Description);

            goal.UpdateDetails(
                request.Name,
                request.LifeDomainId,
                request.GoalType,
                request.Description,
                request.Priority,
                request.StartDate,
                request.TargetDate,
                request.WhyItMatters,
                request.SuccessCriteria);

            _db.Add(goal);
            await _audit.WriteAsync(
                nameof(Goal),
                goal.Id,
                AuditActionType.Created,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(goal.Id);
        });

    public Task<Result> UpdateAsync(
        UpdateGoalRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var goal = await _db.FindGoalAsync(request.Id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Goal), request.Id);

            goal.UpdateDetails(
                request.Name,
                request.LifeDomainId,
                request.GoalType,
                request.Description,
                request.Priority,
                request.StartDate,
                request.TargetDate,
                request.WhyItMatters,
                request.SuccessCriteria);

            await _audit.WriteAsync(
                nameof(Goal),
                goal.Id,
                AuditActionType.Updated,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> ChangeStatusAsync(
        Guid id,
        GoalStatus status,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var goal = await _db.FindGoalAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Goal), id);

            goal.ChangeStatus(status, _clock.UtcNow);

            await _audit.WriteAsync(
                nameof(Goal),
                goal.Id,
                AuditActionType.Updated,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result<Guid>> AddKeyResultAsync(
        Guid goalId,
        string name,
        string? description,
        string unit,
        decimal initialValue,
        decimal targetValue,
        decimal weight,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var goal = await _db.FindGoalAsync(goalId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Goal), goalId);

            var keyResult = goal.AddKeyResult(name, description, unit, initialValue, targetValue, weight);

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(keyResult.Id);
        });

    public Task<Result> RecordKeyResultValueAsync(
        Guid goalId,
        Guid keyResultId,
        decimal value,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var goal = await _db.FindGoalAsync(goalId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Goal), goalId);

            var keyResult = goal.KeyResults.FirstOrDefault(k => k.Id == keyResultId)
                ?? throw new EntityNotFoundException(nameof(GoalKeyResult), keyResultId);

            keyResult.RecordValue(value, _clock.UtcNow);
            goal.RecalculateProgress();

            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> RemoveKeyResultAsync(
        Guid goalId,
        Guid keyResultId,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var goal = await _db.FindGoalAsync(goalId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Goal), goalId);

            var keyResult = goal.KeyResults.FirstOrDefault(k => k.Id == keyResultId)
                ?? throw new EntityNotFoundException(nameof(GoalKeyResult), keyResultId);

            goal.RemoveKeyResult(keyResult);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> SetManualProgressAsync(
        Guid id,
        decimal percentage,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var goal = await _db.FindGoalAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Goal), id);

            goal.SetManualProgress(percentage);
            await _db.SaveChangesAsync(cancellationToken);
        });
}
