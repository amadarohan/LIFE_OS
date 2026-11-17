using LifeOS.Domain.Common;
using LifeOS.Domain.Enums;

namespace LifeOS.Domain.Entities.Goals;

public sealed class Goal : AuditableEntity, IConcurrencyTracked
{
    private readonly List<GoalKeyResult> _keyResults = [];

    private Goal() { }

    public Goal(string name, Guid? lifeDomainId, GoalType goalType, string? description)
    {
        Rename(name);
        LifeDomainId = lifeDomainId;
        GoalType = goalType;
        Description = description;
        Status = GoalStatus.Draft;
        Priority = PriorityLevel.Medium;
    }

    public Guid? LifeDomainId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public GoalType GoalType { get; private set; }
    public GoalStatus Status { get; private set; }
    public PriorityLevel Priority { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? TargetDate { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? WhyItMatters { get; private set; }
    public string? SuccessCriteria { get; private set; }
    public decimal ProgressPercentage { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<GoalKeyResult> KeyResults => _keyResults;

    /// <summary>True when progress is derived from key results rather than a manual value.</summary>
    public bool HasMeasurableProgress => _keyResults.Count > 0;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleException("Goal.NameRequired", "نام هدف الزامی است.");
        }

        Name = name.Trim();
    }

    public void UpdateDetails(
        string name,
        Guid? lifeDomainId,
        GoalType goalType,
        string? description,
        PriorityLevel priority,
        DateOnly? startDate,
        DateOnly? targetDate,
        string? whyItMatters,
        string? successCriteria)
    {
        Rename(name);

        if (startDate.HasValue && targetDate.HasValue && targetDate < startDate)
        {
            throw new DomainRuleException("Goal.InvalidDateRange", "تاریخ هدف نمی‌تواند قبل از تاریخ شروع باشد.");
        }

        LifeDomainId = lifeDomainId;
        GoalType = goalType;
        Description = description;
        Priority = priority;
        StartDate = startDate;
        TargetDate = targetDate;
        WhyItMatters = whyItMatters;
        SuccessCriteria = successCriteria;
    }

    public void ChangeStatus(GoalStatus status, DateTimeOffset now)
    {
        if (Status == status)
        {
            return;
        }

        if (status == GoalStatus.Completed)
        {
            CompletedAtUtc = now;
            ProgressPercentage = 100m;
        }
        else if (Status == GoalStatus.Completed)
        {
            CompletedAtUtc = null;
        }

        Status = status;
    }

    public void SetPriority(PriorityLevel priority) => Priority = priority;

    public GoalKeyResult AddKeyResult(
        string name,
        string? description,
        string unit,
        decimal initialValue,
        decimal targetValue,
        decimal weight)
    {
        var keyResult = new GoalKeyResult(Id, name, description, unit, initialValue, targetValue, weight);
        _keyResults.Add(keyResult);
        RecalculateProgress();
        return keyResult;
    }

    public void RemoveKeyResult(GoalKeyResult keyResult)
    {
        _keyResults.Remove(keyResult);
        RecalculateProgress();
    }

    /// <summary>
    /// Weighted progress across key results. Falls back to the stored manual value when the goal
    /// has no key results, so a goal without measurement never pretends to be objective.
    /// </summary>
    public void RecalculateProgress()
    {
        if (_keyResults.Count == 0)
        {
            return;
        }

        var totalWeight = _keyResults.Sum(k => k.Weight);
        if (totalWeight <= 0m)
        {
            return;
        }

        var weighted = _keyResults.Sum(k => (k.ProgressPercentage ?? 0m) * k.Weight);
        ProgressPercentage = Math.Clamp(Math.Round(weighted / totalWeight, 2), 0m, 100m);
    }

    public void SetManualProgress(decimal percentage)
    {
        if (_keyResults.Count > 0)
        {
            throw new DomainRuleException(
                "Goal.ProgressDerived",
                "این هدف نتیجه کلیدی دارد؛ درصد پیشرفت از روی نتایج کلیدی محاسبه می‌شود.");
        }

        ProgressPercentage = Math.Clamp(Math.Round(percentage, 2), 0m, 100m);
    }
}

public sealed class GoalKeyResult : Entity
{
    private GoalKeyResult() { }

    internal GoalKeyResult(
        Guid goalId,
        string name,
        string? description,
        string unit,
        decimal initialValue,
        decimal targetValue,
        decimal weight)
    {
        GoalId = goalId;
        Rename(name);
        Description = description;
        Unit = unit;
        InitialValue = initialValue;
        CurrentValue = initialValue;
        TargetValue = targetValue;
        Weight = weight <= 0m ? 1m : weight;
        Status = GoalStatus.Active;
    }

    public Guid GoalId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public decimal InitialValue { get; private set; }
    public decimal CurrentValue { get; private set; }
    public decimal TargetValue { get; private set; }
    public decimal Weight { get; private set; }
    public GoalStatus Status { get; private set; }
    public DateTimeOffset? LastUpdatedAtUtc { get; private set; }

    public Goal? Goal { get; private set; }

    /// <summary>
    /// Progress from initial toward target. Handles both increasing and decreasing targets.
    /// Returns null when the span is zero (nothing to measure).
    /// </summary>
    public decimal? ProgressPercentage
    {
        get
        {
            var span = TargetValue - InitialValue;
            if (span == 0m)
            {
                return null;
            }

            var ratio = (CurrentValue - InitialValue) / span * 100m;
            return Math.Clamp(Math.Round(ratio, 2), 0m, 100m);
        }
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleException("KeyResult.NameRequired", "نام نتیجه کلیدی الزامی است.");
        }

        Name = name.Trim();
    }

    public void UpdateDefinition(string name, string? description, string unit, decimal targetValue, decimal weight)
    {
        Rename(name);
        Description = description;
        Unit = unit;
        TargetValue = targetValue;
        Weight = weight <= 0m ? 1m : weight;
    }

    public void RecordValue(decimal value, DateTimeOffset now)
    {
        CurrentValue = value;
        LastUpdatedAtUtc = now;
    }
}
