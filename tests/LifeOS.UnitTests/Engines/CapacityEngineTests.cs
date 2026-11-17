using FluentAssertions;
using LifeOS.Application.Engines;
using LifeOS.Domain.Enums;

namespace LifeOS.UnitTests.Engines;

public sealed class CapacityEngineTests
{
    private readonly CapacityEngine _engine = new();

    [Fact]
    public void Usable_minutes_reserve_the_configured_buffer()
    {
        // Default buffer is 15%, so 600 available minutes leave 510 usable.
        _engine.UsableMinutes(600).Should().Be(510);
    }

    [Fact]
    public void Zero_available_minutes_yields_zero_usable_minutes()
        => _engine.UsableMinutes(0).Should().Be(0);

    [Fact]
    public void No_planned_work_is_reported_as_empty_not_healthy()
    {
        var assessment = _engine.Assess(600, 0);

        assessment.Status.Should().Be(CapacityStatus.Empty);
        assessment.UtilizationPercent.Should().Be(0m);
    }

    [Fact]
    public void Planning_more_than_usable_time_is_overloaded_and_reports_the_deficit()
    {
        var assessment = _engine.Assess(600, 600);

        assessment.Status.Should().Be(CapacityStatus.Overloaded);
        assessment.IsOverloaded.Should().BeTrue();
        assessment.OverloadMinutes.Should().Be(90);
        assessment.RemainingMinutes.Should().Be(-90);
    }

    [Fact]
    public void Planning_just_below_the_threshold_is_healthy()
    {
        // 510 usable; 400 planned is 78.4% — below the 85% near-capacity threshold.
        var assessment = _engine.Assess(600, 400);

        assessment.Status.Should().Be(CapacityStatus.Healthy);
    }

    [Fact]
    public void Planning_above_the_threshold_is_near_capacity()
    {
        // 510 usable; 450 planned is 88.2% — above 85% but still within capacity.
        var assessment = _engine.Assess(600, 450);

        assessment.Status.Should().Be(CapacityStatus.NearCapacity);
        assessment.IsOverloaded.Should().BeFalse();
    }

    [Fact]
    public void AssessWeek_counts_committed_obligations_against_the_same_capacity()
    {
        var withoutCommitments = _engine.AssessWeek(600, 400, 0);
        var withCommitments = _engine.AssessWeek(600, 400, 200);

        withCommitments.PlannedMinutes.Should().Be(600);
        withCommitments.Status.Should().Be(CapacityStatus.Overloaded);
        withoutCommitments.Status.Should().Be(CapacityStatus.Healthy);
    }

    [Fact]
    public void Partition_never_returns_more_than_the_available_capacity()
    {
        var items = Enumerable.Range(0, 10).Select(i => (Item: i, Minutes: 60)).ToList();

        var (fits, deferred) = _engine.Partition(items, i => i.Minutes, 180);

        fits.Sum(i => i.Minutes).Should().BeLessThanOrEqualTo(180);
        fits.Count.Should().Be(3);
        deferred.Count.Should().Be(7);
    }
}
