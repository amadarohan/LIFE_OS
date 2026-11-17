namespace LifeOS.Application.Engines;

/// <summary>
/// Detects cycles in dependency graphs before they are persisted. A cycle makes a plan
/// impossible to execute, so it must be rejected at the boundary rather than discovered later.
/// </summary>
public static class DependencyGraphGuard
{
    /// <summary>
    /// Returns the cycle path when adding <paramref name="fromId"/> → <paramref name="toId"/>
    /// would create one, otherwise <c>null</c>.
    /// </summary>
    public static IReadOnlyList<Guid>? FindCycle(
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> existingEdges,
        Guid fromId,
        Guid toId)
    {
        if (fromId == toId)
        {
            return [fromId, toId];
        }

        // Adding from -> to creates a cycle when `to` can already reach `from`.
        var path = FindPath(existingEdges, toId, fromId);
        if (path is null)
        {
            return null;
        }

        var cycle = new List<Guid> { fromId };
        cycle.AddRange(path);
        return cycle;
    }

    /// <summary>Breadth-first search returning the shortest path, or <c>null</c> when unreachable.</summary>
    public static IReadOnlyList<Guid>? FindPath(
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> edges,
        Guid startId,
        Guid targetId)
    {
        if (startId == targetId)
        {
            return [startId];
        }

        var visited = new HashSet<Guid> { startId };
        var queue = new Queue<List<Guid>>();
        queue.Enqueue([startId]);

        while (queue.Count > 0)
        {
            var path = queue.Dequeue();
            var current = path[^1];

            if (!edges.TryGetValue(current, out var next))
            {
                continue;
            }

            foreach (var neighbour in next)
            {
                if (neighbour == targetId)
                {
                    var result = new List<Guid>(path) { neighbour };
                    return result;
                }

                if (visited.Add(neighbour))
                {
                    queue.Enqueue([.. path, neighbour]);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Topological order for execution planning. Returns <c>null</c> when the graph contains a
    /// cycle, so callers cannot silently receive a partial order.
    /// </summary>
    public static IReadOnlyList<Guid>? TopologicalOrder(IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> edges)
    {
        var allNodes = new HashSet<Guid>(edges.Keys);
        foreach (var targets in edges.Values)
        {
            foreach (var target in targets)
            {
                allNodes.Add(target);
            }
        }

        var inDegree = allNodes.ToDictionary(n => n, _ => 0);
        foreach (var targets in edges.Values)
        {
            foreach (var target in targets)
            {
                inDegree[target]++;
            }
        }

        var ready = new Queue<Guid>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var ordered = new List<Guid>(allNodes.Count);

        while (ready.Count > 0)
        {
            var node = ready.Dequeue();
            ordered.Add(node);

            if (!edges.TryGetValue(node, out var targets))
            {
                continue;
            }

            foreach (var target in targets)
            {
                if (--inDegree[target] == 0)
                {
                    ready.Enqueue(target);
                }
            }
        }

        return ordered.Count == allNodes.Count ? ordered : null;
    }
}
