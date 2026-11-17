using System.Security.Cryptography;
using System.Text;
using LifeOS.Application.Abstractions;
using LifeOS.Application.Common;
using LifeOS.Domain.Entities.Ai;
using LifeOS.Domain.Enums;

namespace LifeOS.Application.Ai;

public sealed record AiMemoryView(
    Guid Id,
    AiMemoryType MemoryType,
    string Content,
    string? SourceEntityType,
    Guid? SourceEntityId,
    decimal Confidence,
    bool IsValidated,
    bool IsActive,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidUntilUtc,
    bool IsCurrentlyValid);

public sealed record AiInsightView(
    Guid Id,
    string Title,
    string Description,
    AiInsightType InsightType,
    ImportanceLevel Severity,
    decimal Confidence,
    AiInsightStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    bool IsExpired);

/// <summary>
/// Manages what the assistant is allowed to remember. Two rules are enforced here rather than in
/// the UI: AI-derived memories are never auto-validated, and memories are deactivated instead of
/// deleted so the history of what the assistant believed stays auditable.
/// </summary>
public sealed class AiMemoryService
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public AiMemoryService(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<AiMemoryView>> ListAsync(
        AiMemoryType? memoryType = null,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = _db.AiMemories.AsQueryable();

        if (memoryType is { } type)
        {
            query = query.Where(m => m.MemoryType == type);
        }

        if (!includeInactive)
        {
            query = query.Where(m => m.IsActive);
        }

        var memories = await _db.ToListAsync(
            query.OrderByDescending(m => m.UpdatedAtUtc),
            cancellationToken);

        var now = _clock.UtcNow;
        return memories.Select(m => ToView(m, now)).ToList();
    }

    /// <summary>
    /// Stores a memory. Duplicate detection is on (type, normalized content hash) so the assistant
    /// cannot accumulate near-identical memories across sessions.
    /// </summary>
    public Task<Result<Guid>> RememberAsync(
        AiMemoryType memoryType,
        string content,
        string? sourceEntityType,
        Guid? sourceEntityId,
        decimal confidence,
        DateTimeOffset? validUntilUtc = null,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return Result.Failure<Guid>(ErrorCodes.Validation, "محتوای حافظه نمی‌تواند خالی باشد.");
            }

            var normalized = Normalize(content);

            var existing = await _db.ToListAsync(
                _db.AiMemories.Where(m => m.MemoryType == memoryType && m.IsActive),
                cancellationToken);

            var duplicate = existing.FirstOrDefault(m => Normalize(m.Content) == normalized);
            if (duplicate is not null)
            {
                return Result.Success(duplicate.Id);
            }

            var now = _clock.UtcNow;
            var memory = new AiMemory(
                memoryType,
                content,
                sourceEntityType,
                sourceEntityId,
                confidence,
                now,
                validUntilUtc);

            memory.StampCreated(now);
            _db.Add(memory);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(memory.Id);
        });

    public Task<Result> UpdateAsync(
        Guid id,
        string content,
        decimal confidence,
        CancellationToken cancellationToken = default)
        => Guard.RunResultAsync(async () =>
        {
            var memory = await _db.FirstOrDefaultAsync(
                _db.AiMemories.Where(m => m.Id == id),
                cancellationToken);

            if (memory is null)
            {
                return Guard.NotFound("حافظه هوش مصنوعی", id);
            }

            memory.UpdateContent(content, confidence);
            memory.StampUpdated(_clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        });

    /// <summary>Only the user can promote an AI-derived memory to a validated fact.</summary>
    public Task<Result> ValidateAsync(Guid id, CancellationToken cancellationToken = default)
        => Guard.RunResultAsync(async () =>
        {
            var memory = await _db.FirstOrDefaultAsync(
                _db.AiMemories.Where(m => m.Id == id),
                cancellationToken);

            if (memory is null)
            {
                return Guard.NotFound("حافظه هوش مصنوعی", id);
            }

            memory.Validate();
            memory.StampUpdated(_clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        });

    public Task<Result> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
        => Guard.RunResultAsync(async () =>
        {
            var memory = await _db.FirstOrDefaultAsync(
                _db.AiMemories.Where(m => m.Id == id),
                cancellationToken);

            if (memory is null)
            {
                return Guard.NotFound("حافظه هوش مصنوعی", id);
            }

            memory.Deactivate();
            memory.StampUpdated(_clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        });

    private static AiMemoryView ToView(AiMemory m, DateTimeOffset now)
        => new(
            m.Id,
            m.MemoryType,
            m.Content,
            m.SourceEntityType,
            m.SourceEntityId,
            m.Confidence,
            m.IsValidated,
            m.IsActive,
            m.ValidFromUtc,
            m.ValidUntilUtc,
            m.IsCurrentlyValid(now));

    /// <summary>
    /// Collapses whitespace and case so "کارها را صبح انجام می‌دهم" and a re-spaced variant are
    /// recognized as the same memory.
    /// </summary>
    private static string Normalize(string content)
    {
        var collapsed = string.Join(' ', content.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return collapsed.ToUpperInvariant();
    }
}

/// <summary>
/// Surfaces and triages AI observations. Insights are advisory only — acknowledging or dismissing
/// one never changes domain data.
/// </summary>
public sealed class AiInsightService
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public AiInsightService(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<AiInsightView>> ListAsync(
        AiInsightStatus? status = null,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var query = _db.AiInsights.AsQueryable();

        if (status is { } filter)
        {
            query = query.Where(i => i.Status == filter);
        }

        var insights = await _db.ToListAsync(
            query.OrderByDescending(i => i.CreatedAtUtc).Take(take),
            cancellationToken);

        var now = _clock.UtcNow;
        return insights.Select(i => ToView(i, now)).ToList();
    }

    public Task<Result<Guid>> RecordAsync(
        string title,
        string description,
        AiInsightType insightType,
        ImportanceLevel severity,
        decimal confidence,
        string? evidenceJson = null,
        TimeSpan? lifetime = null,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return Result.Failure<Guid>(ErrorCodes.Validation, "عنوان بینش نمی‌تواند خالی باشد.");
            }

            var now = _clock.UtcNow;
            var insight = new AiInsight(
                title,
                description,
                insightType,
                severity,
                confidence,
                evidenceJson,
                now,
                lifetime is { } span ? now.Add(span) : null);

            _db.Add(insight);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(insight.Id);
        });

    public Task<Result> ChangeStatusAsync(
        Guid id,
        AiInsightStatus status,
        CancellationToken cancellationToken = default)
        => Guard.RunResultAsync(async () =>
        {
            var insight = await _db.FirstOrDefaultAsync(
                _db.AiInsights.Where(i => i.Id == id),
                cancellationToken);

            if (insight is null)
            {
                return Guard.NotFound("بینش هوش مصنوعی", id);
            }

            insight.ChangeStatus(status);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        });

    private static AiInsightView ToView(AiInsight i, DateTimeOffset now)
        => new(
            i.Id,
            i.Title,
            i.Description,
            i.InsightType,
            i.Severity,
            i.Confidence,
            i.Status,
            i.CreatedAtUtc,
            i.ExpiresAtUtc,
            i.IsExpired(now));
}
