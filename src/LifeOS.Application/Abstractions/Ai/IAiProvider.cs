using LifeOS.Domain.Enums;

namespace LifeOS.Application.Abstractions.Ai;

/// <summary>
/// Provider-agnostic AI boundary. Implementations live in Infrastructure and are the only
/// place that knows about a specific vendor's wire format.
/// </summary>
public interface IAiProvider
{
    string ProviderId { get; }
    AiProviderKind Kind { get; }
    string DisplayName { get; }

    Task<AiProviderResponse> GenerateAsync(AiProviderRequest request, CancellationToken cancellationToken);
}

public sealed record AiChatMessage(string Role, string Content);

public sealed record AiProviderRequest(
    string Model,
    string SystemPrompt,
    IReadOnlyList<AiChatMessage> Messages,
    double Temperature,
    int MaxOutputTokens,
    bool ExpectJson);

public sealed record AiProviderResponse(
    string Content,
    string ProviderId,
    string Model,
    int? PromptTokens,
    int? CompletionTokens,
    TimeSpan Duration);

public enum AiFailureKind
{
    Timeout = 0,
    Unavailable = 1,
    InvalidResponse = 2,
    Refused = 3,
    RateLimited = 4,
    Configuration = 5
}

public sealed class AiProviderException : Exception
{
    public AiProviderException(AiFailureKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }

    public AiFailureKind Kind { get; }
}

/// <summary>Configuration for a single provider instance, resolved from settings.</summary>
public sealed record AiProviderOptions(
    string ProviderId,
    AiProviderKind Kind,
    string Endpoint,
    string Model,
    string? ApiKey,
    int TimeoutSeconds,
    bool AllowSensitiveData);
