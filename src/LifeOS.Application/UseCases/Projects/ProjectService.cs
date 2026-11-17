using LifeOS.Application.Abstractions;
using LifeOS.Application.Common;
using LifeOS.Application.Engines;
using LifeOS.Domain.Common;
using LifeOS.Domain.Entities.Projects;
using LifeOS.Domain.Enums;

namespace LifeOS.Application.UseCases.Projects;

public sealed record ProjectSummary(
    Guid Id,
    string Name,
    Guid? LifeDomainId,
    string? LifeDomainName,
    ProjectStatus Status,
    PriorityLevel Priority,
    DateOnly? StartDate,
    DateOnly? TargetEndDate,
    decimal ProgressPercentage,
    int MilestoneCount,
    int CompletedMilestoneCount,
    int OpenTaskCount,
    bool IsPinned,
    bool IsArchived,
    bool IsOverdue);

public sealed record MilestoneView(
    Guid Id,
    string Name,
    string? Description,
    MilestoneStatus Status,
    DateOnly? DueDate,
    DateTimeOffset? CompletedAtUtc,
    int SortOrder);

public sealed record ProjectDetail(
    Guid Id,
    string Name,
    string? Description,
    Guid? LifeDomainId,
    ProjectStatus Status,
    PriorityLevel Priority,
    DateOnly? StartDate,
    DateOnly? TargetEndDate,
    DateTimeOffset? CompletedAtUtc,
    decimal? EstimatedBudget,
    decimal? ActualBudget,
    string CurrencyCode,
    decimal? EstimatedEffortHours,
    decimal? ActualEffortHours,
    decimal ProgressPercentage,
    bool IsPinned,
    bool IsArchived,
    string? Notes,
    IReadOnlyList<MilestoneView> Milestones,
    IReadOnlyList<Guid> LinkedGoalIds);

public sealed record CreateProjectRequest(
    string Name,
    Guid? LifeDomainId,
    string? Description,
    PriorityLevel Priority,
    DateOnly? StartDate,
    DateOnly? TargetEndDate,
    decimal? EstimatedBudget,
    string CurrencyCode,
    decimal? EstimatedEffortHours,
    string? Notes);

public sealed record UpdateProjectRequest(
    Guid Id,
    string Name,
    Guid? LifeDomainId,
    string? Description,
    PriorityLevel Priority,
    DateOnly? StartDate,
    DateOnly? TargetEndDate,
    decimal? EstimatedBudget,
    string CurrencyCode,
    decimal? EstimatedEffortHours,
    string? Notes);

public sealed record ProjectFilter(
    ProjectStatus? Status = null,
    Guid? LifeDomainId = null,
    bool IncludeArchived = false,
    bool PinnedOnly = false);

