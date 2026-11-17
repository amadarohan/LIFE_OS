using System.Text.Json;
using LifeOS.Application.Abstractions.Ai;
using LifeOS.Application.Common;
using LifeOS.Domain.Enums;
namespace LifeOS.Application.Ai;

/// <summary>
/// The single entry point for invoking a tool. It enforces the rules the model must not be able to
/// bypass: unknown names fail cleanly, malformed JSON fails cleanly, execution tools are refused
/// outright, and a proposal is persisted as an <c>AiAction</c> awaiting human approval rather than
/// being applied. Nothing here throws to the caller — the model gets a structured error it can
/// react to.
/// </summary>
public sealed class AiToolInvoker
{
    private readonly Tools.AiToolRegistry _registry;
    private readonly AiActionService _actions;

    public AiToolInvoker(
        Tools.AiToolRegistry registry,
        AiActionService actions)
    {
        _registry = registry;
        _actions = actions;
    }

    public async Task<Result<AiToolInvocation>> InvokeAsync(
        string toolName,
        string inputJson,
        AiToolContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return Result.Failure<AiToolInvocation>(
                ErrorCodes.Validation,
                "نام ابزار مشخص نشده است.");
        }

        var tool = _registry.Find(toolName);
        if (tool is null)
        {
            return Result.Failure<AiToolInvocation>(
                ErrorCodes.Validation,
                $"ابزار «{toolName}» وجود ندارد.");
        }

        // Execution tools are deliberately unreachable from the model. They only run through
        // AiActionService.ExecuteAsync after a human approves the matching proposal.
        if (tool.Category == AiToolCategory.Execution)
        {
            return Result.Failure<AiToolInvocation>(
                ErrorCodes.Forbidden,
                "ابزارهای اجرایی مستقیماً قابل فراخوانی نیستند و فقط پس از تأیید کاربر اجرا می‌شوند.");
        }

        JsonElement input;
        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
            input = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Result.Failure<AiToolInvocation>(
                ErrorCodes.Validation,
                "ورودی ابزار یک JSON معتبر نیست.");
        }

        if (input.ValueKind != JsonValueKind.Object)
        {
            return Result.Failure<AiToolInvocation>(
                ErrorCodes.Validation,
                "ورودی ابزار باید یک شیء JSON باشد.");
        }

        AiToolResult result;
        try
        {
            result = await tool.ExecuteAsync(input, context, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A tool bug must not surface as a stack trace to the user.
            return Result.Failure<AiToolInvocation>(
                ErrorCodes.Unexpected,
                $"اجرای ابزار «{toolName}» با خطا مواجه شد: {ex.Message}");
        }

        if (!result.IsSuccess)
        {
            return Result.Failure<AiToolInvocation>(
                result.ErrorCode ?? ErrorCodes.Validation,
                result.ErrorMessage ?? "اجرای ابزار ناموفق بود.");
        }

        if (result.Proposal is null)
        {
            return Result.Success(new AiToolInvocation(toolName, result.OutputJson, null));
        }

        var proposal = result.Proposal;

        // A proposal is only a request. Persisting it here means the UI can show it and the user
        // can approve or reject it; no domain data changes at this point.
        var proposed = await _actions.ProposeAsync(
            context.ConversationId,
            proposal,
            cancellationToken: cancellationToken);

        if (proposed.IsFailure)
        {
            return Result.Failure<AiToolInvocation>(proposed.ErrorCode!, proposed.ErrorMessage!);
        }

        return Result.Success(new AiToolInvocation(toolName, result.OutputJson, proposed.Value));
    }
}

/// <summary>
/// Outcome of a tool call. <paramref name="ProposedActionId"/> is set only for proposal tools, so
/// the caller can tell the user "this is waiting for your approval" instead of "done".
/// </summary>
public sealed record AiToolInvocation(
    string ToolName,
    string OutputJson,
    Guid? ProposedActionId);
