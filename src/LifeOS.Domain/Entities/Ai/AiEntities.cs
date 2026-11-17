using LifeOS.Domain.Common;
using LifeOS.Domain.Enums;

namespace LifeOS.Domain.Entities.Ai;

public sealed class AiConversation : AuditableEntity
{
    private AiConversation() { }

    public AiConversation(string title, string contextType, string? relatedEntityType, Guid? relatedEntityId)
    {
        Title = title;
        ContextType = contextType;
        RelatedEntityType = relatedEntityType;
        RelatedEntityId = relatedEntityId;
    }

    public string Title { get; private set; } = string.Empty;
    public string ContextType { get; private set; } = "global";
    public string? RelatedEntityType { get; private set; }
    public Guid? RelatedEntityId { get; private set; }

    public void Rename(string title)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title.Trim();
        }
    }
}

public sealed class AiMessage : Entity
{
    private AiMessage() { }

    public AiMessage(
        Guid conversationId,
        AiMessageRole role,
        string content,
        string? provider,
        string? model,
        int? promptTokens,
        int? completionTokens,
        string? sourcesJson,
        DateTimeOffset createdAtUtc)
    {
        ConversationId = conversationId;
        Role = role;
        Content = content;
        Provider = provider;
        Model = model;
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
        SourcesJson = sourcesJson;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid ConversationId { get; private set; }
    public AiMessageRole Role { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string? Provider { get; private set; }
    public string? Model { get; private set; }
    public int? PromptTokens { get; private set; }
    public int? CompletionTokens { get; private set; }

    /// <summary>Serialized source references so the UI can render evidence links.</summary>
    public string? SourcesJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public AiConversation? Conversation { get; private set; }
}

public sealed class AiMemory : AuditableEntity
{
    private AiMemory() { }

    public AiMemory(
        AiMemoryType memoryType,
        string content,
        string? sourceEntityType,
        Guid? sourceEntityId,
        decimal confidence,
        DateTimeOffset validFromUtc,
        DateTimeOffset? validUntilUtc)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DomainRuleException("AiMemory.ContentRequired", "محتوای حافظه نمی‌تواند خالی باشد.");
        }

        MemoryType = memoryType;
        Content = content.Trim();
        SourceEntityType = sourceEntityType;
        SourceEntityId = sourceEntityId;
        Confidence = Math.Clamp(confidence, 0m, 1m);
        ValidFromUtc = validFromUtc;
        ValidUntilUtc = validUntilUtc;
        IsValidated = false;
        IsActive = true;
    }

    public AiMemoryType MemoryType { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string? SourceEntityType { get; private set; }
    public Guid? SourceEntityId { get; private set; }
    public decimal Confidence { get; private set; }
    public DateTimeOffset ValidFromUtc { get; private set; }
    public DateTimeOffset? ValidUntilUtc { get; private set; }
    public bool IsValidated { get; private set; }
    public bool IsActive { get; private set; }

    public bool IsCurrentlyValid(DateTimeOffset now)
        => IsActive && ValidFromUtc <= now && (ValidUntilUtc is null || ValidUntilUtc > now);

    /// <summary>Only the user can promote an AI-derived memory to a validated fact.</summary>
    public void Validate() => IsValidated = true;

    public void Deactivate() => IsActive = false;

    public void UpdateContent(string content, decimal confidence)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DomainRuleException("AiMemory.ContentRequired", "محتوای حافظه نمی‌تواند خالی باشد.");
        }

        Content = content.Trim();
        Confidence = Math.Clamp(confidence, 0m, 1m);
    }
}

public sealed class AiInsight : Entity
{
    private AiInsight() { }

    public AiInsight(
        string title,
        string description,
        AiInsightType insightType,
        ImportanceLevel severity,
        decimal confidence,
        string? evidenceJson,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? expiresAtUtc)
    {
        Title = title;
        Description = description;
        InsightType = insightType;
        Severity = severity;
        Confidence = Math.Clamp(confidence, 0m, 1m);
        EvidenceJson = evidenceJson;
        Status = AiInsightStatus.New;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public AiInsightType InsightType { get; private set; }
    public ImportanceLevel Severity { get; private set; }
    public decimal Confidence { get; private set; }
    public string? EvidenceJson { get; private set; }
    public AiInsightStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public void ChangeStatus(AiInsightStatus status) => Status = status;

    public bool IsExpired(DateTimeOffset now) => ExpiresAtUtc.HasValue && ExpiresAtUtc.Value <= now;
}

public sealed class AiAction : Entity, IConcurrencyTracked
{
    private AiAction() { }

