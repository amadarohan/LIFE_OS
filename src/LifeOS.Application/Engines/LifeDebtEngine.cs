using LifeOS.Domain.Enums;

namespace LifeOS.Application.Engines;

/// <summary>
/// Life Debt = accumulated unresolved obligations. Deliberately reported as a breakdown with
/// age and severity, never collapsed into a single arbitrary score.
/// </summary>
public sealed class LifeDebtEngine
{
    public LifeDebtReport Evaluate(LifeDebtInput input, DateOnly today)
    {
        var items = new List<LifeDebtItem>();

        foreach (var task in input.OverdueTasks)
        {
            var age = today.DayNumber - task.DueDate.DayNumber;
            items.Add(new LifeDebtItem(
                LifeDebtKind.OverdueTask,
                task.Id,
                task.Title,
                age,
                Severity(age, task.Priority),
                $"کار «{task.Title}» {age} روز از مهلت گذشته است."));
        }

        foreach (var commitment in input.OverdueCommitments)
        {
            var age = today.DayNumber - commitment.DueDate.DayNumber;
            items.Add(new LifeDebtItem(
                LifeDebtKind.ForgottenCommitment,
                commitment.Id,
                commitment.Title,
                age,
                Severity(age, commitment.Importance),
                $"تعهد «{commitment.Title}» {age} روز از موعد گذشته است."));
        }

        foreach (var project in input.StagnantProjects)
        {
            items.Add(new LifeDebtItem(
                LifeDebtKind.StagnantProject,
                project.Id,
                project.Name,
                project.DaysSinceActivity,
                project.DaysSinceActivity > 45 ? ImportanceLevel.High : ImportanceLevel.Medium,
                $"پروژه «{project.Name}» {project.DaysSinceActivity} روز بدون فعالیت بوده است."));
        }

        foreach (var decision in input.UnreviewedDecisions)
        {
            var age = today.DayNumber - decision.ReviewDate.DayNumber;
            items.Add(new LifeDebtItem(
                LifeDebtKind.UnreviewedDecision,
                decision.Id,
                decision.Title,
                Math.Max(0, age),
                decision.Importance,
                $"تصمیم «{decision.Title}» منتظر بازبینی است."));
        }

        foreach (var plan in input.UnfinishedPlans)
        {
            items.Add(new LifeDebtItem(
                LifeDebtKind.UnfinishedPlan,
                plan.Id,
                plan.Label,
                plan.DaysSinceEnd,
                ImportanceLevel.Medium,
                $"برنامه «{plan.Label}» بدون بازبینی بسته شده است."));
        }

        foreach (var obligation in input.UpcomingFinancialObligations)
        {
            var daysUntil = obligation.DueDate.DayNumber - today.DayNumber;
            items.Add(new LifeDebtItem(
                LifeDebtKind.FinancialObligation,
                obligation.Id,
                obligation.Name,
                Math.Max(0, -daysUntil),
                daysUntil <= 3 ? ImportanceLevel.Critical : ImportanceLevel.High,
                $"تعهد مالی «{obligation.Name}» {daysUntil} روز دیگر سررسید می‌شود."));
        }

        var byKind = items
            .GroupBy(i => i.Kind)
            .Select(g => new LifeDebtGroup(g.Key, g.Count(), g.Max(i => i.AgeDays), g.Max(i => i.Severity)))
            .OrderByDescending(g => g.Count)
            .ToList();

        return new LifeDebtReport(items, byKind, items.Count);
    }

    private static ImportanceLevel Severity(int ageDays, PriorityLevel priority) => ageDays switch
    {
        >= 30 => ImportanceLevel.Critical,
        >= 14 => ImportanceLevel.High,
        >= 7 => ImportanceLevel.Medium,
        _ => priority >= PriorityLevel.High ? ImportanceLevel.High : ImportanceLevel.Low
    };

    private static ImportanceLevel Severity(int ageDays, ImportanceLevel importance) => ageDays switch
    {
        >= 30 => ImportanceLevel.Critical,
        >= 14 => importance >= ImportanceLevel.High ? ImportanceLevel.Critical : ImportanceLevel.High,
        >= 7 => ImportanceLevel.High,
        _ => importance
    };
}

public sealed record LifeDebtInput(
    IReadOnlyList<OverdueTaskInfo> OverdueTasks,
    IReadOnlyList<OverdueCommitmentInfo> OverdueCommitments,
    IReadOnlyList<StagnantProjectInfo> StagnantProjects,
    IReadOnlyList<UnreviewedDecisionInfo> UnreviewedDecisions,
    IReadOnlyList<UnfinishedPlanInfo> UnfinishedPlans,
    IReadOnlyList<UpcomingObligationInfo> UpcomingFinancialObligations)
{
    public static LifeDebtInput Empty { get; } = new([], [], [], [], [], []);
}

public sealed record OverdueTaskInfo(Guid Id, string Title, DateOnly DueDate, PriorityLevel Priority);
public sealed record OverdueCommitmentInfo(Guid Id, string Title, DateOnly DueDate, ImportanceLevel Importance);
public sealed record StagnantProjectInfo(Guid Id, string Name, int DaysSinceActivity);
public sealed record UnreviewedDecisionInfo(Guid Id, string Title, DateOnly ReviewDate, ImportanceLevel Importance);
public sealed record UnfinishedPlanInfo(Guid Id, string Label, int DaysSinceEnd);
public sealed record UpcomingObligationInfo(Guid Id, string Name, DateOnly DueDate);

public enum LifeDebtKind
{
    OverdueTask = 0,
    ForgottenCommitment = 1,
    StagnantProject = 2,
    UnreviewedDecision = 3,
    UnfinishedPlan = 4,
    FinancialObligation = 5
}

public sealed record LifeDebtItem(
    LifeDebtKind Kind,
    Guid EntityId,
    string Title,
    int AgeDays,
    ImportanceLevel Severity,
    string Explanation);

public sealed record LifeDebtGroup(LifeDebtKind Kind, int Count, int OldestAgeDays, ImportanceLevel WorstSeverity);

public sealed record LifeDebtReport(
    IReadOnlyList<LifeDebtItem> Items,
    IReadOnlyList<LifeDebtGroup> Groups,
    int TotalCount)
{
    public bool IsEmpty => TotalCount == 0;

    public IReadOnlyList<LifeDebtItem> MostUrgent(int take = 5)
        => Items
            .OrderByDescending(i => i.Severity)
            .ThenByDescending(i => i.AgeDays)
            .Take(take)
            .ToList();
}
