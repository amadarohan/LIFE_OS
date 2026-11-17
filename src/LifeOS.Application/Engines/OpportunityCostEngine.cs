namespace LifeOS.Application.Engines;

/// <summary>
/// Evaluates what a new commitment would cost. Produces multiple scenarios with explicit
/// assumptions instead of a single recommendation presented as fact.
/// </summary>
public sealed class OpportunityCostEngine
{
    public OpportunityCostAnalysis Analyze(OpportunityCostInput input)
    {
        var assumptions = new List<string>
        {
            $"ظرفیت هفتگی قابل استفاده: {input.UsableWeeklyMinutes / 60m:0.#} ساعت",
            $"بار کاری فعلی برنامه‌ریزی‌شده: {input.PlannedWeeklyMinutes / 60m:0.#} ساعت",
            $"نیاز تخمینی مورد جدید: {input.NewCommitmentWeeklyMinutes / 60m:0.#} ساعت در هفته"
        };

        var freeMinutes = input.UsableWeeklyMinutes - input.PlannedWeeklyMinutes;
        var deficit = input.NewCommitmentWeeklyMinutes - freeMinutes;

        var scenarios = new List<OpportunityScenario>();

        if (deficit <= 0)
        {
            scenarios.Add(new OpportunityScenario(
                OpportunityScenarioKind.StartNow,
                "شروع همین حالا",
                "ظرفیت خالی کافی وجود دارد.",
                [],
                "بدون تأخیر در کارهای فعلی",
                RiskLevel.Low));
        }
        else
        {
            scenarios.Add(new OpportunityScenario(
                OpportunityScenarioKind.StartNow,
                "شروع همین حالا",
                $"حدود {deficit / 60m:0.#} ساعت در هفته کمبود ظرفیت وجود دارد؛ کارهای فعلی عقب می‌افتند.",
                [],
                "افزایش فشار و احتمال تأخیر در تعهدات موجود",
                RiskLevel.High));
        }

        var weeksToDelay = input.NewCommitmentWeeklyMinutes == 0
            ? 0
            : (int)Math.Ceiling(deficit / (decimal)Math.Max(1, input.NewCommitmentWeeklyMinutes));
        scenarios.Add(new OpportunityScenario(
            OpportunityScenarioKind.Delay,
            $"تعویق {Math.Max(1, weeksToDelay)} هفته",
            "اجازه می‌دهد بخشی از کارهای فعلی بسته شود.",
            [],
            "شروع دیرتر مورد جدید",
            RiskLevel.Medium));

        if (input.PausableProjects.Count > 0)
        {
            var pausable = input.PausableProjects
                .OrderByDescending(p => p.WeeklyMinutes)
                .First();

            scenarios.Add(new OpportunityScenario(
                OpportunityScenarioKind.PauseOther,
                $"توقف موقت «{pausable.Name}»",
                $"حدود {pausable.WeeklyMinutes / 60m:0.#} ساعت در هفته آزاد می‌شود.",
                [pausable.Name],
                "توقف پیشرفت پروژه فعلی",
                RiskLevel.Medium));
        }

        scenarios.Add(new OpportunityScenario(
            OpportunityScenarioKind.ReduceScope,
            "کاهش دامنه مورد جدید",
            "نسخه کوچک‌تر با تلاش کمتر شروع می‌شود.",
            [],
            "نتیجه کوچک‌تر از حد انتظار",
            RiskLevel.Low));

        var goalAligned = input.NewCommitmentSupportsActiveGoal;
        if (!goalAligned)
        {
            assumptions.Add("مورد جدید به هیچ هدف فعالی متصل نیست.");
        }

        return new OpportunityCostAnalysis(
            scenarios,
            assumptions,
            deficit > 0,
            deficit,
            goalAligned);
    }
}

public sealed record OpportunityCostInput(
    int UsableWeeklyMinutes,
    int PlannedWeeklyMinutes,
    int NewCommitmentWeeklyMinutes,
    bool NewCommitmentSupportsActiveGoal,
    IReadOnlyList<PausableProject> PausableProjects);

public sealed record PausableProject(Guid Id, string Name, int WeeklyMinutes);

public sealed record OpportunityScenario(
    OpportunityScenarioKind Kind,
    string Title,
    string Description,
    IReadOnlyList<string> AffectedItems,
    string TradeOff,
    RiskLevel Risk);

public enum OpportunityScenarioKind
{
    StartNow = 0,
    Delay = 1,
    PauseOther = 2,
    ReduceScope = 3
}

public enum RiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2
}

public sealed record OpportunityCostAnalysis(
    IReadOnlyList<OpportunityScenario> Scenarios,
    IReadOnlyList<string> Assumptions,
    bool HasCapacityDeficit,
    int DeficitMinutes,
    bool AlignsWithActiveGoal);