    public AiAction(
        Guid? conversationId,
        AiActionType actionType,
        string description,
        string payloadJson,
        string idempotencyKey,
        string contextHash,
        bool requiresApproval,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? expiresAtUtc)
    {
        ConversationId = conversationId;
        ActionType = actionType;
        Description = description;
        PayloadJson = payloadJson;
        IdempotencyKey = idempotencyKey;
        ContextHash = contextHash;
        RequiresApproval = requiresApproval;
        Status = AiActionStatus.Proposed;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid? ConversationId { get; private set; }
    public AiActionType ActionType { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = "{}";
    public AiActionStatus Status { get; private set; }
    public bool RequiresApproval { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public DateTimeOffset? ExecutedAtUtc { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    /// <summary>Unique key that makes proposal submission idempotent.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>Hash of the context the proposal was built from, used to detect staleness.</summary>
    public string ContextHash { get; private set; } = string.Empty;

    public byte[] RowVersion { get; private set; } = [];

    public AiConversation? Conversation { get; private set; }

    public bool IsExpired(DateTimeOffset now) => ExpiresAtUtc.HasValue && ExpiresAtUtc.Value <= now;

    public bool CanBeApproved(DateTimeOffset now)
        => Status == AiActionStatus.Proposed && !IsExpired(now);

    public bool CanBeExecuted(DateTimeOffset now)
        => Status == AiActionStatus.Approved && !IsExpired(now);

    public void Approve(DateTimeOffset now)
    {
        if (!CanBeApproved(now))
        {
            throw new DomainRuleException(
                "AiAction.NotApprovable",
                IsExpired(now) ? "این پیشنهاد منقضی شده است." : "این پیشنهاد در وضعیت قابل تأیید نیست.");
        }

        Status = AiActionStatus.Approved;
        ApprovedAtUtc = now;
    }

    public void Reject(DateTimeOffset now)
    {
        if (Status is not (AiActionStatus.Proposed or AiActionStatus.Approved))
        {
            throw new DomainRuleException("AiAction.NotRejectable", "این پیشنهاد در وضعیت قابل رد کردن نیست.");
        }

        Status = AiActionStatus.Rejected;
        ApprovedAtUtc = null;
    }

    public void Cancel()
    {
        if (Status is AiActionStatus.Executed or AiActionStatus.Failed)
        {
            throw new DomainRuleException("AiAction.NotCancellable", "این پیشنهاد قابل لغو نیست.");
        }

        Status = AiActionStatus.Cancelled;
    }

    public void MarkExecuted(DateTimeOffset now)
    {
        if (Status != AiActionStatus.Approved)
        {
            throw new DomainRuleException("AiAction.NotExecutable", "فقط پیشنهاد تأییدشده قابل اجراست.");
        }

        Status = AiActionStatus.Executed;
        ExecutedAtUtc = now;
        ErrorMessage = null;
    }

    public void MarkFailed(string errorMessage, DateTimeOffset now)
    {
        Status = AiActionStatus.Failed;
        ErrorMessage = errorMessage;
        ExecutedAtUtc = now;
    }

    public void MarkExpired()
    {
        if (Status is AiActionStatus.Proposed or AiActionStatus.Approved)
        {
            Status = AiActionStatus.Expired;
        }
    }
}

public sealed class AiExecutionLog : Entity
{
    private AiExecutionLog() { }

    public AiExecutionLog(
        Guid aiActionId,
        string toolName,
        string inputJson,
        string? outputJson,
        AiExecutionStatus executionStatus,
        string? errorMessage,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? completedAtUtc)
    {
        AiActionId = aiActionId;
        ToolName = toolName;
        InputJson = inputJson;
        OutputJson = outputJson;
        ExecutionStatus = executionStatus;
        ErrorMessage = errorMessage;
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
    }

    public Guid AiActionId { get; private set; }
    public string ToolName { get; private set; } = string.Empty;
    public string InputJson { get; private set; } = "{}";
    public string? OutputJson { get; private set; }
    public AiExecutionStatus ExecutionStatus { get; private set; }

    public string? ErrorMessage { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public AiAction? AiAction { get; private set; }
}
