namespace LifeOS.Application.Engines;

/// <summary>
/// Capacity is realistic availability, not theoretical free time. The engine subtracts a
/// configurable buffer and reports overload explicitly instead of silently over-scheduling.
/// </summary>
public sealed class CapacityEngine
{
    public CapacitySettings Settings { get; }

    public CapacityEngine(CapacitySettings? settings = null)
    {
        Settings = settings ?? CapacitySettings.Default;
    }

    /// <summary>Usable minutes for a day after removing the buffer.</summary>
    public int UsableMinutes(int availableMinutes)
    {
        if (availableMinutes <= 0)
        {
            return 0;
        }

        var buffer = (int)Math.Round(availableMinutes * Settings.BufferRatio);
        return Math.Max(0, availableMinutes - buffer);
    }

    public CapacityAssessment Assess(int availableMinutes, int plannedMinutes)
    {
        var usable = UsableMinutes(availableMinutes);
        var remaining = usable - plannedMinutes;
        var utilization = usable == 0 ? 0m : Math.Round(plannedMinutes * 100m / usable, 1);

        var status = plannedMinutes == 0
            ? CapacityStatus.Empty
            : remaining < 0
                ? CapacityStatus.Overloaded
                : utilization >= Settings.NearCapacityThreshold
                    ? CapacityStatus.NearCapacity
                    : CapacityStatus.Healthy;

        return new CapacityAssessment(availableMinutes, usable, plannedMinutes, remaining, utilization, status);
    }

    /// <summary>
    /// Weekly assessment. <paramref name="committedMinutes"/> covers fixed obligations
    /// (meetings, appointments) that are not part of the plan but consume the same capacity.
    /// </summary>
    public CapacityAssessment AssessWeek(int availableMinutes, int plannedMinutes, int committedMinutes)
    {
        var usable = UsableMinutes(availableMinutes);
        var totalLoad = plannedMinutes + committedMinutes;
        var remaining = usable - totalLoad;
        var utilization = usable == 0 ? 0m : Math.Round(totalLoad * 100m / usable, 1);

        var status = totalLoad == 0
            ? CapacityStatus.Empty
            : remaining < 0
                ? CapacityStatus.Overloaded
                : utilization >= Settings.NearCapacityThreshold
                    ? CapacityStatus.NearCapacity
                    : CapacityStatus.Healthy;

        return new CapacityAssessment(availableMinutes, usable, totalLoad, remaining, utilization, status);
    }

    /// <summary>
    /// How many minutes of the given tasks actually fit. Used by the planner to show what must
    /// be deferred rather than pretending everything fits.
    /// </summary>
    public (IReadOnlyList<T> Fitting, IReadOnlyList<T> Overflow) Partition<T>(
        IEnumerable<T> orderedItems,
        Func<T, int> minutesSelector,
        int usableMinutes)
    {
        var fitting = new List<T>();
        var overflow = new List<T>();
        var used = 0;

        foreach (var item in orderedItems)
        {
            var minutes = Math.Max(0, minutesSelector(item));
            if (used + minutes <= usableMinutes)
            {
                fitting.Add(item);
                used += minutes;
            }
            else
            {
                overflow.Add(item);
            }
        }

        return (fitting, overflow);
    }
}

public sealed record CapacitySettings(decimal BufferRatio, decimal NearCapacityThreshold)
{
    public static CapacitySettings Default { get; } = new(0.15m, 85m);
}

public sealed record CapacityAssessment(
    int AvailableMinutes,
    int UsableMinutes,
    int PlannedMinutes,
    int RemainingMinutes,
    decimal UtilizationPercent,
    CapacityStatus Status)
{
    public bool IsOverloaded => Status == CapacityStatus.Overloaded;
    public int OverloadMinutes => Math.Max(0, -RemainingMinutes);
}

public enum CapacityStatus
{
    Empty = 0,
    Healthy = 1,
    NearCapacity = 2,
    Overloaded = 3
}
