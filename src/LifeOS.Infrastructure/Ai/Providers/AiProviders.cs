using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOS.Application.Abstractions.Ai;
using LifeOS.Domain.Enums;

namespace LifeOS.Infrastructure.Ai.Providers;

/// <summary>
/// Shared HTTP plumbing for the provider implementations. Kept internal so the wire-format
/// details never leak outside Infrastructure.
/// </summary>
internal static class ProviderHttp
{
    /// <summary>Truncates a provider error body so a huge HTML error page cannot flood the log.</summary>
    internal const int MaxErrorBodyLength = 400;

    internal static async Task<string> ReadErrorBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return body.Length <= MaxErrorBodyLength ? body : body[..MaxErrorBodyLength];
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Maps a transport/status failure onto the provider-agnostic failure taxonomy. The message is
    /// Persian because it is shown directly to the user.
    /// </summary>
    internal static AiProviderException MapStatus(
        HttpStatusCode status,
        string providerId,
        string body)
    {
        var kind = status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiFailureKind.Configuration,
            HttpStatusCode.TooManyRequests => AiFailureKind.RateLimited,
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => AiFailureKind.Timeout,
            _ when (int)status >= 500 => AiFailureKind.Unavailable,
            _ => AiFailureKind.InvalidResponse
        };

        var message = kind switch
        {
            AiFailureKind.Configuration =>
                $"دسترسی به سرویس هوش مصنوعی «{providerId}» رد شد. کلید دسترسی یا آدرس سرویس را بررسی کنید.",
            AiFailureKind.RateLimited =>
                $"سرویس هوش مصنوعی «{providerId}» محدودیت نرخ درخواست اعمال کرده است. کمی بعد دوباره تلاش کنید.",
            AiFailureKind.Timeout =>
                $"سرویس هوش مصنوعی «{providerId}» به‌موقع پاسخ نداد.",
            AiFailureKind.Unavailable =>
                $"سرویس هوش مصنوعی «{providerId}» در دسترس نیست.",
            _ =>
                $"پاسخ سرویس هوش مصنوعی «{providerId}» قابل تفسیر نبود."
        };

        if (!string.IsNullOrWhiteSpace(body))
        {
            message = $"{message} ({body})";
        }

        return new AiProviderException(kind, message);
    }

    /// <summary>Wraps transport exceptions into the same taxonomy.</summary>
    internal static AiProviderException MapTransport(
        Exception exception,
        string providerId,
        CancellationToken cancellationToken)
    {
        if (exception is AiProviderException providerException)
        {
            return providerException;
        }

        if (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new AiProviderException(
                AiFailureKind.Timeout,
                $"سرویس هوش مصنوعی «{providerId}» به‌موقع پاسخ نداد.",
                exception);
        }

        return new AiProviderException(
            AiFailureKind.Unavailable,
            $"ارتباط با سرویس هوش مصنوعی «{providerId}» برقرار نشد.",
            exception);
    }

    /// <summary>Normalizes a configured endpoint into the concrete request URI.</summary>
    internal static Uri BuildUri(string endpoint, string suffix)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new AiProviderException(
                AiFailureKind.Configuration,
                "آدرس سرویس هوش مصنوعی تنظیم نشده است.");
        }

        var trimmed = endpoint.Trim().TrimEnd('/');

        if (!trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            trimmed += suffix;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new AiProviderException(
                AiFailureKind.Configuration,
                $"آدرس سرویس هوش مصنوعی معتبر نیست: {endpoint}");
        }

        return uri;
    }
}

// ---------------------------------------------------------------------------------------------
// Ollama (local)
// ---------------------------------------------------------------------------------------------

internal sealed class OllamaChatRequest
{
    [JsonPropertyName("model")] public string Model { get; init; } = string.Empty;
    [JsonPropertyName("messages")] public List<OllamaMessage> Messages { get; init; } = [];
    [JsonPropertyName("stream")] public bool Stream { get; init; }
    [JsonPropertyName("options")] public OllamaOptions Options { get; init; } = new();
    [JsonPropertyName("format")] public string? Format { get; init; }
}

internal sealed class OllamaMessage
{
    [JsonPropertyName("role")] public string Role { get; init; } = "user";
    [JsonPropertyName("content")] public string Content { get; init; } = string.Empty;
}

internal sealed class OllamaOptions
{
    [JsonPropertyName("temperature")] public double Temperature { get; init; }
    [JsonPropertyName("num_predict")] public int NumPredict { get; init; }
}

