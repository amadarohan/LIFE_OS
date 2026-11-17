using LifeOS.Application.Abstractions;
using LifeOS.Application.Common;
using LifeOS.Application.Engines;
using LifeOS.Application.UseCases.Planning;
using LifeOS.Domain.Common;
using LifeOS.Domain.Entities.Analytics;
using LifeOS.Domain.Entities.Planning;
using LifeOS.Domain.Enums;

namespace LifeOS.Application.UseCases.Analytics;

public sealed record DashboardTaskItem(
    Guid Id,
    string Title,
    string? ProjectName,
    DateOnly? DueDate,
    PriorityLevel Priority,
    decimal PriorityScore,
    PriorityBand Band,
    bool IsOverdue);

public sealed record DashboardProjectItem(
    Guid Id,
    string Name,
    decimal ProgressPercentage,
    ProjectHealth Health,
    string Recommendation);

public sealed record DashboardGoalItem(
    Guid Id,
    string Name,
    decimal ProgressPercentage,
    DateOnly? TargetDate,
    bool IsOverdue);

public sealed record DashboardCapacity(
    int AvailableMinutes,
    int UsableMinutes,
    int PlannedMinutes,
    int RemainingMinutes,
    decimal UtilizationPercent,
    CapacityStatus Status);

public sealed record DashboardSnapshot(
    DateOnly Today,
    int InboxCount,
    int OpenTaskCount,
    int OverdueTaskCount,
    int CompletedTodayCount,
    int ActiveProjectCount,
    int ActiveGoalCount,
    int DueCommitmentCount,
    DashboardCapacity Capacity,
    IReadOnlyList<DashboardTaskItem> NextActions,
    IReadOnlyList<DashboardProjectItem> ProjectsNeedingAttention,
    IReadOnlyList<DashboardGoalItem> GoalsAtRisk,
    LifeDebtReport LifeDebt);

public sealed record WeeklyReviewView(
    Guid Id,
    Guid WeeklyPlanId,
    DateOnly ReviewDate,
    int PlannedTasks,
    int CompletedTasks,
    decimal CompletionRate,
    int PlannedMinutes,
    int ActualMinutes,
    string? MainAchievements,
    string? Obstacles,
    string? LessonsLearned,
    string? NextWeekFocus);

