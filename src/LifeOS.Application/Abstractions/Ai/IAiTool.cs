using System.Text.Json;
using LifeOS.Domain.Enums;

namespace LifeOS.Application.Abstractions.Ai;

/// <summary>
/// Minimal, typed description of a tool's input. It is serialized to JSON Schema for the model
/// and used by the orchestrator to reject malformed input before a tool ever runs.
/// </summary>
public sealed record JsonSchemaProperty(
    string Type,
    string Description,
    bool IsRequired = false,
    IReadOnlyList<string>? AllowedValues = null);

public sealed record JsonSchema(
    string Type,
    IReadOnlyDictionary<string, JsonSchemaProperty> Properties)
{
    public IReadOnlyList<string> RequiredProperties
        => Properties.Where(p => p.Value.IsRequired).Select(p => p.Key).ToList();

    public string ToJson()
    {
        var properties = Properties.ToDictionary(
            p => p.Key,
            p =>
            {
                var node = new Dictionary<string, object?>
                {
                    ["type"] = p.Value.Type,
                    ["description"] = p.Value.Description
                };

                if (p.Value.AllowedValues is { Count: > 0 } allowed)
                {
                    node["enum"] = allowed;
                }

                return node;
            });

        var document = new Dictionary<string, object?>
        {
            ["type"] = Type,
            ["properties"] = properties
        };

        var required = RequiredProperties;
        if (required.Count > 0)
        {
            document["required"] = required;
        }

        return JsonSerializer.Serialize(document);
    }
}

/// <summary>
/// Ambient information a tool needs. Deliberately contains no database handles and no entity
/// objects — tools reach data through Application use cases only.
/// </summary>
public sealed record AiToolContext(
    Guid? ConversationId,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    SensitivityLevel Sensitivity);

/// <summary>
/// A proposal produced by a Proposal-category tool. It is never executed directly; it becomes an
/// <c>AiAction</c> row that a human must approve.
/// </summary>
public sealed record AiActionProposal(
    AiActionType ActionType,
    string Description,
    string PayloadJson,
    string ContextHash);

public sealed record AiToolResult(
    bool IsSuccess,
    string OutputJson,
    string? ErrorCode,
    string? ErrorMessage,
    AiActionProposal? Proposal)
{
    public static AiToolResult Success(string outputJson)
        => new(true, outputJson, null, null, null);

    public static AiToolResult Failure(string errorCode, string errorMessage)
        => new(false, "{}", errorCode, errorMessage, null);

    public static AiToolResult Proposed(AiActionProposal proposal)
        => new(true, "{}", null, null, proposal);
}

/// <summary>
/// A capability the model may invoke. Read and Analysis tools run immediately; Proposal tools
/// return an <see cref="AiActionProposal"/>; Execution tools are never exposed to the model and
/// are invoked only by the action executor after approval.
/// </summary>
public interface IAiTool
{
    string Name { get; }

    AiToolCategory Category { get; }

    string Description { get; }

    JsonSchema InputSchema { get; }

    Task<AiToolResult> ExecuteAsync(
        JsonElement input,
        AiToolContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// Materializes a live provider instance for a resolved set of options. The router decides
/// <em>which</em> provider should serve a request; the registry supplies the instance. Options are
/// passed in rather than looked up again so a single request reads settings exactly once.
/// </summary>
public interface IAiProviderRegistry
{
    IAiProvider? Create(AiProviderOptions options);
}