internal sealed class OllamaChatResponse
{
    [JsonPropertyName("message")] public OllamaMessage? Message { get; init; }
    [JsonPropertyName("prompt_eval_count")] public int? PromptEvalCount { get; init; }
    [JsonPropertyName("eval_count")] public int? EvalCount { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
}

/// <summary>
/// Local provider speaking Ollama's native chat API. No SDK dependency: the wire format is small
/// and stable, and keeping it here means the Application layer never sees a vendor type.
/// </summary>
public sealed class OllamaAiProvider : IAiProvider
{
    private readonly AiProviderOptions _options;
    private readonly HttpClient _httpClient;

    public OllamaAiProvider(AiProviderOptions options, HttpClient httpClient)
    {
        _options = options;
        _httpClient = httpClient;
    }

    public string ProviderId => _options.ProviderId;

    public AiProviderKind Kind => AiProviderKind.Local;

    public string DisplayName => "مدل محلی (Ollama)";

    public async Task<AiProviderResponse> GenerateAsync(
        AiProviderRequest request,
        CancellationToken cancellationToken)
    {
        var uri = ProviderHttp.BuildUri(_options.Endpoint, "/api/chat");

        var messages = new List<OllamaMessage>(request.Messages.Count + 1)
        {
            new() { Role = "system", Content = request.SystemPrompt }
        };

        foreach (var message in request.Messages)
        {
            messages.Add(new OllamaMessage { Role = message.Role, Content = message.Content });
        }

        var payload = new OllamaChatRequest
        {
            Model = string.IsNullOrWhiteSpace(request.Model) ? _options.Model : request.Model,
            Messages = messages,
            Stream = false,
            Options = new OllamaOptions
            {
                Temperature = request.Temperature,
                NumPredict = request.MaxOutputTokens
            },
            Format = request.ExpectJson ? "json" : null
        };

        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(uri, payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await ProviderHttp.ReadErrorBodyAsync(response, cancellationToken);
                throw ProviderHttp.MapStatus(response.StatusCode, ProviderId, body);
            }

            var parsed = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
                cancellationToken: cancellationToken);

            if (parsed is null)
            {
                throw new AiProviderException(
                    AiFailureKind.InvalidResponse,
                    "پاسخ سرویس محلی خالی بود.");
            }

            if (!string.IsNullOrWhiteSpace(parsed.Error))
            {
                throw new AiProviderException(AiFailureKind.InvalidResponse, parsed.Error);
            }

            var content = parsed.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new AiProviderException(
                    AiFailureKind.InvalidResponse,
                    "سرویس محلی متنی بازنگرداند.");
            }

            return new AiProviderResponse(
                content,
                ProviderId,
                payload.Model,
                parsed.PromptEvalCount,
                parsed.EvalCount,
                DateTimeOffset.UtcNow - startedAt);
        }
        catch (Exception ex) when (ex is not AiProviderException)
        {
            throw ProviderHttp.MapTransport(ex, ProviderId, cancellationToken);
        }
    }
}

// ---------------------------------------------------------------------------------------------
// OpenAI-compatible (cloud)
// ---------------------------------------------------------------------------------------------

internal sealed class OpenAiChatRequest
{
    [JsonPropertyName("model")] public string Model { get; init; } = string.Empty;
    [JsonPropertyName("messages")] public List<OpenAiMessage> Messages { get; init; } = [];
    [JsonPropertyName("temperature")] public double Temperature { get; init; }
    [JsonPropertyName("max_tokens")] public int MaxTokens { get; init; }
    [JsonPropertyName("response_format")] public OpenAiResponseFormat? ResponseFormat { get; init; }
}

internal sealed class OpenAiMessage
{
    [JsonPropertyName("role")] public string Role { get; init; } = "user";
    [JsonPropertyName("content")] public string Content { get; init; } = string.Empty;
}

internal sealed class OpenAiResponseFormat
{
    [JsonPropertyName("type")] public string Type { get; init; } = "json_object";
}

internal sealed class OpenAiChatResponse
{
    [JsonPropertyName("choices")] public List<OpenAiChoice>? Choices { get; init; }
    [JsonPropertyName("usage")] public OpenAiUsage? Usage { get; init; }
}

internal sealed class OpenAiChoice
{
    [JsonPropertyName("message")] public OpenAiMessage? Message { get; init; }
    [JsonPropertyName("finish_reason")] public string? FinishReason { get; init; }
}

