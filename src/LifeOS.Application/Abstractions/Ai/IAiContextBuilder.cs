using LifeOS.Domain.Enums;

namespace LifeOS.Application.Abstractions.Ai;

/// <summary>
/// A single piece of context handed to the model. Every item keeps its source references so
/// the UI can show evidence and the router can reason about sensitivity.
/// </summary>
public sealed record ContextItem(
    string Key,
    string Label,
    string RenderedText,
    SensitivityLevel Level,
    IReadOnlyList<ContextSource> Sources);

public sealed record ContextSource(string EntityType, Guid EntityId, string DisplayName);

/// <summary>Assembled context for one AI request.</summary>
public sealed record AiContext(
    IReadOnlyList<ContextItem> Items,
    SensitivityLevel AggregateLevel,
    int CharacterCount)
{
    public static AiContext Empty { get; } = new([], SensitivityLevel.Public, 0);

    public string RenderAsText()
        => string.Join("\n\n", Items.Select(i => $"## {i.Label}\n{i.RenderedText}"));

    public IReadOnlyList<ContextSource> AllSources
        => Items.SelectMany(i => i.Sources).Distinct().ToList();
}

/// <summary>Builds minimal, traceable, privacy-tagged context from Application queries.</summary>
public interface IAiContextBuilder
{
    Task<AiContext> BuildAsync(AiContextRequest request, CancellationToken cancellationToken);
}

public sealed record AiContextRequest(
    AiTaskCategory Category,
    string? RelatedEntityType = null,
    Guid? RelatedEntityId = null,
    bool IncludeFinance = false,
    bool IncludeJournal = false,
    int CharacterBudget = 6000);

/// <summary>Chooses a provider for a request, honouring the sensitivity gate.</summary>
public interface IAiProviderRouter
{
    Task<AiRoutingDecision> ResolveAsync(
        AiTaskCategory category,
        SensitivityLevel sensitivity,
        string? explicitProviderId,
        CancellationToken cancellationToken);
}

public sealed record AiRoutingDecision(
    bool IsResolved,
    AiProviderOptions? Provider,
    string Reason,
    IReadOnlyList<string> RejectedProviders);

/// <summary>Guards against prompt injection in user-authored content.</summary>
public interface IPromptInjectionGuard
{
    PromptInjectionScan Scan(string userContent);
}

public sealed record PromptInjectionScan(bool IsSuspicious, IReadOnlyList<string> MatchedPatterns, string SanitizedContent);
