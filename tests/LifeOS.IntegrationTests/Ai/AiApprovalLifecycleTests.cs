using System.Text.Json;
using FluentAssertions;
using LifeOS.Application.Abstractions;
using LifeOS.Application.Abstractions.Ai;
using LifeOS.Application.Ai;
using LifeOS.Application.Common;
using LifeOS.Application.UseCases.Tasks;
using LifeOS.Domain.Enums;
using LifeOS.Infrastructure.Persistence;
using LifeOS.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOS.IntegrationTests.Ai;

/// <summary>
/// The approval-first guarantee is the single most important safety property of the AI subsystem:
/// no proposal may mutate domain data without an explicit human approval, and an approved proposal
/// must not be applied against data that changed after it was made.
/// </summary>
public sealed class AiApprovalLifecycleTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public AiApprovalLifecycleTests(DatabaseFixture fixture) => _fixture = fixture;

    private static AiActionProposal TaskProposal(string title, string contextHash = "hash-v1")
        => new(
            AiActionType.CreateTask,
            $"ایجاد کار «{title}»",
            $$"""{"Title":"{{title}}","Priority":2}""",
            contextHash);

    [Fact]
    public async Task A_proposal_starts_in_Proposed_and_does_not_touch_domain_data()
    {
        await using var scope = _fixture.CreateScope();
        var actions = scope.ServiceProvider.GetRequiredService<AiActionService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var before = await db.CountAsync(db.Tasks);

        var created = await actions.ProposeAsync(null, TaskProposal("کار آزمون"));
        created.IsSuccess.Should().BeTrue();

        var pending = await actions.ListPendingAsync();
        pending.Should().ContainSingle(a => a.Id == created.Value);
        pending[0].Status.Should().Be(AiActionStatus.Proposed);
        pending[0].CanBeExecuted.Should().BeFalse();

        (await db.CountAsync(db.Tasks)).Should().Be(before, "a proposal must never write domain data");
    }

    [Fact]
    public async Task Executing_a_proposal_that_was_never_approved_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var actions = scope.ServiceProvider.GetRequiredService<AiActionService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var created = await actions.ProposeAsync(null, TaskProposal("کار بدون تأیید"));
        var before = await db.CountAsync(db.Tasks);

        var result = await actions.ExecuteAsync(created.Value);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        (await db.CountAsync(db.Tasks)).Should().Be(before);
    }

    [Fact]
    public async Task Approve_then_execute_creates_the_task_and_records_an_execution_log()
    {
        await using var scope = _fixture.CreateScope();
        var actions = scope.ServiceProvider.GetRequiredService<AiActionService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var created = await actions.ProposeAsync(null, TaskProposal("کار تأییدشده"));

        (await actions.ApproveAsync(created.Value)).IsSuccess.Should().BeTrue();
        (await actions.ExecuteAsync(created.Value)).IsSuccess.Should().BeTrue();

        var stored = await db.FirstOrDefaultAsync(db.AiActions.Where(a => a.Id == created.Value));
        stored!.Status.Should().Be(AiActionStatus.Executed);
        stored.ExecutedAtUtc.Should().NotBeNull();

        var logs = await actions.ListExecutionLogsAsync(created.Value);
        logs.Should().ContainSingle();
        logs[0].ExecutionStatus.Should().Be(AiExecutionStatus.Succeeded);

        var task = await db.FirstOrDefaultAsync(db.Tasks.Where(t => t.Title == "کار تأییدشده"));
        task.Should().NotBeNull("execution is the only path that writes domain data");
    }

    [Fact]
    public async Task A_rejected_proposal_can_never_be_executed()
    {
        await using var scope = _fixture.CreateScope();
        var actions = scope.ServiceProvider.GetRequiredService<AiActionService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var created = await actions.ProposeAsync(null, TaskProposal("کار ردشده"));
        (await actions.RejectAsync(created.Value)).IsSuccess.Should().BeTrue();

        var before = await db.CountAsync(db.Tasks);
        var result = await actions.ExecuteAsync(created.Value);

        result.IsFailure.Should().BeTrue();
        (await db.CountAsync(db.Tasks)).Should().Be(before);
    }

    [Fact]
    public async Task Executing_the_same_proposal_twice_does_not_duplicate_the_side_effect()
    {
        await using var scope = _fixture.CreateScope();
        var actions = scope.ServiceProvider.GetRequiredService<AiActionService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var created = await actions.ProposeAsync(null, TaskProposal("کار یکتا"));
        await actions.ApproveAsync(created.Value);
        (await actions.ExecuteAsync(created.Value)).IsSuccess.Should().BeTrue();

        var afterFirst = await db.CountAsync(db.Tasks.Where(t => t.Title == "کار یکتا"));

        var second = await actions.ExecuteAsync(created.Value);

        second.IsFailure.Should().BeTrue("an executed proposal is no longer executable");
        (await db.CountAsync(db.Tasks.Where(t => t.Title == "کار یکتا"))).Should().Be(afterFirst);
    }

    [Fact]
    public async Task Proposing_the_same_payload_twice_is_idempotent()
    {
        await using var scope = _fixture.CreateScope();
        var actions = scope.ServiceProvider.GetRequiredService<AiActionService>();

        var first = await actions.ProposeAsync(null, TaskProposal("کار تکراری"));
        var second = await actions.ProposeAsync(null, TaskProposal("کار تکراری"));

        second.Value.Should().Be(first.Value, "the idempotency key must collapse duplicate proposals");
    }

    [Fact]
    public async Task An_expired_proposal_cannot_be_approved_or_executed()
    {
        await using var scope = _fixture.CreateScope();
        var actions = scope.ServiceProvider.GetRequiredService<AiActionService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Negative expiry is clamped to the default, so create a proposal and expire it explicitly.
        var created = await actions.ProposeAsync(null, TaskProposal("کار منقضی"));

        var stored = await db.FirstOrDefaultAsync(db.AiActions.Where(a => a.Id == created.Value));
        stored!.MarkExpired();
        await db.SaveChangesAsync();

        (await actions.ApproveAsync(created.Value)).IsFailure.Should().BeTrue();
        (await actions.ExecuteAsync(created.Value)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ExpireStale_marks_only_proposals_past_their_expiry()
    {
        await using var scope = _fixture.CreateScope();
        var actions = scope.ServiceProvider.GetRequiredService<AiActionService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var stale = await actions.ProposeAsync(null, TaskProposal("کهنه"));
        var fresh = await actions.ProposeAsync(null, TaskProposal("تازه"));

        // Push one proposal's expiry into the past. ExpiresAtUtc has no public setter by design,
        // so the test moves it with SQL rather than widening the domain API for testability.
        var context = scope.ServiceProvider.GetRequiredService<LifeOsDbContext>();
        await context.Database.ExecuteSqlAsync(
            $"UPDATE AiActions SET ExpiresAtUtc = DATEADD(hour, -1, SYSUTCDATETIME()) WHERE Id = {stale.Value}");

        // The raw UPDATE bumped RowVersion behind the tracked entities, so drop the change tracker
        // and let ExpireStaleAsync read fresh rows. (In production it runs in its own scope.)
        context.ChangeTracker.Clear();

        var expiredCount = await actions.ExpireStaleAsync();

        expiredCount.Should().Be(1);

        var staleRow = await db.FirstOrDefaultAsync(db.AiActions.Where(a => a.Id == stale.Value));
        staleRow!.Status.Should().Be(AiActionStatus.Expired);

        var freshRow = await db.FirstOrDefaultAsync(db.AiActions.Where(a => a.Id == fresh.Value));
        freshRow!.Status.Should().Be(AiActionStatus.Proposed);
    }

    [Fact]
    public async Task An_unsupported_action_type_fails_without_writing_domain_data()
    {
        await using var scope = _fixture.CreateScope();
        var actions = scope.ServiceProvider.GetRequiredService<AiActionService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var proposal = new AiActionProposal(
            AiActionType.AdjustBudget,
            "تعدیل بودجه",
            "{}",
            "hash-budget");

        var created = await actions.ProposeAsync(null, proposal);
        await actions.ApproveAsync(created.Value);

        var before = await db.CountAsync(db.Tasks);
        var result = await actions.ExecuteAsync(created.Value);

        result.IsFailure.Should().BeTrue();
        (await db.CountAsync(db.Tasks)).Should().Be(before);

        var stored = await db.FirstOrDefaultAsync(db.AiActions.Where(a => a.Id == created.Value));
        stored!.Status.Should().Be(AiActionStatus.Failed);
        stored.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_malformed_payload_fails_cleanly_instead_of_throwing()
    {
        await using var scope = _fixture.CreateScope();
        var actions = scope.ServiceProvider.GetRequiredService<AiActionService>();

        var proposal = new AiActionProposal(
            AiActionType.CreateTask,
            "کار با داده خراب",
            "{ this is not json",
            "hash-broken");

        var created = await actions.ProposeAsync(null, proposal);
        await actions.ApproveAsync(created.Value);

        var result = await actions.ExecuteAsync(created.Value);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Validation);
    }

    [Fact]
    public async Task An_empty_description_is_rejected_at_proposal_time()
    {
        await using var scope = _fixture.CreateScope();
        var actions = scope.ServiceProvider.GetRequiredService<AiActionService>();

        var result = await actions.ProposeAsync(
            null,
            new AiActionProposal(AiActionType.CreateTask, "   ", "{}", "hash-empty"));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Validation);
    }

    [Fact]
    public async Task An_update_proposal_is_refused_when_the_target_changed_after_proposal()
    {
        await using var scope = _fixture.CreateScope();
        var actions = scope.ServiceProvider.GetRequiredService<AiActionService>();
        var tasks = scope.ServiceProvider.GetRequiredService<TaskService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var created = await tasks.CreateAsync(
            new CreateTaskRequest("کار اصلی", null, null, null, null, PriorityLevel.Medium, null, null, null));
        created.IsSuccess.Should().BeTrue();

        var task = await db.FirstOrDefaultAsync(db.Tasks.Where(t => t.Id == created.Value));
        var payload = JsonSerializer.Serialize(new
        {
            Id = task!.Id,
            Title = "عنوان پیشنهادی هوش مصنوعی",
            Description = (string?)null,
            ProjectId = (Guid?)null,
            GoalId = (Guid?)null,
            Priority = PriorityLevel.High,
            DueDate = (DateOnly?)null,
            ScheduledDate = (DateOnly?)null,
            EstimatedMinutes = (int?)null
        });

        // The proposal is built against the row as it is now.
        var proposal = new AiActionProposal(
            AiActionType.UpdateTask,
            "به‌روزرسانی کار",
            payload,
            AiContextHash.Compute(payload, Convert.ToHexString(task.RowVersion)));

        var action = await actions.ProposeAsync(null, proposal);
        await actions.ApproveAsync(action.Value);

        // Someone edits the task before the proposal is executed.
        var rename = await tasks.UpdateAsync(
            new UpdateTaskRequest(
                task.Id,
                "عنوان تغییر‌یافته توسط کاربر",
                null,
                null,
                null,
                PriorityLevel.Medium,
                null,
                null,
                null));
        rename.IsSuccess.Should().BeTrue();

        var result = await actions.ExecuteAsync(action.Value);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.StaleProposal);

        var after = await db.FirstOrDefaultAsync(db.Tasks.Where(t => t.Id == task.Id));
        after!.Title.Should().Be("عنوان تغییر‌یافته توسط کاربر", "a stale proposal must not overwrite newer data");
    }

    [Fact]
    public async Task An_update_proposal_executes_when_the_target_is_unchanged()
    {
        await using var scope = _fixture.CreateScope();
        var actions = scope.ServiceProvider.GetRequiredService<AiActionService>();
        var tasks = scope.ServiceProvider.GetRequiredService<TaskService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var created = await tasks.CreateAsync(
            new CreateTaskRequest("کار پایدار", null, null, null, null, PriorityLevel.Medium, null, null, null));

        var task = await db.FirstOrDefaultAsync(db.Tasks.Where(t => t.Id == created.Value));
        var payload = JsonSerializer.Serialize(new
        {
            Id = task!.Id,
            Title = "عنوان جدید",
            Description = (string?)null,
            ProjectId = (Guid?)null,
            GoalId = (Guid?)null,
            Priority = PriorityLevel.High,
            DueDate = (DateOnly?)null,
            ScheduledDate = (DateOnly?)null,
            EstimatedMinutes = (int?)null
        });

        var proposal = new AiActionProposal(
            AiActionType.UpdateTask,
            "به‌روزرسانی کار",
            payload,
            AiContextHash.Compute(payload, Convert.ToHexString(task.RowVersion)));

        var action = await actions.ProposeAsync(null, proposal);
        await actions.ApproveAsync(action.Value);

        var result = await actions.ExecuteAsync(action.Value);

        result.IsSuccess.Should().BeTrue();

        var after = await db.FirstOrDefaultAsync(db.Tasks.Where(t => t.Id == task.Id));
        after!.Title.Should().Be("عنوان جدید");
    }
}
