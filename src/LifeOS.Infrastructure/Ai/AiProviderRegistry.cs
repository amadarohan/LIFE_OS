using LifeOS.Application.Abstractions.Ai;
using LifeOS.Domain.Enums;
using LifeOS.Infrastructure.Ai.Providers;

namespace LifeOS.Infrastructure.Ai;

/// <summary>
/// Builds provider instances from resolved options. Providers are cheap and stateless apart from
/// their <see cref="HttpClient"/>, so a fresh instance per request is simpler and safer than
/// caching one per provider id — a settings change takes effect on the very next message.
/// </summary>
public sealed class AiProviderRegistry : IAiProviderRegistry
{
    private readonly IHttpClientFactory _httpClientFactory;

    public AiProviderRegistry(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    public IAiProvider? Create(AiProviderOptions options)
    {
        var client = _httpClientFactory.CreateClient(AiHttpClients.Name);
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 120);

        return options.Kind switch
        {
            AiProviderKind.Local => new OllamaAiProvider(options, client),
            AiProviderKind.Cloud => new OpenAiCompatibleProvider(options, client),
            AiProviderKind.Fake => new FakeAiProvider(options),
            _ => null
        };
    }
}

/// <summary>Named <see cref="HttpClient"/> used for every AI provider call.</summary>
public static class AiHttpClients
{
    public const string Name = "ai-provider";
}
