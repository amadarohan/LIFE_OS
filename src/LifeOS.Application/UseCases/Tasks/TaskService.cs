using LifeOS.Application.Abstractions;
using LifeOS.Application.Common;
using LifeOS.Application.Engines;
using LifeOS.Domain.Common;
using LifeOS.Domain.Entities.Tasks;
using LifeOS.Domain.Enums;

namespace LifeOS.Application.UseCases.Tasks;

public sealed record TaskSummary(
    Guid Id,
    string Title,
    Guid? ProjectId,
    string? ProjectName,
    Guid? GoalId,
    LifeTaskStatus Status,
    PriorityLevel Priority,
    DateOnly? DueDate,
    DateOnly? ScheduledDate,
    int? EstimatedMinutes,
    int? ActualMinutes,
    bool IsNextAction,
    bool IsOverdue,
    decimal PriorityScore,
    PriorityBand PriorityBand);

public sealed record TaskDetail(
    Guid Id,
    string Title,
    string? Description,
    Guid? ProjectId,
    Guid? GoalId,
    Guid? ParentTaskId,
    LifeTaskStatus Status,
    PriorityLevel Priority,
    DateOnly? DueDate,
    DateOnly? ScheduledDate,
    int? EstimatedMinutes,
    int? ActualMinutes,
    DateTimeOffset? CompletedAtUtc,
    bool IsRecurring,
    string? RecurrenceRule,
    TaskSource Source,
    bool IsNextAction,
    string? DeferReason,
    DateOnly? ReviewDate,
    IReadOnlyList<TimeEntryView> TimeEntries,
    IReadOnlyList<PriorityFactor> PriorityFactors);

public sealed record TimeEntryView(
    Guid Id,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    int DurationMinutes,
    string? Description);

public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    Guid? ProjectId,
    Guid? GoalId,
    Guid? ParentTaskId,
    PriorityLevel Priority,
    DateOnly? DueDate,
    DateOnly? ScheduledDate,
    int? EstimatedMinutes);

public sealed record UpdateTaskRequest(
    Guid Id,
    string Title,
    string? Description,
    Guid? ProjectId,
    Guid? GoalId,
    PriorityLevel Priority,
    DateOnly? DueDate,
    DateOnly? ScheduledDate,
    int? EstimatedMinutes);

public sealed record TaskFilter(
    LifeTaskStatus? Status = null,
    Guid? ProjectId = null,
    Guid? GoalId = null,
    bool? IsNextAction = null,
    bool OverdueOnly = false,
    DateOnly? ScheduledOn = null,
    bool IncludeCompleted = false);

