using LifeOS.Domain.Common;
using LifeOS.Domain.Entities.Planning;
using LifeOS.Domain.Enums;

namespace LifeOS.Domain.Entities.Analytics;

public sealed class ProgressSnapshot : Entity
{
    private ProgressSnapshot() { }

    public ProgressSnapshot(
        DateOnly snapshotDate,
        SnapshotEntityType entityType,
        Guid entityId,
        decimal progressPercentage,
        string status,
        string? metricsJson,
        DateTimeOffset createdAtUtc)
    {
        SnapshotDate = snapshotDate;
        EntityType = entityType;
        EntityId = entityId;
        ProgressPercentage = Math.Clamp(Math.Round(progressPercentage, 2), 0m, 100m);
        Status = status;
        MetricsJson = metricsJson;
        CreatedAtUtc = createdAtUtc;
    }

    public DateOnly SnapshotDate { get; private set; }
    public SnapshotEntityType EntityType { get; private set; }
    public Guid EntityId { get; private set; }
    public decimal ProgressPercentage { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string? MetricsJson { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}

public sealed class WeeklyReview : Entity
{
    private WeeklyReview() { }

    public WeeklyReview(Guid weeklyPlanId, DateOnly reviewDate, DateTimeOffset createdAtUtc)
    {
        WeeklyPlanId = weeklyPlanId;
        ReviewDate = reviewDate;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid WeeklyPlanId { get; private set; }
    public DateOnly ReviewDate { get; private set; }
    public int PlannedTasks { get; private set; }
    public int CompletedTasks { get; private set; }
    public decimal CompletionRate { get; private set; }
    public int PlannedMinutes { get; private set; }
    public int ActualMinutes { get; private set; }
    public string? MainAchievements { get; private set; }
    public string? Obstacles { get; private set; }
    public string? LessonsLearned { get; private set; }
    public string? NextWeekFocus { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public WeeklyPlan? WeeklyPlan { get; private set; }

    public void RecordMetrics(int plannedTasks, int completedTasks, int plannedMinutes, int actualMinutes)
    {
        PlannedTasks = plannedTasks;
        CompletedTasks = completedTasks;
        PlannedMinutes = plannedMinutes;
        ActualMinutes = actualMinutes;
        CompletionRate = plannedTasks == 0
            ? 0m
            : Math.Round(completedTasks * 100m / plannedTasks, 2);
    }

    public void RecordReflection(string? achievements, string? obstacles, string? lessons, string? nextWeekFocus)
    {
        MainAchievements = achievements;
        Obstacles = obstacles;
        LessonsLearned = lessons;
        NextWeekFocus = nextWeekFocus;
    }
}