internal sealed class OpenAiUsage
{
    [JsonPropertyName("prompt_tokens")] public int? PromptTokens { get; init; }
    [JsonPropertyName("completion_tokens")] public int? CompletionTokens { get; init; }
}

/// <summary>
/// Cloud provider speaking the OpenAI chat-completions shape, which is also implemented by
/// OpenRouter, Groq, LM Studio, vLLM and others. One implementation, many vendors.
/// </summary>
public sealed class OpenAiCompatibleProvider : IAiProvider
{
    private readonly AiProviderOptions _options;
    private readonly HttpClient _httpClient;

    public OpenAiCompatibleProvider(AiProviderOptions options, HttpClient httpClient)
    {
        _options = options;
        _httpClient = httpClient;
    }

    public string ProviderId => _options.ProviderId;

    public AiProviderKind Kind => AiProviderKind.Cloud;

    public string DisplayName => "مدل ابری (سازگار با OpenAI)";

    public async Task<AiProviderResponse> GenerateAsync(
        AiProviderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new AiProviderException(
                AiFailureKind.Configuration,
                "کلید دسترسی سرویس ابری تنظیم نشده است.");
        }

        var uri = ProviderHttp.BuildUri(_options.Endpoint, "/chat/completions");

        var messages = new List<OpenAiMessage>(request.Messages.Count + 1)
        {
            new() { Role = "system", Content = request.SystemPrompt }
        };

        foreach (var message in request.Messages)
        {
            messages.Add(new OpenAiMessage { Role = message.Role, Content = message.Content });
        }

        var payload = new OpenAiChatRequest
        {
            Model = string.IsNullOrWhiteSpace(request.Model) ? _options.Model : request.Model,
            Messages = messages,
            Temperature = request.Temperature,
            MaxTokens = request.MaxOutputTokens,
            ResponseFormat = request.ExpectJson ? new OpenAiResponseFormat() : null
        };

        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = JsonContent.Create(payload)
            };

            httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.ApiKey}");

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await ProviderHttp.ReadErrorBodyAsync(response, cancellationToken);
                throw ProviderHttp.MapStatus(response.StatusCode, ProviderId, body);
            }

            var parsed = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(
                cancellationToken: cancellationToken);

            var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new AiProviderException(
                    AiFailureKind.InvalidResponse,
                    "سرویس ابری متنی بازنگرداند.");
            }

            return new AiProviderResponse(
                content,
                ProviderId,
                payload.Model,
                parsed?.Usage?.PromptTokens,
                parsed?.Usage?.CompletionTokens,
                DateTimeOffset.UtcNow - startedAt);
        }
        catch (Exception ex) when (ex is not AiProviderException)
        {
            throw ProviderHttp.MapTransport(ex, ProviderId, cancellationToken);
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Fake (tests and offline demos)
// ---------------------------------------------------------------------------------------------

/// <summary>
/// Deterministic provider used by tests and by the offline demo mode. It never touches the
/// network, so it is safe to register when no real provider is configured.
/// </summary>
public sealed class FakeAiProvider : IAiProvider
{
    private readonly AiProviderOptions _options;
    private readonly Func<AiProviderRequest, string> _responder;

    public FakeAiProvider(AiProviderOptions options, Func<AiProviderRequest, string>? responder = null)
    {
        _options = options;
        _responder = responder ?? DefaultResponder;
    }

    public string ProviderId => _options.ProviderId;

    public AiProviderKind Kind => AiProviderKind.Fake;

    public string DisplayName => "مدل آزمایشی";

    /// <summary>Requests received, in order. Tests assert against this.</summary>
    public List<AiProviderRequest> ReceivedRequests { get; } = [];

    public Task<AiProviderResponse> GenerateAsync(
        AiProviderRequest request,
        CancellationToken cancellationToken)
    {
        ReceivedRequests.Add(request);

        var content = _responder(request);

        return Task.FromResult(new AiProviderResponse(
            content,
            ProviderId,
            string.IsNullOrWhiteSpace(request.Model) ? _options.Model : request.Model,
            PromptTokens: null,
            CompletionTokens: null,
            Duration: TimeSpan.Zero));
    }

    private static string DefaultResponder(AiProviderRequest request)
        => request.ExpectJson ? "{}" : "پاسخ آزمایشی.";
}
