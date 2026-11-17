using LifeOS.Domain.Enums;

namespace LifeOS.Application.Engines;

/// <summary>
/// Transparent, configurable priority scoring. Every contribution is returned so the UI can
/// explain *why* an item ranked where it did. Scores are ordinal hints, not objective facts.
/// </summary>
public sealed class PriorityEngine
{
    public PriorityWeights Weights { get; }

    public PriorityEngine(PriorityWeights? weights = null)
    {
        Weights = weights ?? PriorityWeights.Default;
    }

    public PriorityScore Evaluate(TaskPriorityInput input, DateOnly today)
    {
        var factors = new List<PriorityFactor>();

        var priorityPoints = input.Priority switch
        {
            PriorityLevel.Critical => 40m,
            PriorityLevel.High => 30m,
            PriorityLevel.Medium => 18m,
            PriorityLevel.Low => 8m,
            _ => 0m
        };
        factors.Add(new PriorityFactor("priority", "اهمیت تعیین‌شده", priorityPoints, Weights.Priority));

        var urgencyPoints = 0m;
        if (input.DueDate.HasValue)
        {
            var daysUntilDue = input.DueDate.Value.DayNumber - today.DayNumber;
            urgencyPoints = daysUntilDue switch
            {
                < 0 => 35m,
                0 => 30m,
                1 => 24m,
                <= 3 => 18m,
                <= 7 => 12m,
                <= 14 => 6m,
                _ => 2m
            };
        }
        factors.Add(new PriorityFactor("urgency", "نزدیکی مهلت", urgencyPoints, Weights.Urgency));

        var goalPoints = input.HasGoalLink ? 12m : 0m;
        factors.Add(new PriorityFactor("goalAlignment", "هم‌راستایی با هدف", goalPoints, Weights.GoalAlignment));

        var projectPoints = input.HasProjectLink ? 8m : 0m;
        factors.Add(new PriorityFactor("projectImpact", "اثر بر پروژه", projectPoints, Weights.ProjectImpact));

        var dependencyPoints = Math.Min(15m, input.BlockingTaskCount * 5m);
        factors.Add(new PriorityFactor("dependencyImpact", "کارهای وابسته", dependencyPoints, Weights.DependencyImpact));

        var nextActionPoints = input.IsNextAction ? 10m : 0m;
        factors.Add(new PriorityFactor("nextAction", "اقدام بعدی پروژه", nextActionPoints, Weights.NextAction));

        var effortPoints = input.EstimatedMinutes switch
        {
            null => 0m,
            <= 15 => 6m,
            <= 30 => 4m,
            <= 60 => 2m,
            _ => 0m
        };
        factors.Add(new PriorityFactor("quickWin", "قابل انجام در زمان کوتاه", effortPoints, Weights.QuickWin));

        var waitingPenalty = input.Status == LifeTaskStatus.Waiting ? -10m : 0m;
        factors.Add(new PriorityFactor("waiting", "در انتظار دیگران", waitingPenalty, Weights.WaitingPenalty));

        var total = factors.Sum(f => f.Points * f.Weight);
        var normalized = Math.Clamp(Math.Round(total, 1), 0m, 100m);

        return new PriorityScore(normalized, Band(normalized), factors);
    }

    /// <summary>
    /// Coarse banding instead of false precision: a 87.4 vs 86.9 difference is not meaningful,
    /// so the UI shows a band and the factor breakdown.
    /// </summary>
    public static PriorityBand Band(decimal score) => score switch
    {
        >= 70m => PriorityBand.Critical,
        >= 50m => PriorityBand.High,
        >= 30m => PriorityBand.Medium,
        >= 12m => PriorityBand.Low,
        _ => PriorityBand.Minimal
    };
}

public sealed record PriorityWeights(
    decimal Priority,
    decimal Urgency,
    decimal GoalAlignment,
    decimal ProjectImpact,
    decimal DependencyImpact,
    decimal NextAction,
    decimal QuickWin,
    decimal WaitingPenalty)
{
    public static PriorityWeights Default { get; } = new(1.0m, 1.0m, 1.0m, 1.0m, 1.0m, 1.0m, 1.0m, 1.0m);
}

public sealed record TaskPriorityInput(
    PriorityLevel Priority,
    DateOnly? DueDate,
    bool HasGoalLink,
    bool HasProjectLink,
    int BlockingTaskCount,
    bool IsNextAction,
    int? EstimatedMinutes,
    LifeTaskStatus Status);

public sealed record PriorityFactor(string Key, string Label, decimal Points, decimal Weight)
{
    public decimal Contribution => Points * Weight;
}

public sealed record PriorityScore(decimal Score, PriorityBand Band, IReadOnlyList<PriorityFactor> Factors)
{
    /// <summary>Human-readable explanation of the top contributing factors.</summary>
    public IReadOnlyList<string> Explain(int take = 3)
        => Factors
            .Where(f => f.Contribution != 0m)
            .OrderByDescending(f => Math.Abs(f.Contribution))
            .Take(take)
            .Select(f => $"{f.Label}: {(f.Contribution > 0 ? "+" : string.Empty)}{f.Contribution:0.#}")
            .ToList();
}

public enum PriorityBand
{
    Minimal = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}