/// <summary>
/// Read-side aggregation for the dashboard and weekly review. All numbers come from stored rows;
/// the engines only classify and rank what already exists.
/// </summary>
public sealed class AnalyticsService
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IUserTimeZone _timeZone;
    private readonly PriorityEngine _priorityEngine;
    private readonly CapacityEngine _capacityEngine;
    private readonly LifeDebtEngine _lifeDebtEngine;
    private readonly StagnationEngine _stagnationEngine;

    public AnalyticsService(
        IApplicationDbContext db,
        IClock clock,
        IUserTimeZone timeZone,
        PriorityEngine priorityEngine,
        CapacityEngine capacityEngine,
        LifeDebtEngine lifeDebtEngine,
        StagnationEngine stagnationEngine)
    {
        _db = db;
        _clock = clock;
        _timeZone = timeZone;
        _priorityEngine = priorityEngine;
        _capacityEngine = capacityEngine;
        _lifeDebtEngine = lifeDebtEngine;
        _stagnationEngine = stagnationEngine;
    }

    public async Task<DashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var today = _clock.TodayUtc;

        var tasks = await _db.ToListAsync(_db.Tasks, cancellationToken);
        var projects = await _db.ToListAsync(_db.Projects, cancellationToken);
        var goals = await _db.ToListAsync(_db.Goals, cancellationToken);
        var commitments = await _db.ToListAsync(_db.Commitments, cancellationToken);
        var decisions = await _db.ToListAsync(_db.Decisions, cancellationToken);
        var dependencies = await _db.ToListAsync(_db.TaskDependencies, cancellationToken);
        var projectNames = projects.ToDictionary(p => p.Id, p => p.Name);

        var blockingCounts = dependencies
            .GroupBy(d => d.DependsOnTaskId)
            .ToDictionary(g => g.Key, g => g.Count());

        var openTasks = tasks
            .Where(t => t.Status != LifeTaskStatus.Completed && t.Status != LifeTaskStatus.Cancelled)
            .ToList();

        var scored = openTasks
            .Select(t =>
            {
                var score = _priorityEngine.Evaluate(
                    new TaskPriorityInput(
                        t.Priority,
                        t.DueDate,
                        t.GoalId.HasValue,
                        t.ProjectId.HasValue,
                        blockingCounts.GetValueOrDefault(t.Id),
                        t.IsNextAction,
                        t.EstimatedMinutes,
                        t.Status),
                    today);

                return (Task: t, Score: score);
            })
            .OrderByDescending(x => x.Score.Score)
            .ThenBy(x => x.Task.DueDate ?? DateOnly.MaxValue)
            .ToList();

        var nextActions = scored
            .Take(7)
            .Select(x => new DashboardTaskItem(
                x.Task.Id,
                x.Task.Title,
                x.Task.ProjectId.HasValue ? projectNames.GetValueOrDefault(x.Task.ProjectId.Value) : null,
                x.Task.DueDate,
                x.Task.Priority,
                x.Score.Score,
                x.Score.Band,
                x.Task.IsOverdue(today)))
            .ToList();

        var activeProjects = projects
            .Where(p => p.Status == ProjectStatus.Active && !p.IsArchived)
            .ToList();

        var activeGoals = goals
            .Where(g => g.Status == GoalStatus.Active)
            .ToList();

        var activeGoalIds = activeGoals.Select(g => g.Id).ToHashSet();

        var projectGoalLinks = await _db.ToListAsync(
            _db.GoalProjects.Select(l => new { l.ProjectId, l.GoalId }),
            cancellationToken);

        var projectsWithActiveGoal = projectGoalLinks
            .Where(l => activeGoalIds.Contains(l.GoalId))
            .Select(l => l.ProjectId)
            .ToHashSet();

        var projectAttention = activeProjects
            .Select(p =>
            {
                var projectTasks = openTasks.Where(t => t.ProjectId == p.Id).ToList();
                var daysSinceActivity = Math.Max(0, (today.DayNumber - DateOnly.FromDateTime(p.UpdatedAtUtc.UtcDateTime).DayNumber));

                var assessment = _stagnationEngine.Assess(
                    new ProjectHealthInput(
                        p.Id,
                        p.Name,
                        daysSinceActivity,
                        projectTasks.Any(t => t.IsNextAction),
                        projectTasks.Count,
                        p.Milestones.Count(m => m.Status == MilestoneStatus.Missed),
                        p.TargetEndDate,
                        p.ProgressPercentage,
                        projectsWithActiveGoal.Contains(p.Id),
                        AvailableWeeklyMinutes: 0),
                    today);

                return (Project: p, Assessment: assessment);
            })
            .Where(x => x.Assessment.NeedsAttention)
            .OrderByDescending(x => x.Assessment.Health)
            .Select(x => new DashboardProjectItem(
                x.Project.Id,
                x.Project.Name,
                x.Project.ProgressPercentage,
                x.Assessment.Health,
                x.Assessment.Recommendation.Rationale))
            .ToList();

        var goalsAtRisk = activeGoals
            .Where(g => g.TargetDate.HasValue && g.TargetDate.Value < today && g.ProgressPercentage < 100m)
            .OrderBy(g => g.TargetDate)
            .Select(g => new DashboardGoalItem(
                g.Id,
                g.Name,
                g.ProgressPercentage,
                g.TargetDate,
                true))
            .ToList();

        var capacity = await BuildTodayCapacityAsync(cancellationToken);

        var lifeDebt = _lifeDebtEngine.Evaluate(
            new LifeDebtInput(
                openTasks
                    .Where(t => t.DueDate.HasValue && t.DueDate.Value < today)
                    .Select(t => new OverdueTaskInfo(t.Id, t.Title, t.DueDate!.Value, t.Priority))
                    .ToList(),
                commitments
                    .Where(c => c.DueDate.HasValue
                        && c.DueDate.Value < today
                        && c.Status != CommitmentStatus.Completed
                        && c.Status != CommitmentStatus.Cancelled)
                    .Select(c => new OverdueCommitmentInfo(c.Id, c.Title, c.DueDate!.Value, c.Importance))
                    .ToList(),
                activeProjects
                    .Where(p => (today.DayNumber - DateOnly.FromDateTime(p.UpdatedAtUtc.UtcDateTime).DayNumber) >= 14)
                    .Select(p => new StagnantProjectInfo(
                        p.Id,
                        p.Name,
                        today.DayNumber - DateOnly.FromDateTime(p.UpdatedAtUtc.UtcDateTime).DayNumber))
                    .ToList(),
                decisions
                    .Where(d => d.ReviewDate.HasValue
                        && d.ReviewDate.Value <= today
                        && d.Status == DecisionStatus.Decided)
                    .Select(d => new UnreviewedDecisionInfo(
                        d.Id,
                        d.Title,
                        d.ReviewDate!.Value,
                        d.Importance))
                    .ToList(),
                [],
                commitments
                    .Where(c => c.DueDate.HasValue
                        && c.DueDate.Value >= today
                        && c.DueDate.Value <= today.AddDays(7)
                        && c.Status == CommitmentStatus.Active)
                    .Select(c => new UpcomingObligationInfo(c.Id, c.Title, c.DueDate!.Value))
                    .ToList()),
            today);

        var completedToday = tasks.Count(t =>
            t.CompletedAtUtc.HasValue
            && _timeZone.ToLocalDate(t.CompletedAtUtc.Value) == today);

        return new DashboardSnapshot(
            today,
            tasks.Count(t => t.Status == LifeTaskStatus.Inbox),
            openTasks.Count,
            openTasks.Count(t => t.IsOverdue(today)),
            completedToday,
            activeProjects.Count,
            activeGoals.Count,
            commitments.Count(c => c.Status == CommitmentStatus.Active
                && c.DueDate.HasValue
                && c.DueDate.Value <= today.AddDays(7)),
            capacity,
            nextActions,
            projectAttention,
            goalsAtRisk,
            lifeDebt);
    }

    private async Task<DashboardCapacity> BuildTodayCapacityAsync(CancellationToken cancellationToken)
    {
        var today = _clock.TodayUtc;
        var weekStart = PlanningService.StartOfWeek(today);

        var plan = await _db.FirstOrDefaultAsync(
            _db.WeeklyPlans.Where(p => p.WeekStartDate == weekStart),
            cancellationToken);

        if (plan is null)
        {
            return new DashboardCapacity(0, 0, 0, 0, 0m, CapacityStatus.Empty);
        }

        var daily = await _db.FirstOrDefaultAsync(
            _db.DailyPlans.Where(d => d.WeeklyPlanId == plan.Id && d.PlanDate == today),
            cancellationToken);

        if (daily is null)
        {
            return new DashboardCapacity(0, 0, 0, 0, 0m, CapacityStatus.Empty);
        }

        var blocks = await _db.ToListAsync(
            _db.TimeBlocks.Where(b => b.DailyPlanId == daily.Id),
            cancellationToken);

        var planned = blocks
            .Where(b => b.Status != TimeBlockStatus.Skipped)
            .Sum(b => b.DurationMinutes);

        var assessment = _capacityEngine.Assess(daily.AvailableMinutes, planned);

        return new DashboardCapacity(
            assessment.AvailableMinutes,
            assessment.UsableMinutes,
            assessment.PlannedMinutes,
            assessment.RemainingMinutes,
            assessment.UtilizationPercent,
            assessment.Status);
    }

    /// <summary>
    /// Builds (or refreshes) the weekly review for a plan, computing the metrics from the tasks
    /// that were actually scheduled in that week.
    /// </summary>
    public Task<Result<Guid>> GenerateWeeklyReviewAsync(
        Guid weeklyPlanId,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var plan = await _db.FindWeeklyPlanAsync(weeklyPlanId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(WeeklyPlan), weeklyPlanId);

            var tasks = await _db.ToListAsync(
                _db.Tasks.Where(t => t.ScheduledDate != null
                    && t.ScheduledDate >= plan.WeekStartDate
                    && t.ScheduledDate <= plan.WeekEndDate),
                cancellationToken);

            var plannedTasks = tasks.Count;
            var completedTasks = tasks.Count(t => t.Status == LifeTaskStatus.Completed);
            var plannedMinutes = tasks.Sum(t => t.EstimatedMinutes ?? 0);
            var actualMinutes = tasks.Sum(t => t.ActualMinutes ?? 0);

            var existing = await _db.FirstOrDefaultAsync(
                _db.WeeklyReviews.Where(r => r.WeeklyPlanId == weeklyPlanId),
                cancellationToken);

            if (existing is not null)
            {
                existing.RecordMetrics(plannedTasks, completedTasks, plannedMinutes, actualMinutes);
                await _db.SaveChangesAsync(cancellationToken);
                return Result.Success(existing.Id);
            }

            var review = new WeeklyReview(weeklyPlanId, plan.WeekEndDate, _clock.UtcNow);
            review.RecordMetrics(plannedTasks, completedTasks, plannedMinutes, actualMinutes);

            _db.Add(review);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(review.Id);
        });

    public Task<Result> RecordWeeklyReflectionAsync(
        Guid reviewId,
        string? achievements,
        string? obstacles,
        string? lessons,
        string? nextWeekFocus,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var review = await _db.FirstOrDefaultAsync(
                _db.WeeklyReviews.Where(r => r.Id == reviewId),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(WeeklyReview), reviewId);

            review.RecordReflection(achievements, obstacles, lessons, nextWeekFocus);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public async Task<IReadOnlyList<WeeklyReviewView>> ListWeeklyReviewsAsync(
        int take = 12,
        CancellationToken cancellationToken = default)
    {
        var reviews = await _db.ToListAsync(
            _db.WeeklyReviews.OrderByDescending(r => r.ReviewDate).Take(take),
            cancellationToken);

        return reviews
            .Select(r => new WeeklyReviewView(
                r.Id,
                r.WeeklyPlanId,
                r.ReviewDate,
                r.PlannedTasks,
                r.CompletedTasks,
                r.CompletionRate,
                r.PlannedMinutes,
                r.ActualMinutes,
                r.MainAchievements,
                r.Obstacles,
                r.LessonsLearned,
                r.NextWeekFocus))
            .ToList();
    }

    /// <summary>
    /// Captures a progress snapshot for every active project and goal. Snapshots are append-only
    /// so trend lines cannot be rewritten retroactively.
    /// </summary>
    public Task<Result<int>> CaptureSnapshotsAsync(CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var today = _clock.TodayUtc;
            var now = _clock.UtcNow;

            var alreadyCaptured = await _db.AnyAsync(
                _db.ProgressSnapshots.Where(s => s.SnapshotDate == today),
                cancellationToken);

            if (alreadyCaptured)
            {
                throw new DomainRuleException(
                    "ProgressSnapshot.AlreadyCaptured",
                    "برای امروز قبلاً تصویر وضعیت ثبت شده است.");
            }

            var projects = await _db.ToListAsync(
                _db.Projects.Where(p => !p.IsArchived),
                cancellationToken);

            var goals = await _db.ToListAsync(_db.Goals, cancellationToken);

            var snapshots = new List<ProgressSnapshot>();

            snapshots.AddRange(projects.Select(p => new ProgressSnapshot(
                today,
                SnapshotEntityType.Project,
                p.Id,
                p.ProgressPercentage,
                p.Status.ToString(),
                null,
                now)));

            snapshots.AddRange(goals.Select(g => new ProgressSnapshot(
                today,
                SnapshotEntityType.Goal,
                g.Id,
                g.ProgressPercentage,
                g.Status.ToString(),
                null,
                now)));

            if (snapshots.Count == 0)
            {
                return Result.Success(0);
            }

            _db.AddRange(snapshots);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(snapshots.Count);
        });

    public async Task<IReadOnlyList<ProgressSnapshot>> ListSnapshotsAsync(
        SnapshotEntityType entityType,
        Guid entityId,
        int take = 30,
        CancellationToken cancellationToken = default)
        => await _db.ToListAsync(
            _db.ProgressSnapshots
                .Where(s => s.EntityType == entityType && s.EntityId == entityId)
                .OrderByDescending(s => s.SnapshotDate)
                .Take(take),
            cancellationToken);
}
