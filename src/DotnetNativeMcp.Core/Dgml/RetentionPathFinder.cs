using System.Collections.ObjectModel;

namespace DotnetNativeMcp.Core.Dgml;

public sealed record RetentionPathSegment(
    string NodeId,
    string Label,
    string? Category,
    string? IncomingEdgeLabel);

public static class RetentionPathFinder
{
    public const int DefaultMaxDepth = 12;
    public const int MaxDepthLimit = 64;

    public static int CountTargetMatches(DgmlGraph graph, string targetNodeQuery)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return FindMatchingNodes(graph, targetNodeQuery).Count;
    }

    public static IReadOnlyList<RetentionPathSegment> FindShortestPath(DgmlGraph graph, string targetNodeQuery, int maxDepth = DefaultMaxDepth)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (string.IsNullOrWhiteSpace(targetNodeQuery))
            return [];

        if (maxDepth <= 0)
            maxDepth = DefaultMaxDepth;

        var matches = FindMatchingNodes(graph, targetNodeQuery);
        if (matches.Count == 0)
            return [];

        var target = matches[0];
        var nodesById = new Dictionary<string, DgmlNode>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
            nodesById.TryAdd(node.Id, node);

        if (!nodesById.ContainsKey(target.Id))
            return [];

        var incomingCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var adjacency = new Dictionary<string, List<DgmlEdge>>(StringComparer.Ordinal);
        foreach (var edge in graph.Edges)
        {
            if (!nodesById.ContainsKey(edge.Source) || !nodesById.ContainsKey(edge.Target))
                continue;

            if (!adjacency.TryGetValue(edge.Source, out var bucket))
            {
                bucket = [];
                adjacency[edge.Source] = bucket;
            }

            bucket.Add(edge);
            incomingCounts[edge.Target] = incomingCounts.TryGetValue(edge.Target, out var count) ? count + 1 : 1;
        }

        var rootIds = graph.Nodes
            .Where(node => string.Equals(node.Category, "Root", StringComparison.OrdinalIgnoreCase) || !incomingCounts.ContainsKey(node.Id))
            .Select(node => node.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (rootIds.Count == 0)
            return [];

        var queue = new Queue<(string NodeId, int Depth)>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var predecessors = new Dictionary<string, (string? PreviousNodeId, DgmlEdge? Edge)>(StringComparer.Ordinal);

        foreach (var rootId in rootIds)
        {
            if (!visited.Add(rootId))
                continue;

            queue.Enqueue((rootId, 0));
            predecessors[rootId] = (null, null);
        }

        while (queue.Count > 0)
        {
            var (currentId, depth) = queue.Dequeue();
            if (string.Equals(currentId, target.Id, StringComparison.Ordinal))
                return BuildPath(currentId, nodesById, predecessors);

            if (depth >= maxDepth)
                continue;

            if (!adjacency.TryGetValue(currentId, out var outgoing))
                continue;

            foreach (var edge in outgoing)
            {
                if (!visited.Add(edge.Target))
                    continue;

                predecessors[edge.Target] = (currentId, edge);
                queue.Enqueue((edge.Target, depth + 1));
            }
        }

        return [];
    }

    private static List<DgmlNode> FindMatchingNodes(DgmlGraph graph, string targetNodeQuery)
    {
        if (string.IsNullOrWhiteSpace(targetNodeQuery))
            return [];

        var exactMatches = graph.Nodes
            .Where(node => string.Equals(node.Id, targetNodeQuery, StringComparison.Ordinal))
            .ToList();
        if (exactMatches.Count > 0)
            return exactMatches;

        return graph.Nodes
            .Where(node => node.Label.Contains(targetNodeQuery, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static ReadOnlyCollection<RetentionPathSegment> BuildPath(
        string targetNodeId,
        Dictionary<string, DgmlNode> nodesById,
        Dictionary<string, (string? PreviousNodeId, DgmlEdge? Edge)> predecessors)
    {
        var path = new List<RetentionPathSegment>();
        var currentId = targetNodeId;

        while (true)
        {
            var node = nodesById[currentId];
            var predecessor = predecessors[currentId];
            path.Add(new RetentionPathSegment(
                node.Id,
                node.Label,
                node.Category,
                predecessor.Edge?.Label));

            if (predecessor.PreviousNodeId is null)
                break;

            currentId = predecessor.PreviousNodeId;
        }

        path.Reverse();
        return new ReadOnlyCollection<RetentionPathSegment>(path);
    }
}
