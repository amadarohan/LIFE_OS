using LifeOS.Application.Abstractions.Ai;

namespace LifeOS.Application.Ai.Tools;

/// <summary>
/// Resolves tools by name. The model only ever sees tool names and schemas; this registry is the
/// single place that maps a name back to an implementation, so an unknown name is a clean error
/// rather than a reflection lookup.
/// </summary>
public sealed class AiToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAiTool> _tools;

    public AiToolRegistry(IEnumerable<IAiTool> tools)
        => _tools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<IAiTool> All => _tools.Values.ToList();

    public IAiTool? Find(string name)
        => _tools.TryGetValue(name, out var tool) ? tool : null;

    /// <summary>
    /// Tools the model is allowed to call for a given profile. Execution tools are excluded by
    /// construction: they are never model-callable, only invoked after approval.
    /// </summary>
    public IReadOnlyList<IAiTool> ResolveAllowed(IReadOnlyList<string> allowedNames)
        => allowedNames
            .Select(Find)
            .Where(t => t is not null && t.Category != Domain.Enums.AiToolCategory.Execution)
            .Select(t => t!)
            .ToList();
}