/// <summary>
/// Task use cases. Status transitions, deferral rules and next-action invariants live in the
/// domain; this service adds cross-aggregate checks (project/goal existence, dependency cycles)
/// and the priority ranking used by the dashboard.
/// </summary>
public sealed class TaskService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;
    private readonly PriorityEngine _priorityEngine;

    public TaskService(
        IApplicationDbContext db,
        IAuditWriter audit,
        IClock clock,
        PriorityEngine priorityEngine)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _priorityEngine = priorityEngine;
    }

    public async Task<IReadOnlyList<TaskSummary>> ListAsync(
        TaskFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Tasks.AsQueryable();
        var today = _clock.TodayUtc;

        if (filter.Status.HasValue)
        {
            query = query.Where(t => t.Status == filter.Status.Value);
        }
        else if (!filter.IncludeCompleted)
        {
            query = query.Where(t => t.Status != LifeTaskStatus.Completed
                && t.Status != LifeTaskStatus.Cancelled);
        }

        if (filter.ProjectId.HasValue)
        {
            query = query.Where(t => t.ProjectId == filter.ProjectId.Value);
        }

        if (filter.GoalId.HasValue)
        {
            query = query.Where(t => t.GoalId == filter.GoalId.Value);
        }

        if (filter.IsNextAction.HasValue)
        {
            query = query.Where(t => t.IsNextAction == filter.IsNextAction.Value);
        }

        if (filter.ScheduledOn.HasValue)
        {
            query = query.Where(t => t.ScheduledDate == filter.ScheduledOn.Value);
        }

        if (filter.OverdueOnly)
        {
            query = query.Where(t => t.DueDate != null && t.DueDate < today
                && t.Status != LifeTaskStatus.Completed
                && t.Status != LifeTaskStatus.Cancelled);
        }

        var tasks = await _db.ToListAsync(query, cancellationToken);

        var projectNames = await _db.ToListAsync(
            _db.Projects.Select(p => new { p.Id, p.Name }),
            cancellationToken);

        var blockingCounts = await _db.ToListAsync(
            _db.TaskDependencies
                .GroupBy(d => d.DependsOnTaskId)
                .Select(g => new { TaskId = g.Key, Count = g.Count() }),
            cancellationToken);

        var projects = projectNames.ToDictionary(p => p.Id, p => p.Name);
        var blocking = blockingCounts.ToDictionary(b => b.TaskId, b => b.Count);

        var summaries = tasks.Select(t =>
        {
            var score = _priorityEngine.Evaluate(
                new TaskPriorityInput(
                    t.Priority,
                    t.DueDate,
                    t.GoalId.HasValue,
                    t.ProjectId.HasValue,
                    blocking.GetValueOrDefault(t.Id),
                    t.IsNextAction,
                    t.EstimatedMinutes,
                    t.Status),
                today);

            return new TaskSummary(
                t.Id,
                t.Title,
                t.ProjectId,
                t.ProjectId.HasValue ? projects.GetValueOrDefault(t.ProjectId.Value) : null,
                t.GoalId,
                t.Status,
                t.Priority,
                t.DueDate,
                t.ScheduledDate,
                t.EstimatedMinutes,
                t.ActualMinutes,
                t.IsNextAction,
                t.IsOverdue(today),
                score.Score,
                score.Band);
        });

        // Highest score first, then earliest due date, then oldest created.
        return summaries
            .OrderByDescending(s => s.PriorityScore)
            .ThenBy(s => s.DueDate ?? DateOnly.MaxValue)
            .ThenBy(s => s.Title)
            .ToList();
    }

    public async Task<Result<TaskDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var task = await _db.FindTaskAsync(id, cancellationToken);
        if (task is null)
        {
            return Guard.NotFound<TaskDetail>("کار", id);
        }

        var blockingCount = await _db.CountAsync(
            _db.TaskDependencies.Where(d => d.DependsOnTaskId == id),
            cancellationToken);

        var score = _priorityEngine.Evaluate(
            new TaskPriorityInput(
                task.Priority,
                task.DueDate,
                task.GoalId.HasValue,
                task.ProjectId.HasValue,
                blockingCount,
                task.IsNextAction,
                task.EstimatedMinutes,
                task.Status),
            _clock.TodayUtc);

        return Result.Success(new TaskDetail(
            task.Id,
            task.Title,
            task.Description,
            task.ProjectId,
            task.GoalId,
            task.ParentTaskId,
            task.Status,
            task.Priority,
            task.DueDate,
            task.ScheduledDate,
            task.EstimatedMinutes,
            task.ActualMinutes,
            task.CompletedAtUtc,
            task.IsRecurring,
            task.RecurrenceRule,
            task.Source,
            task.IsNextAction,
            task.DeferReason,
            task.ReviewDate,
            task.TimeEntries
                .OrderByDescending(e => e.StartedAtUtc)
                .Select(e => new TimeEntryView(
                    e.Id,
                    e.StartedAtUtc,
                    e.EndedAtUtc,
                    e.DurationMinutes,
                    e.Description))
                .ToList(),
            score.Factors));
    }

    public Task<Result<Guid>> CreateAsync(
        CreateTaskRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            await EnsureLinksExistAsync(request.ProjectId, request.GoalId, request.ParentTaskId, cancellationToken);

            var task = new TaskItem(request.Title, request.ProjectId, request.GoalId, request.ParentTaskId);

            task.UpdateDetails(
                request.Title,
                request.Description,
                request.ProjectId,
                request.GoalId,
                request.Priority,
                request.DueDate,
                request.ScheduledDate,
                request.EstimatedMinutes);

            _db.Add(task);
            await _audit.WriteAsync(
                nameof(TaskItem),
                task.Id,
                AuditActionType.Created,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(task.Id);
        });

    public Task<Result> UpdateAsync(
        UpdateTaskRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var task = await _db.FindTaskAsync(request.Id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(TaskItem), request.Id);

            await EnsureLinksExistAsync(request.ProjectId, request.GoalId, null, cancellationToken);

            task.UpdateDetails(
                request.Title,
                request.Description,
                request.ProjectId,
                request.GoalId,
                request.Priority,
                request.DueDate,
                request.ScheduledDate,
                request.EstimatedMinutes);

            await _audit.WriteAsync(
                nameof(TaskItem),
                task.Id,
                AuditActionType.Updated,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> ChangeStatusAsync(
        Guid id,
        LifeTaskStatus status,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var task = await _db.FindTaskAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(TaskItem), id);

            task.ChangeStatus(status, _clock.UtcNow);

            await _audit.WriteAsync(
                nameof(TaskItem),
                task.Id,
                AuditActionType.Updated,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> DeferAsync(
        Guid id,
        string reason,
        DateOnly? reviewDate,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var task = await _db.FindTaskAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(TaskItem), id);

            task.Defer(reason, reviewDate);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> SetNextActionAsync(
        Guid id,
        bool isNextAction,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var task = await _db.FindTaskAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(TaskItem), id);

            task.SetNextAction(isNextAction);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> ScheduleAsync(
        Guid id,
        DateOnly? scheduledDate,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var task = await _db.FindTaskAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(TaskItem), id);

            task.Schedule(scheduledDate);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> SetRecurrenceAsync(
        Guid id,
        bool isRecurring,
        string? recurrenceRule,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var task = await _db.FindTaskAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(TaskItem), id);

            task.SetRecurrence(isRecurring, recurrenceRule);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result<Guid>> AddTimeEntryAsync(
        Guid taskId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc,
        string? description,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var task = await _db.FindTaskAsync(taskId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(TaskItem), taskId);

            var entry = task.AddTimeEntry(startedAtUtc, endedAtUtc, description);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(entry.Id);
        });

    /// <summary>
    /// Adds a dependency only when it cannot create a cycle. A cycle would make the task list
    /// permanently unexecutable, so it is rejected at the boundary.
    /// </summary>
    public Task<Result> AddDependencyAsync(
        Guid taskId,
        Guid dependsOnTaskId,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var taskExists = await _db.AnyAsync(_db.Tasks.Where(t => t.Id == taskId), cancellationToken);
            if (!taskExists)
            {
                throw new EntityNotFoundException(nameof(TaskItem), taskId);
            }

            var dependsOnExists = await _db.AnyAsync(
                _db.Tasks.Where(t => t.Id == dependsOnTaskId),
                cancellationToken);

            if (!dependsOnExists)
            {
                throw new EntityNotFoundException(nameof(TaskItem), dependsOnTaskId);
            }

            var edges = await _db.ToListAsync(
                _db.TaskDependencies.Select(d => new { d.TaskId, d.DependsOnTaskId }),
                cancellationToken);

            var graph = edges
                .GroupBy(e => e.TaskId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<Guid>)g.Select(e => e.DependsOnTaskId).ToList());

            var cycle = DependencyGraphGuard.FindCycle(graph, taskId, dependsOnTaskId);
            if (cycle is not null)
            {
                throw new DomainRuleException(
                    "TaskDependency.Cycle",
                    "این وابستگی یک چرخه ایجاد می‌کند و کارها قابل اجرا نخواهند بود.");
            }

            var duplicate = await _db.AnyAsync(
                _db.TaskDependencies.Where(d => d.TaskId == taskId && d.DependsOnTaskId == dependsOnTaskId),
                cancellationToken);

            if (duplicate)
            {
                throw new DomainRuleException("TaskDependency.Duplicate", "این وابستگی قبلاً ثبت شده است.");
            }

            _db.Add(new TaskDependency(taskId, dependsOnTaskId, _clock.UtcNow));
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> RemoveDependencyAsync(
        Guid dependencyId,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var dependency = await _db.FirstOrDefaultAsync(
                _db.TaskDependencies.Where(d => d.Id == dependencyId),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(TaskDependency), dependencyId);

            _db.Remove(dependency);
            await _db.SaveChangesAsync(cancellationToken);
        });

    /// <summary>
    /// Quick capture: creates an inbox task with no project, goal or dates. This is the fastest
    /// path from thought to system, which is what keeps the inbox honest.
    /// </summary>
    public Task<Result<Guid>> QuickCaptureAsync(
        string title,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var task = new TaskItem(title, null, null, null);
            task.MarkSource(TaskSource.QuickCapture);

            _db.Add(task);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(task.Id);
        });

    private async Task EnsureLinksExistAsync(
        Guid? projectId,
        Guid? goalId,
        Guid? parentTaskId,
        CancellationToken cancellationToken)
    {
        if (projectId.HasValue)
        {
            var exists = await _db.AnyAsync(_db.Projects.Where(p => p.Id == projectId.Value), cancellationToken);
            if (!exists)
            {
                throw new EntityNotFoundException("Project", projectId.Value);
            }
        }

        if (goalId.HasValue)
        {
            var exists = await _db.AnyAsync(_db.Goals.Where(g => g.Id == goalId.Value), cancellationToken);
            if (!exists)
            {
                throw new EntityNotFoundException("Goal", goalId.Value);
            }
        }

        if (parentTaskId.HasValue)
        {
            var exists = await _db.AnyAsync(_db.Tasks.Where(t => t.Id == parentTaskId.Value), cancellationToken);
            if (!exists)
            {
                throw new EntityNotFoundException(nameof(TaskItem), parentTaskId.Value);
            }
        }
    }
}
