using FluentAssertions;
using LifeOS.Application.Engines;
using LifeOS.Domain.Enums;

namespace LifeOS.UnitTests.Engines;

public sealed class PriorityEngineTests
{
    private static readonly DateOnly Today = new(2026, 1, 15);
    private readonly PriorityEngine _engine = new();

    private static TaskPriorityInput Input(
        PriorityLevel priority = PriorityLevel.Medium,
        DateOnly? dueDate = null,
        bool hasGoalLink = false,
        bool hasProjectLink = false,
        int blockingTaskCount = 0,
        bool isNextAction = false,
        int? estimatedMinutes = null,
        LifeTaskStatus status = LifeTaskStatus.Todo)
        => new(priority, dueDate, hasGoalLink, hasProjectLink, blockingTaskCount, isNextAction, estimatedMinutes, status);

    [Fact]
    public void Evaluate_returns_zero_for_a_bare_low_priority_task()
    {
        var score = _engine.Evaluate(Input(priority: PriorityLevel.None), Today);

        score.Score.Should().Be(0m);
        score.Band.Should().Be(PriorityBand.Minimal);
    }

    [Fact]
    public void Overdue_task_outranks_an_identical_task_due_next_month()
    {
        var overdue = _engine.Evaluate(Input(dueDate: Today.AddDays(-1)), Today);
        var distant = _engine.Evaluate(Input(dueDate: Today.AddDays(30)), Today);

        overdue.Score.Should().BeGreaterThan(distant.Score);
    }

    [Fact]
    public void Score_is_clamped_to_one_hundred_even_when_every_factor_fires()
    {
        var score = _engine.Evaluate(
            Input(
                priority: PriorityLevel.Critical,
                dueDate: Today.AddDays(-5),
                hasGoalLink: true,
                hasProjectLink: true,
                blockingTaskCount: 10,
                isNextAction: true,
                estimatedMinutes: 10),
            Today);

        score.Score.Should().Be(100m);
        score.Band.Should().Be(PriorityBand.Critical);
    }

    [Fact]
    public void Waiting_status_applies_a_penalty()
    {
        var todo = _engine.Evaluate(Input(), Today);
        var waiting = _engine.Evaluate(Input(status: LifeTaskStatus.Waiting), Today);

        waiting.Score.Should().BeLessThan(todo.Score);
    }

    [Fact]
    public void Every_factor_is_reported_so_the_ui_can_explain_the_ranking()
    {
        var score = _engine.Evaluate(Input(), Today);

        score.Factors.Should().NotBeEmpty();
        score.Factors.Select(f => f.Key).Should().Contain(
            ["priority", "urgency", "goalAlignment", "projectImpact", "dependencyImpact", "nextAction", "quickWin", "waiting"]);
    }

    [Theory]
    [InlineData(70, PriorityBand.Critical)]
    [InlineData(69.9, PriorityBand.High)]
    [InlineData(50, PriorityBand.High)]
    [InlineData(30, PriorityBand.Medium)]
    [InlineData(12, PriorityBand.Low)]
    [InlineData(11.9, PriorityBand.Minimal)]
    public void Band_boundaries_are_inclusive_at_the_lower_edge(double score, PriorityBand expected)
        => PriorityEngine.Band((decimal)score).Should().Be(expected);
}
