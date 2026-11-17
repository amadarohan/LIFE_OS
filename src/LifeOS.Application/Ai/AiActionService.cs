using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LifeOS.Application.Abstractions;
using LifeOS.Application.Abstractions.Ai;
using LifeOS.Application.Common;
using LifeOS.Application.UseCases.Decisions;
using LifeOS.Application.UseCases.Finance;
using LifeOS.Application.UseCases.Goals;
using LifeOS.Application.UseCases.Planning;
using LifeOS.Application.UseCases.Projects;
using LifeOS.Application.UseCases.Tasks;
using LifeOS.Domain.Entities.Ai;
using LifeOS.Domain.Enums;

namespace LifeOS.Application.Ai;

public sealed record AiActionView(
    Guid Id,
    AiActionType ActionType,
    string Description,
    string PayloadJson,
    AiActionStatus Status,
    bool RequiresApproval,
    string ContextHash,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? ExecutedAtUtc,
    string? ErrorMessage,
    bool IsExpired,
    bool CanBeApproved,
    bool CanBeExecuted);

public sealed record AiExecutionLogView(
    Guid Id,
    string ToolName,
    AiExecutionStatus ExecutionStatus,
    string? ErrorMessage,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

/// <summary>
/// Owns the approval-first lifecycle of AI proposals. Nothing here mutates domain data until a
/// human calls <see cref="ApproveAsync"/> and then <see cref="ExecuteAsync"/>. Execution re-checks
/// the context hash so a proposal built against stale data can never be applied.
/// </summary>
public sealed class AiActionService
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly TaskService _tasks;
    private readonly ProjectService _projects;
    private readonly GoalService _goals;
    private readonly DecisionService _decisions;
    private readonly FinanceService _finance;
    private readonly PlanningService _planning;

    public AiActionService(
        IApplicationDbContext db,
        IClock clock,
        IAuditWriter audit,
        TaskService tasks,
        ProjectService projects,
        GoalService goals,
        DecisionService decisions,
        FinanceService finance,
        PlanningService planning)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
        _tasks = tasks;
        _projects = projects;
        _goals = goals;
        _decisions = decisions;
        _finance = finance;
        _planning = planning;
    }

    // -----------------------------------------------------------------------------------------
    // Queries
    // -----------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<AiActionView>> ListAsync(
        AiActionStatus? status = null,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var query = _db.AiActions.AsQueryable();

        if (status is { } filter)
        {
            query = query.Where(a => a.Status == filter);
        }

        var actions = await _db.ToListAsync(
            query.OrderByDescending(a => a.CreatedAtUtc).Take(take),
            cancellationToken);

        var now = _clock.UtcNow;
        return actions.Select(a => ToView(a, now)).ToList();
    }

    public async Task<IReadOnlyList<AiActionView>> ListPendingAsync(
        CancellationToken cancellationToken = default)
        => await ListAsync(AiActionStatus.Proposed, 50, cancellationToken);

    public async Task<IReadOnlyList<AiExecutionLogView>> ListExecutionLogsAsync(
        Guid actionId,
        CancellationToken cancellationToken = default)
    {
        var logs = await _db.ToListAsync(
            _db.AiExecutionLogs
                .Where(l => l.AiActionId == actionId)
                .OrderByDescending(l => l.StartedAtUtc),
            cancellationToken);

        return logs.Select(l => new AiExecutionLogView(
                l.Id,
                l.ToolName,
                l.ExecutionStatus,
                l.ErrorMessage,
                l.StartedAtUtc,
                l.CompletedAtUtc))
            .ToList();
    }

    // -----------------------------------------------------------------------------------------
    // Proposal creation
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Records a proposal. Idempotent: re-submitting the same key returns the existing row instead
    /// of creating a duplicate, so a retried model call cannot double-propose.
    /// </summary>
    public Task<Result<Guid>> ProposeAsync(
        Guid? conversationId,
        AiActionProposal proposal,
        int expiryMinutes = 60,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(proposal.Description))
            {
                return Result.Failure<Guid>(ErrorCodes.Validation, "توضیح پیشنهاد نمی‌تواند خالی باشد.");
            }

            var idempotencyKey = ComputeIdempotencyKey(proposal);

            var existing = await _db.FirstOrDefaultAsync(
                _db.AiActions.Where(a => a.IdempotencyKey == idempotencyKey),
                cancellationToken);

            if (existing is not null)
            {
                return Result.Success(existing.Id);
            }

            var now = _clock.UtcNow;
            var action = new AiAction(
                conversationId,
                proposal.ActionType,
                proposal.Description,
                proposal.PayloadJson,
                idempotencyKey,
                proposal.ContextHash,
                requiresApproval: true,
                now,
                now.AddMinutes(expiryMinutes <= 0 ? 60 : expiryMinutes));

            _db.Add(action);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(action.Id);
        });

    // -----------------------------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------------------------

    public Task<Result> ApproveAsync(Guid actionId, CancellationToken cancellationToken = default)
        => Guard.RunResultAsync(async () =>
        {
            var action = await _db.FirstOrDefaultAsync(
                _db.AiActions.Where(a => a.Id == actionId),
                cancellationToken);

            if (action is null)
            {
                return Guard.NotFound("پیشنهاد هوش مصنوعی", actionId);
            }

            action.Approve(_clock.UtcNow);

            await _audit.WriteAsync(
                nameof(AiAction),
                action.Id,
                AuditActionType.Approved,
                null,
                JsonSerializer.Serialize(new { action.ActionType, action.Description }),
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        });

    public Task<Result> RejectAsync(Guid actionId, CancellationToken cancellationToken = default)
        => Guard.RunResultAsync(async () =>
        {
            var action = await _db.FirstOrDefaultAsync(
                _db.AiActions.Where(a => a.Id == actionId),
                cancellationToken);

            if (action is null)
            {
                return Guard.NotFound("پیشنهاد هوش مصنوعی", actionId);
            }

            action.Reject(_clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        });

    public Task<Result> CancelAsync(Guid actionId, CancellationToken cancellationToken = default)
        => Guard.RunResultAsync(async () =>
        {
            var action = await _db.FirstOrDefaultAsync(
                _db.AiActions.Where(a => a.Id == actionId),
                cancellationToken);

            if (action is null)
            {
                return Guard.NotFound("پیشنهاد هوش مصنوعی", actionId);
            }

            action.Cancel();
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        });

    /// <summary>
    /// Executes an approved proposal. The context hash is recomputed from live data first; a
    /// mismatch means the world moved on since the proposal was made, so execution is refused
    /// rather than applied to stale assumptions.
    /// </summary>
    public Task<Result> ExecuteAsync(Guid actionId, CancellationToken cancellationToken = default)
        => Guard.RunResultAsync(async () =>
        {
            var action = await _db.FirstOrDefaultAsync(
                _db.AiActions.Where(a => a.Id == actionId),
                cancellationToken);

            if (action is null)
            {
                return Guard.NotFound("پیشنهاد هوش مصنوعی", actionId);
            }

            var now = _clock.UtcNow;

            if (!action.CanBeExecuted(now))
            {
                return Result.Failure(
                    ErrorCodes.Conflict,
                    action.IsExpired(now)
                        ? "این پیشنهاد منقضی شده است و قابل اجرا نیست."
                        : "فقط پیشنهاد تأییدشده قابل اجراست.");
            }

            // Re-verify the context hash against live state. Only proposals that depend on an
            // existing row carry a fingerprint; a create-type proposal has nothing to go stale.
            var staleCheck = await VerifyContextAsync(action, cancellationToken);
            if (staleCheck.IsFailure)
            {
                return staleCheck;
            }

            var startedAt = now;
            var result = await DispatchAsync(action, cancellationToken);

            if (result.IsFailure)
            {
                action.MarkFailed(result.ErrorMessage ?? "اجرای پیشنهاد ناموفق بود.", _clock.UtcNow);

                _db.Add(new AiExecutionLog(
                    action.Id,
                    action.ActionType.ToString(),
                    action.PayloadJson,
                    null,
                    AiExecutionStatus.Failed,
                    result.ErrorMessage,
                    startedAt,
                    _clock.UtcNow));

                await _db.SaveChangesAsync(cancellationToken);
                return result;
            }

            action.MarkExecuted(_clock.UtcNow);

            _db.Add(new AiExecutionLog(
                action.Id,
                action.ActionType.ToString(),
                action.PayloadJson,
                "{}",
                AiExecutionStatus.Succeeded,
                null,
                startedAt,
                _clock.UtcNow));

            await _audit.WriteAsync(
                nameof(AiAction),
                action.Id,
                AuditActionType.Executed,
                null,
                JsonSerializer.Serialize(new { action.ActionType, action.Description }),
                AuditSource.Ai,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        });

    /// <summary>Marks every expired proposal as expired. Safe to call repeatedly.</summary>
    public async Task<int> ExpireStaleAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        var candidates = await _db.ToListAsync(
            _db.AiActions.Where(a =>
                (a.Status == AiActionStatus.Proposed || a.Status == AiActionStatus.Approved)
                && a.ExpiresAtUtc != null
                && a.ExpiresAtUtc <= now),
            cancellationToken);

        if (candidates.Count == 0)
        {
            return 0;
        }

        foreach (var action in candidates)
        {
            action.MarkExpired();
        }

        await _db.SaveChangesAsync(cancellationToken);
        return candidates.Count;
    }

    // -----------------------------------------------------------------------------------------
    // Dispatch — the only place AI proposals turn into real writes
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Recomputes the context hash from live data and compares it with the value recorded at
    /// proposal time. A mismatch means the underlying row changed after the proposal was made, so
    /// applying it would act on assumptions the user never approved.
    /// </summary>
    private async Task<Result> VerifyContextAsync(AiAction action, CancellationToken cancellationToken)
    {
        var fingerprint = await LiveFingerprintAsync(action, cancellationToken);

        // No fingerprint means the proposal does not depend on existing state (a pure create).
        if (fingerprint is null)
        {
            return Result.Success();
        }

        var expected = AiContextHash.Compute(action.PayloadJson, fingerprint);

        return string.Equals(expected, action.ContextHash, StringComparison.OrdinalIgnoreCase)
            ? Result.Success()
            : Result.Failure(
                ErrorCodes.StaleProposal,
                "داده‌های مرتبط پس از ساخت این پیشنهاد تغییر کرده‌اند. لطفاً پیشنهاد را رد کنید و دوباره درخواست دهید.");
    }

    /// <summary>
    /// Returns a fingerprint of the live row a proposal targets, or <c>null</c> when the proposal
    /// creates something new. Uses <c>RowVersion</c> so any write to the row invalidates it.
    /// </summary>
    private async Task<string?> LiveFingerprintAsync(AiAction action, CancellationToken cancellationToken)
    {
        if (action.ActionType != AiActionType.UpdateTask)
        {
            return null;
        }

        UpdateTaskPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<UpdateTaskPayload>(action.PayloadJson);
        }
        catch (JsonException)
        {
            // A malformed payload is reported by dispatch with a clearer message.
            return null;
        }

        if (payload is null || payload.Id == Guid.Empty)
        {
            return null;
        }

        var task = await _db.FirstOrDefaultAsync(
            _db.Tasks.Where(t => t.Id == payload.Id),
            cancellationToken);

        // A deleted target is a stale proposal, not a missing one: the user approved an update to
        // a row that no longer exists.
        return task is null ? "deleted" : Convert.ToHexString(task.RowVersion);
    }

    private async Task<Result> DispatchAsync(AiAction action, CancellationToken cancellationToken)
    {
        try
        {
            return action.ActionType switch
            {
                AiActionType.CreateTask => await ExecuteCreateTaskAsync(action.PayloadJson, cancellationToken),
                AiActionType.UpdateTask => await ExecuteUpdateTaskAsync(action.PayloadJson, cancellationToken),
                AiActionType.CreateProject => await ExecuteCreateProjectAsync(action.PayloadJson, cancellationToken),
                AiActionType.CreateGoal => await ExecuteCreateGoalAsync(action.PayloadJson, cancellationToken),
                AiActionType.CreateDecision => await ExecuteCreateDecisionAsync(action.PayloadJson, cancellationToken),
                AiActionType.RecordFinancialTransaction => await ExecuteRecordTransactionAsync(action.PayloadJson, cancellationToken),
                AiActionType.SaveWeeklyPlan => await ExecuteSaveWeeklyPlanAsync(action.PayloadJson, cancellationToken),
                _ => Result.Failure(
                    ErrorCodes.Validation,
                    $"اجرای پیشنهاد از نوع «{action.ActionType}» در این نسخه پشتیبانی نمی‌شود.")
            };
        }
        catch (JsonException)
        {
            return Result.Failure(ErrorCodes.Validation, "ساختار داده پیشنهاد معتبر نیست.");
        }
    }

    private async Task<Result> ExecuteCreateTaskAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<CreateTaskPayload>(payloadJson);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Title))
        {
            return Result.Failure(ErrorCodes.Validation, "عنوان کار در پیشنهاد مشخص نشده است.");
        }

        var result = await _tasks.CreateAsync(
            new CreateTaskRequest(
                payload.Title,
                payload.Description,
                payload.ProjectId,
                payload.GoalId,
                null,
                payload.Priority,
                payload.DueDate,
                payload.ScheduledDate,
                payload.EstimatedMinutes),
            cancellationToken);

        return result.IsSuccess
            ? Result.Success()
            : Result.Failure(result.ErrorCode!, result.ErrorMessage!);
    }

    private async Task<Result> ExecuteUpdateTaskAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<UpdateTaskPayload>(payloadJson);
        if (payload is null || payload.Id == Guid.Empty)
        {
            return Result.Failure(ErrorCodes.Validation, "شناسه کار در پیشنهاد مشخص نشده است.");
        }

        var result = await _tasks.UpdateAsync(
            new UpdateTaskRequest(
                payload.Id,
                payload.Title,
                payload.Description,
                payload.ProjectId,
                payload.GoalId,
                payload.Priority,
                payload.DueDate,
                payload.ScheduledDate,
                payload.EstimatedMinutes),
            cancellationToken);

        return result.IsSuccess
            ? Result.Success()
            : Result.Failure(result.ErrorCode!, result.ErrorMessage!);
    }

    private async Task<Result> ExecuteCreateProjectAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<CreateProjectPayload>(payloadJson);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
        {
            return Result.Failure(ErrorCodes.Validation, "نام پروژه در پیشنهاد مشخص نشده است.");
        }

        var result = await _projects.CreateAsync(
            new CreateProjectRequest(
                payload.Name,
                payload.LifeDomainId,
                payload.Description,
                payload.Priority,
                payload.StartDate,
                payload.TargetEndDate,
                null,
                string.IsNullOrWhiteSpace(payload.CurrencyCode) ? "IRR" : payload.CurrencyCode,
                null,
                null),
            cancellationToken);

        return result.IsSuccess
            ? Result.Success()
            : Result.Failure(result.ErrorCode!, result.ErrorMessage!);
    }

    private async Task<Result> ExecuteCreateGoalAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<CreateGoalPayload>(payloadJson);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
        {
            return Result.Failure(ErrorCodes.Validation, "نام هدف در پیشنهاد مشخص نشده است.");
        }

        var result = await _goals.CreateAsync(
            new CreateGoalRequest(
                payload.Name,
                payload.LifeDomainId,
                payload.GoalType,
                payload.Description,
                payload.Priority,
                payload.StartDate,
                payload.TargetDate,
                payload.WhyItMatters,
                payload.SuccessCriteria),
            cancellationToken);

        return result.IsSuccess
            ? Result.Success()
            : Result.Failure(result.ErrorCode!, result.ErrorMessage!);
    }

    private async Task<Result> ExecuteCreateDecisionAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<CreateDecisionPayload>(payloadJson);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Title))
        {
            return Result.Failure(ErrorCodes.Validation, "عنوان تصمیم در پیشنهاد مشخص نشده است.");
        }

        var result = await _decisions.CreateAsync(
            new CreateDecisionRequest(
                payload.Title,
                payload.Description,
                payload.Context,
                payload.DecisionDate,
                payload.Importance,
                payload.ExpectedOutcome,
                payload.ReviewDate),
            cancellationToken);

        return result.IsSuccess
            ? Result.Success()
            : Result.Failure(result.ErrorCode!, result.ErrorMessage!);
    }

    private async Task<Result> ExecuteRecordTransactionAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<RecordTransactionPayload>(payloadJson);
        if (payload is null || payload.FinancialAccountId == Guid.Empty || payload.Amount <= 0)
        {
            return Result.Failure(ErrorCodes.Validation, "اطلاعات تراکنش در پیشنهاد کامل نیست.");
        }

        var result = await _finance.RecordTransactionAsync(
            new CreateTransactionRequest(
                payload.FinancialAccountId,
                payload.TransactionType,
                payload.Amount,
                payload.Category,
                payload.Description,
                payload.TransactionDate,
                false,
                null),
            cancellationToken);

        return result.IsSuccess
            ? Result.Success()
            : Result.Failure(result.ErrorCode!, result.ErrorMessage!);
    }

    private async Task<Result> ExecuteSaveWeeklyPlanAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SaveWeeklyPlanPayload>(payloadJson);
        if (payload is null)
        {
            return Result.Failure(ErrorCodes.Validation, "اطلاعات برنامه هفتگی در پیشنهاد کامل نیست.");
        }

        var result = await _planning.CreateAsync(
            new CreateWeeklyPlanRequest(payload.WeekStartDate, payload.Theme, payload.AvailableHours),
            cancellationToken);

        return result.IsSuccess
            ? Result.Success()
            : Result.Failure(result.ErrorCode!, result.ErrorMessage!);
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Stable key over the action type and payload. Two identical proposals collapse into one row,
    /// which is what makes a retried model call safe.
    /// </summary>
    private static string ComputeIdempotencyKey(AiActionProposal proposal)
    {
        var material = $"{proposal.ActionType}|{NormalizeJson(proposal.PayloadJson)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash);
    }

    private static string NormalizeJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{}";
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException)
        {
            return json.Trim();
        }
    }

    private static AiActionView ToView(AiAction action, DateTimeOffset now)
        => new(
            action.Id,
            action.ActionType,
            action.Description,
            action.PayloadJson,
            action.Status,
            action.RequiresApproval,
            action.ContextHash,
            action.CreatedAtUtc,
            action.ExpiresAtUtc,
            action.ApprovedAtUtc,
            action.ExecutedAtUtc,
            action.ErrorMessage,
            action.IsExpired(now),
            action.CanBeApproved(now),
            action.CanBeExecuted(now));

    // Payload shapes. Kept private so the wire format stays an implementation detail of this class.
    private sealed record CreateTaskPayload(
        string Title,
        string? Description,
        Guid? ProjectId,
        Guid? GoalId,
        PriorityLevel Priority,
        DateOnly? DueDate,
        DateOnly? ScheduledDate,
        int? EstimatedMinutes);

    private sealed record UpdateTaskPayload(
        Guid Id,
        string Title,
        string? Description,
        Guid? ProjectId,
        Guid? GoalId,
        PriorityLevel Priority,
        DateOnly? DueDate,
        DateOnly? ScheduledDate,
        int? EstimatedMinutes);

    private sealed record CreateProjectPayload(
        string Name,
        Guid? LifeDomainId,
        string? Description,
        PriorityLevel Priority,
        DateOnly? StartDate,
        DateOnly? TargetEndDate,
        string? CurrencyCode);

    private sealed record CreateGoalPayload(
        string Name,
        Guid? LifeDomainId,
        GoalType GoalType,
        string? Description,
        PriorityLevel Priority,
        DateOnly? StartDate,
        DateOnly? TargetDate,
        string? WhyItMatters,
        string? SuccessCriteria);

    private sealed record CreateDecisionPayload(
        string Title,
        string? Description,
        string? Context,
        DateOnly DecisionDate,
        ImportanceLevel Importance,
        string? ExpectedOutcome,
        DateOnly? ReviewDate);

    private sealed record RecordTransactionPayload(
        Guid FinancialAccountId,
        TransactionType TransactionType,
        decimal Amount,
        string? Category,
        string? Description,
        DateOnly TransactionDate);

    private sealed record SaveWeeklyPlanPayload(
        DateOnly WeekStartDate,
        string? Theme,
        decimal AvailableHours);
}
