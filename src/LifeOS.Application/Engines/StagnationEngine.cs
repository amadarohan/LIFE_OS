using LifeOS.Domain.Enums;

namespace LifeOS.Application.Engines;

/// <summary>
/// Detects projects that are not moving. Never changes status — it only produces a
/// recommendation the user must act on.
/// </summary>
public sealed class StagnationEngine
{
    public StagnationSettings Settings { get; }

    public StagnationEngine(StagnationSettings? settings = null)
    {
        Settings = settings ?? StagnationSettings.Default;
    }

    public StagnationAssessment Assess(ProjectHealthInput input, DateOnly today)
    {
        var reasons = new List<StagnationReason>();

        if (input.DaysSinceActivity >= Settings.NoActivityDays)
        {
            reasons.Add(new StagnationReason(
                StagnationCause.NoRecentActivity,
                $"هیچ فعالیتی در {input.DaysSinceActivity} روز گذشته ثبت نشده است."));
        }

        if (!input.HasNextAction && input.OpenTaskCount == 0)
        {
            reasons.Add(new StagnationReason(
                StagnationCause.NoNextAction,
                "پروژه نه اقدام بعدی دارد و نه کار باز."));
        }

        if (input.MissedMilestoneCount >= Settings.MissedMilestoneThreshold)
        {
            reasons.Add(new StagnationReason(
                StagnationCause.MissedMilestones,
                $"{input.MissedMilestoneCount} نقطه‌عطف از موعد گذشته و تکمیل نشده است."));
        }

        if (input.TargetEndDate.HasValue && input.TargetEndDate.Value < today && input.ProgressPercentage < 100m)
        {
            var overdueDays = today.DayNumber - input.TargetEndDate.Value.DayNumber;
            reasons.Add(new StagnationReason(
                StagnationCause.PastTargetDate,
                $"تاریخ پایان پروژه {overdueDays} روز گذشته و پیشرفت {input.ProgressPercentage:0}٪ است."));
        }

        if (!input.SupportsActiveGoal)
        {
            reasons.Add(new StagnationReason(
                StagnationCause.NoActiveGoal,
                "این پروژه به هیچ هدف فعالی متصل نیست."));
        }

        if (input.AvailableWeeklyMinutes <= 0 && input.OpenTaskCount > 0)
        {
            reasons.Add(new StagnationReason(
                StagnationCause.NoCapacity,
                "ظرفیت هفتگی باقی‌مانده‌ای برای این پروژه وجود ندارد."));
        }

        var health = reasons.Count switch
        {
            0 => ProjectHealth.Healthy,
            1 => ProjectHealth.Watch,
            2 => ProjectHealth.AtRisk,
            _ => ProjectHealth.Stagnant
        };

        return new StagnationAssessment(health, reasons, Recommend(health, reasons));
    }

    private static StagnationRecommendation Recommend(ProjectHealth health, IReadOnlyList<StagnationReason> reasons)
    {
        if (health == ProjectHealth.Healthy)
        {
            return new StagnationRecommendation(StagnationAction.Continue, "پروژه در مسیر درست است.");
        }

        if (reasons.Any(r => r.Cause == StagnationCause.NoActiveGoal))
        {
            return new StagnationRecommendation(
                StagnationAction.Replan,
                "ابتدا مشخص کنید این پروژه به کدام هدف فعال خدمت می‌کند یا آن را متوقف کنید.");
        }

        if (reasons.Any(r => r.Cause == StagnationCause.NoCapacity))
        {
            return new StagnationRecommendation(
                StagnationAction.Pause,
                "ظرفیت واقعی برای این پروژه وجود ندارد؛ آن را موقتاً متوقف کنید تا کارهای دیگر تمام شوند.");
        }

        if (reasons.Any(r => r.Cause == StagnationCause.MissedMilestones || r.Cause == StagnationCause.PastTargetDate))
        {
            return new StagnationRecommendation(
                StagnationAction.ReduceScope,
                "دامنه یا تاریخ‌ها واقع‌بینانه نیستند؛ محدوده را کوچک‌تر کنید.");
        }

        return new StagnationRecommendation(
            StagnationAction.Replan,
            "یک اقدام بعدی مشخص تعیین کنید و زمان واقعی برای آن رزرو کنید.");
    }
}

public sealed record StagnationSettings(int NoActivityDays, int MissedMilestoneThreshold)
{
    public static StagnationSettings Default { get; } = new(14, 2);
}

public sealed record ProjectHealthInput(
    Guid ProjectId,
    string Name,
    int DaysSinceActivity,
    bool HasNextAction,
    int OpenTaskCount,
    int MissedMilestoneCount,
    DateOnly? TargetEndDate,
    decimal ProgressPercentage,
    bool SupportsActiveGoal,
    int AvailableWeeklyMinutes);

public sealed record StagnationReason(StagnationCause Cause, string Explanation);

public sealed record StagnationAssessment(
    ProjectHealth Health,
    IReadOnlyList<StagnationReason> Reasons,
    StagnationRecommendation Recommendation)
{
    public bool NeedsAttention => Health != ProjectHealth.Healthy;
}

public enum ProjectHealth
{
    Healthy = 0,
    Watch = 1,
    AtRisk = 2,
    Stagnant = 3
}

public enum StagnationCause
{
    NoRecentActivity = 0,
    NoNextAction = 1,
    MissedMilestones = 2,
    PastTargetDate = 3,
    NoActiveGoal = 4,
    NoCapacity = 5
}

public enum StagnationAction
{
    Continue = 0,
    Replan = 1,
    ReduceScope = 2,
    Pause = 3,
    Cancel = 4
}

public sealed record StagnationRecommendation(StagnationAction Action, string Rationale);
