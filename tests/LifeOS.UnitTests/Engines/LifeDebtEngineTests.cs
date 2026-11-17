using FluentAssertions;
using LifeOS.Application.Engines;
using LifeOS.Domain.Enums;

namespace LifeOS.UnitTests.Engines;

public sealed class LifeDebtEngineTests
{
    private static readonly DateOnly Today = new(2026, 1, 15);
    private readonly LifeDebtEngine _engine = new();

    [Fact]
    public void Empty_input_produces_an_empty_report()
    {
        var report = _engine.Evaluate(LifeDebtInput.Empty, Today);

        report.IsEmpty.Should().BeTrue();
        report.TotalCount.Should().Be(0);
        report.Items.Should().BeEmpty();
    }

    [Fact]
    public void Overdue_task_age_is_measured_from_its_due_date()
    {
        var input = LifeDebtInput.Empty with
        {
            OverdueTasks = [new OverdueTaskInfo(Guid.CreateVersion7(), "مالیات", Today.AddDays(-10), PriorityLevel.Medium)]
        };

        var report = _engine.Evaluate(input, Today);

        report.Items.Should().ContainSingle();
        report.Items[0].AgeDays.Should().Be(10);
        report.Items[0].Kind.Should().Be(LifeDebtKind.OverdueTask);
    }

    [Fact]
    public void Severity_escalates_with_age()
    {
        var fresh = _engine.Evaluate(
            LifeDebtInput.Empty with
            {
                OverdueTasks = [new OverdueTaskInfo(Guid.CreateVersion7(), "الف", Today.AddDays(-1), PriorityLevel.Low)]
            },
            Today);

        var ancient = _engine.Evaluate(
            LifeDebtInput.Empty with
            {
                OverdueTasks = [new OverdueTaskInfo(Guid.CreateVersion7(), "ب", Today.AddDays(-40), PriorityLevel.Low)]
            },
            Today);

        ancient.Items[0].Severity.Should().Be(ImportanceLevel.Critical);
        fresh.Items[0].Severity.Should().Be(ImportanceLevel.Low);
    }

    [Fact]
    public void Items_are_grouped_by_kind_with_the_worst_severity_per_group()
    {
        var input = LifeDebtInput.Empty with
        {
            OverdueTasks =
            [
                new OverdueTaskInfo(Guid.CreateVersion7(), "الف", Today.AddDays(-2), PriorityLevel.Low),
                new OverdueTaskInfo(Guid.CreateVersion7(), "ب", Today.AddDays(-35), PriorityLevel.Low)
            ],
            StagnantProjects = [new StagnantProjectInfo(Guid.CreateVersion7(), "پروژه", 20)]
        };

        var report = _engine.Evaluate(input, Today);

        report.Groups.Should().HaveCount(2);
        var taskGroup = report.Groups.Single(g => g.Kind == LifeDebtKind.OverdueTask);
        taskGroup.Count.Should().Be(2);
        taskGroup.OldestAgeDays.Should().Be(35);
        taskGroup.WorstSeverity.Should().Be(ImportanceLevel.Critical);
    }

    [Fact]
    public void MostUrgent_returns_the_oldest_items_first_and_respects_the_limit()
    {
        var input = LifeDebtInput.Empty with
        {
            OverdueTasks =
            [
                new OverdueTaskInfo(Guid.CreateVersion7(), "کم", Today.AddDays(-1), PriorityLevel.Low),
                new OverdueTaskInfo(Guid.CreateVersion7(), "زیاد", Today.AddDays(-50), PriorityLevel.Low),
                new OverdueTaskInfo(Guid.CreateVersion7(), "متوسط", Today.AddDays(-20), PriorityLevel.Low)
            ]
        };

        var report = _engine.Evaluate(input, Today);
        var urgent = report.MostUrgent(2);

        urgent.Should().HaveCount(2);
        urgent[0].AgeDays.Should().Be(50);
        urgent[1].AgeDays.Should().Be(20);
    }

    [Fact]
    public void A_future_financial_obligation_is_reported_with_zero_age()
    {
        var input = LifeDebtInput.Empty with
        {
            UpcomingFinancialObligations =
                [new UpcomingObligationInfo(Guid.CreateVersion7(), "قسط", Today.AddDays(2))]
        };

        var report = _engine.Evaluate(input, Today);

        report.Items[0].AgeDays.Should().Be(0);
        report.Items[0].Severity.Should().Be(ImportanceLevel.Critical);
    }
}
