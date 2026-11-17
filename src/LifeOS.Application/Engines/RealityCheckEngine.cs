namespace LifeOS.Application.Engines;

/// <summary>
/// Compares planned against actual to surface estimation error. Reports facts and a neutral
/// observation — never a judgement about the user.
/// </summary>
public sealed class RealityCheckEngine
{
    public RealityCheckReport Evaluate(RealityCheckInput input)
    {
        var observations = new List<RealityObservation>();

        if (input.PlannedTasks > 0)
        {
            var completionRate = Math.Round(input.CompletedTasks * 100m / input.PlannedTasks, 1);
            observations.Add(new RealityObservation(
                RealityDimension.TaskCompletion,
                $"از {input.PlannedTasks} کار برنامه‌ریزی‌شده، {input.CompletedTasks} کار انجام شد ({completionRate:0.#}٪).",
                completionRate));
        }

        if (input.EstimatedMinutes > 0 && input.ActualMinutes > 0)
        {
            var ratio = Math.Round(input.ActualMinutes / (decimal)input.EstimatedMinutes, 2);
            var direction = ratio > 1m ? "بیشتر" : "کمتر";
            observations.Add(new RealityObservation(
                RealityDimension.TimeEstimation,
                $"زمان واقعی {Math.Abs(ratio - 1m) * 100m:0}٪ {direction} از تخمین بود (تخمین {input.EstimatedMinutes} دقیقه، واقعی {input.ActualMinutes} دقیقه).",
                ratio * 100m));
        }

        if (input.EstimatedEffortHours > 0m && input.ActualEffortHours > 0m)
        {
            var ratio = Math.Round(input.ActualEffortHours / input.EstimatedEffortHours, 2);
            observations.Add(new RealityObservation(
                RealityDimension.EffortEstimation,
                $"تلاش واقعی {ratio:0.##} برابر تخمین بود.",
                ratio * 100m));
        }

        if (input.ExpectedProgress is not null && input.ActualProgress is not null)
        {
            var gap = Math.Round(input.ActualProgress.Value - input.ExpectedProgress.Value, 1);
            observations.Add(new RealityObservation(
                RealityDimension.ProgressGap,
                gap >= 0
                    ? $"پیشرفت واقعی {gap:0.#} واحد جلوتر از انتظار است."
                    : $"پیشرفت واقعی {Math.Abs(gap):0.#} واحد عقب‌تر از انتظار است.",
                gap));
        }

        var bias = DetectBias(input);
        return new RealityCheckReport(observations, bias);
    }

    /// <summary>
    /// Recurring estimation error across a set of completed items. Requires at least three
    /// samples so a single outlier is not presented as a pattern.
    /// </summary>
    public EstimationBias? DetectBias(RealityCheckInput input)
    {
        if (input.HistoricalEstimates.Count < 3)
        {
            return null;
        }

        var ratios = input.HistoricalEstimates
            .Where(h => h.EstimatedMinutes > 0 && h.ActualMinutes > 0)
            .Select(h => h.ActualMinutes / (decimal)h.EstimatedMinutes)
            .ToList();

        if (ratios.Count < 3)
        {
            return null;
        }

        var median = Median(ratios);
        if (median >= 1.25m)
        {
            return new EstimationBias(
                BiasDirection.Underestimate,
                median,
                $"به‌طور معمول زمان واقعی حدود {median:0.##} برابر تخمین شماست؛ تخمین‌ها را بزرگ‌تر بگیرید.");
        }

        if (median <= 0.75m)
        {
            return new EstimationBias(
                BiasDirection.Overestimate,
                median,
                $"به‌طور معمول زمان واقعی حدود {median:0.##} برابر تخمین شماست؛ تخمین‌ها بیش از حد محتاطانه‌اند.");
        }

        return new EstimationBias(BiasDirection.Calibrated, median, "تخمین‌های شما با واقعیت هم‌خوان است.");
    }

    private static decimal Median(List<decimal> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2m
            : sorted[mid];
    }
}

public sealed record RealityCheckInput(
    int PlannedTasks,
    int CompletedTasks,
    int EstimatedMinutes,
    int ActualMinutes,
    decimal EstimatedEffortHours,
    decimal ActualEffortHours,
    decimal? ExpectedProgress,
    decimal? ActualProgress,
    IReadOnlyList<HistoricalEstimate> HistoricalEstimates)
{
    public static RealityCheckInput Empty { get; } = new(0, 0, 0, 0, 0m, 0m, null, null, []);
}

public sealed record HistoricalEstimate(int EstimatedMinutes, int ActualMinutes);

public sealed record RealityObservation(RealityDimension Dimension, string Description, decimal Value);

public enum RealityDimension
{
    TaskCompletion = 0,
    TimeEstimation = 1,
    EffortEstimation = 2,
    ProgressGap = 3
}

public sealed record EstimationBias(BiasDirection Direction, decimal MedianRatio, string Explanation);

public enum BiasDirection
{
    Calibrated = 0,
    Underestimate = 1,
    Overestimate = 2
}

public sealed record RealityCheckReport(
    IReadOnlyList<RealityObservation> Observations,
    EstimationBias? Bias)
{
    public bool HasData => Observations.Count > 0;
}