/// <summary>
/// Project use cases. Status transitions, progress derivation and dependency cycles are all
/// enforced here so the UI cannot bypass them.
/// </summary>
public sealed class ProjectService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;

    public ProjectService(IApplicationDbContext db, IAuditWriter audit, IClock clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<ProjectSummary>> ListAsync(
        ProjectFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Projects.AsQueryable();

        if (!filter.IncludeArchived)
        {
            query = query.Where(p => !p.IsArchived);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(p => p.Status == filter.Status.Value);
        }

        if (filter.LifeDomainId.HasValue)
        {
            query = query.Where(p => p.LifeDomainId == filter.LifeDomainId.Value);
        }

        if (filter.PinnedOnly)
        {
            query = query.Where(p => p.IsPinned);
        }

        var projects = await _db.ToListAsync(
            query.OrderByDescending(p => p.IsPinned)
                .ThenBy(p => p.Status)
                .ThenBy(p => p.TargetEndDate)
                .ThenBy(p => p.Name),
            cancellationToken);

        var domainNames = await _db.ToListAsync(
            _db.LifeDomains.Select(d => new { d.Id, d.Name }),
            cancellationToken);

        var milestoneStats = await _db.ToListAsync(
            _db.ProjectMilestones
                .GroupBy(m => m.ProjectId)
                .Select(g => new
                {
                    ProjectId = g.Key,
                    Total = g.Count(),
                    Completed = g.Count(m => m.Status == MilestoneStatus.Completed)
                }),
            cancellationToken);

        var openTaskStats = await _db.ToListAsync(
            _db.Tasks
                .Where(t => t.ProjectId != null
                    && (t.Status == LifeTaskStatus.Inbox
                        || t.Status == LifeTaskStatus.Todo
                        || t.Status == LifeTaskStatus.InProgress
                        || t.Status == LifeTaskStatus.Waiting
                        || t.Status == LifeTaskStatus.Deferred))
                .GroupBy(t => t.ProjectId!.Value)
                .Select(g => new { ProjectId = g.Key, Count = g.Count() }),
            cancellationToken);

        var domains = domainNames.ToDictionary(d => d.Id, d => d.Name);
        var milestones = milestoneStats.ToDictionary(m => m.ProjectId);
        var openTasks = openTaskStats.ToDictionary(t => t.ProjectId, t => t.Count);
        var today = _clock.TodayUtc;

        return projects.Select(p =>
        {
            milestones.TryGetValue(p.Id, out var stats);
            return new ProjectSummary(
                p.Id,
                p.Name,
                p.LifeDomainId,
                p.LifeDomainId.HasValue ? domains.GetValueOrDefault(p.LifeDomainId.Value) : null,
                p.Status,
                p.Priority,
                p.StartDate,
                p.TargetEndDate,
                p.ProgressPercentage,
                stats?.Total ?? 0,
                stats?.Completed ?? 0,
                openTasks.GetValueOrDefault(p.Id),
                p.IsPinned,
                p.IsArchived,
                p.TargetEndDate.HasValue
                    && p.TargetEndDate.Value < today
                    && p.Status is not ProjectStatus.Completed and not ProjectStatus.Cancelled
                    && !p.IsArchived);
        }).ToList();
    }

    public async Task<Result<ProjectDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await _db.FindProjectAsync(id, cancellationToken);
        if (project is null)
        {
            return Guard.NotFound<ProjectDetail>("پروژه", id);
        }

        return Result.Success(new ProjectDetail(
            project.Id,
            project.Name,
            project.Description,
            project.LifeDomainId,
            project.Status,
            project.Priority,
            project.StartDate,
            project.TargetEndDate,
            project.CompletedAtUtc,
            project.EstimatedBudget,
            project.ActualBudget,
            project.CurrencyCode,
            project.EstimatedEffortHours,
            project.ActualEffortHours,
            project.ProgressPercentage,
            project.IsPinned,
            project.IsArchived,
            project.Notes,
            project.Milestones
                .OrderBy(m => m.SortOrder)
                .Select(m => new MilestoneView(
                    m.Id,
                    m.Name,
                    m.Description,
                    m.Status,
                    m.DueDate,
                    m.CompletedAtUtc,
                    m.SortOrder))
                .ToList(),
            project.GoalLinks.Select(l => l.GoalId).ToList()));
    }

    public Task<Result<Guid>> CreateAsync(
        CreateProjectRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var project = new Project(request.Name, request.LifeDomainId, request.Description);

            project.UpdateDetails(
                request.Name,
                request.LifeDomainId,
                request.Description,
                request.Priority,
                request.StartDate,
                request.TargetEndDate,
                request.EstimatedBudget,
                request.CurrencyCode,
                request.EstimatedEffortHours,
                request.Notes);

            _db.Add(project);
            await _audit.WriteAsync(
                nameof(Project),
                project.Id,
                AuditActionType.Created,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(project.Id);
        });

    public Task<Result> UpdateAsync(
        UpdateProjectRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var project = await _db.FindProjectAsync(request.Id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Project), request.Id);

            project.UpdateDetails(
                request.Name,
                request.LifeDomainId,
                request.Description,
                request.Priority,
                request.StartDate,
                request.TargetEndDate,
                request.EstimatedBudget,
                request.CurrencyCode,
                request.EstimatedEffortHours,
                request.Notes);

            await _audit.WriteAsync(
                nameof(Project),
                project.Id,
                AuditActionType.Updated,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> ChangeStatusAsync(
        Guid id,
        ProjectStatus status,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var project = await _db.FindProjectAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Project), id);

            project.ChangeStatus(status, _clock.UtcNow);

            await _audit.WriteAsync(
                nameof(Project),
                project.Id,
                AuditActionType.Updated,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> SetPinnedAsync(
        Guid id,
        bool isPinned,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var project = await _db.FindProjectAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Project), id);

            project.SetPinned(isPinned);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> ArchiveAsync(Guid id, CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var project = await _db.FindProjectAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Project), id);

            project.Archive();

            await _audit.WriteAsync(
                nameof(Project),
                project.Id,
                AuditActionType.Updated,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var project = await _db.FindProjectAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Project), id);

            project.Restore();

            await _audit.WriteAsync(
                nameof(Project),
                project.Id,
                AuditActionType.Restored,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result<Guid>> AddMilestoneAsync(
        Guid projectId,
        string name,
        string? description,
        DateOnly? dueDate,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var project = await _db.FindProjectAsync(projectId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Project), projectId);

            var sortOrder = project.Milestones.Count == 0
                ? 0
                : project.Milestones.Max(m => m.SortOrder) + 1;

            var milestone = project.AddMilestone(name, description, dueDate, sortOrder);
            project.RecalculateProgress();

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(milestone.Id);
        });

    public Task<Result> ChangeMilestoneStatusAsync(
        Guid projectId,
        Guid milestoneId,
        MilestoneStatus status,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var project = await _db.FindProjectAsync(projectId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Project), projectId);

            var milestone = project.Milestones.FirstOrDefault(m => m.Id == milestoneId)
                ?? throw new EntityNotFoundException(nameof(ProjectMilestone), milestoneId);

            milestone.ChangeStatus(status, _clock.UtcNow);
            project.RecalculateProgress();

            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> RemoveMilestoneAsync(
        Guid projectId,
        Guid milestoneId,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var project = await _db.FindProjectAsync(projectId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Project), projectId);

            var milestone = project.Milestones.FirstOrDefault(m => m.Id == milestoneId)
                ?? throw new EntityNotFoundException(nameof(ProjectMilestone), milestoneId);

            project.RemoveMilestone(milestone);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> SetManualProgressAsync(
        Guid id,
        decimal percentage,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var project = await _db.FindProjectAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Project), id);

            project.SetManualProgress(percentage);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> LinkGoalAsync(
        Guid projectId,
        Guid goalId,
        decimal contributionWeight,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var project = await _db.FindProjectAsync(projectId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Project), projectId);

            var goalExists = await _db.AnyAsync(_db.Goals.Where(g => g.Id == goalId), cancellationToken);
            if (!goalExists)
            {
                throw new EntityNotFoundException("Goal", goalId);
            }

            project.LinkGoal(goalId, contributionWeight, _clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> UnlinkGoalAsync(
        Guid projectId,
        Guid goalId,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var project = await _db.FindProjectAsync(projectId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Project), projectId);

            project.UnlinkGoal(goalId);
            await _db.SaveChangesAsync(cancellationToken);
        });

    /// <summary>
    /// Adds a dependency only when it cannot create a cycle. The graph is loaded once and checked
    /// in memory, which is correct because the whole graph is small and single-user.
    /// </summary>
    public Task<Result> AddDependencyAsync(
        Guid projectId,
        Guid dependsOnProjectId,
        DependencyType dependencyType,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var projectExists = await _db.AnyAsync(_db.Projects.Where(p => p.Id == projectId), cancellationToken);
            if (!projectExists)
            {
                throw new EntityNotFoundException(nameof(Project), projectId);
            }

            var dependsOnExists = await _db.AnyAsync(
                _db.Projects.Where(p => p.Id == dependsOnProjectId),
                cancellationToken);

            if (!dependsOnExists)
            {
                throw new EntityNotFoundException(nameof(Project), dependsOnProjectId);
            }

            var edges = await _db.ToListAsync(
                _db.ProjectDependencies.Select(d => new { d.ProjectId, d.DependsOnProjectId }),
                cancellationToken);

            var graph = edges
                .GroupBy(e => e.ProjectId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<Guid>)g.Select(e => e.DependsOnProjectId).ToList());

            var cycle = DependencyGraphGuard.FindCycle(graph, projectId, dependsOnProjectId);
            if (cycle is not null)
            {
                throw new DomainRuleException(
                    "ProjectDependency.Cycle",
                    "این وابستگی یک چرخه ایجاد می‌کند و پروژه‌ها قابل اجرا نخواهند بود.");
            }

            var duplicate = await _db.AnyAsync(
                _db.ProjectDependencies.Where(d =>
                    d.ProjectId == projectId && d.DependsOnProjectId == dependsOnProjectId),
                cancellationToken);

            if (duplicate)
            {
                throw new DomainRuleException(
                    "ProjectDependency.Duplicate",
                    "این وابستگی قبلاً ثبت شده است.");
            }

            _db.Add(new ProjectDependency(projectId, dependsOnProjectId, dependencyType, _clock.UtcNow));
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> RemoveDependencyAsync(
        Guid dependencyId,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var dependency = await _db.FirstOrDefaultAsync(
                _db.ProjectDependencies.Where(d => d.Id == dependencyId),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(ProjectDependency), dependencyId);

            _db.Remove(dependency);
            await _db.SaveChangesAsync(cancellationToken);
        });
}
