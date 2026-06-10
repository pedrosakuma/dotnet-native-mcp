using System.Collections.ObjectModel;

namespace DotnetNativeMcp.Core.Dgml;

public sealed record RetentionPathSegment(
    string NodeId,
    string Label,
    string? Category,
    string? IncomingEdgeLabel);

/// <summary>A candidate node matched by a retention target query.</summary>
public sealed record RetentionTarget(string NodeId, string Label, string? Category);

/// <summary>
/// A single retention path: the chain of segments from a root (index 0) to the target (last index),
/// surfaced together with the root that anchors it and the edge depth between them.
/// </summary>
public sealed record RetentionPath(
    string RootId,
    string RootLabel,
    string? RootCategory,
    int Depth,
    IReadOnlyList<RetentionPathSegment> Segments);

public static class RetentionPathFinder
{
    public const int DefaultMaxDepth = 12;
    public const int MaxDepthLimit = 64;
    public const int DefaultMaxPaths = 1;
    public const int MaxPathsLimit = 64;

    /// <summary>Counts how many graph nodes the query matches (exact id, else case-insensitive label substring).</summary>
    public static int CountTargetMatches(DgmlGraph graph, string targetNodeQuery)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return FindMatchingNodes(graph, targetNodeQuery).Count;
    }

    /// <summary>
    /// Returns every node the query matches, ordered exact-id first then by graph order, capped at
    /// <paramref name="maxResults"/>. Used to disambiguate a query that resolves to more than one node.
    /// </summary>
    public static IReadOnlyList<RetentionTarget> FindTargets(DgmlGraph graph, string targetNodeQuery, int maxResults)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (maxResults <= 0)
            maxResults = 1;

        var matches = FindMatchingNodes(graph, targetNodeQuery);
        if (matches.Count == 0)
            return [];

        var take = Math.Min(matches.Count, maxResults);
        var result = new List<RetentionTarget>(take);
        for (var i = 0; i < take; i++)
            result.Add(new RetentionTarget(matches[i].Id, matches[i].Label, matches[i].Category));

        return new ReadOnlyCollection<RetentionTarget>(result);
    }

    /// <summary>
    /// Back-compat: the single shortest retention path for the query, or empty when none reaches a root.
    /// Equivalent to the first entry of <see cref="FindRetentionPaths(DgmlGraph, string, int, int)"/>.
    /// </summary>
    public static IReadOnlyList<RetentionPathSegment> FindShortestPath(DgmlGraph graph, string targetNodeQuery, int maxDepth = DefaultMaxDepth)
    {
        var paths = FindRetentionPaths(graph, targetNodeQuery, maxDepth, maxPaths: 1);
        return paths.Count > 0 ? paths[0].Segments : [];
    }

    /// <summary>
    /// Finds up to <paramref name="maxPaths"/> retention paths to the first node matched by
    /// <paramref name="targetNodeQuery"/>: the shortest path from each distinct reachable root,
    /// ranked shortest-first. A single reverse breadth-first traversal from the target builds a
    /// shortest-path tree, so every root that retains the target contributes one canonical chain —
    /// answering "what independent reasons keep this alive?".
    /// </summary>
    public static IReadOnlyList<RetentionPath> FindRetentionPaths(
        DgmlGraph graph,
        string targetNodeQuery,
        int maxDepth = DefaultMaxDepth,
        int maxPaths = DefaultMaxPaths)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (string.IsNullOrWhiteSpace(targetNodeQuery))
            return [];

        if (maxDepth <= 0)
            maxDepth = DefaultMaxDepth;
        if (maxPaths <= 0)
            maxPaths = DefaultMaxPaths;

        var matches = FindMatchingNodes(graph, targetNodeQuery);
        if (matches.Count == 0)
            return [];

        var target = matches[0];
        var nodesById = new Dictionary<string, DgmlNode>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
            nodesById.TryAdd(node.Id, node);

        if (!nodesById.TryGetValue(target.Id, out _))
            return [];

        var incomingCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var reverseAdjacency = new Dictionary<string, List<DgmlEdge>>(StringComparer.Ordinal);
        foreach (var edge in graph.Edges)
        {
            if (!nodesById.ContainsKey(edge.Source) || !nodesById.ContainsKey(edge.Target))
                continue;

            if (!reverseAdjacency.TryGetValue(edge.Target, out var bucket))
            {
                bucket = [];
                reverseAdjacency[edge.Target] = bucket;
            }

            bucket.Add(edge);
            incomingCounts[edge.Target] = incomingCounts.TryGetValue(edge.Target, out var count) ? count + 1 : 1;
        }

        bool IsRoot(DgmlNode node) =>
            string.Equals(node.Category, "Root", StringComparison.OrdinalIgnoreCase) || !incomingCounts.ContainsKey(node.Id);

        // Reverse BFS from the target toward the roots. The predecessor map records, for each visited
        // node, the next node on the shortest hop toward the target plus the original-direction edge.
        var queue = new Queue<(string NodeId, int Depth)>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var predecessors = new Dictionary<string, (string? TowardTargetNodeId, DgmlEdge? Edge)>(StringComparer.Ordinal);

        queue.Enqueue((target.Id, 0));
        visited.Add(target.Id);
        predecessors[target.Id] = (null, null);

        // Collect only (rootId, depth) during the walk; materialize at most maxPaths chains afterward
        // so a graph with many roots sharing a deep tail cannot force roots×depth segment allocations.
        var foundRoots = new List<(string RootId, int Depth)>();

        // The target itself may be a root (Category=Root or no incoming edges): a zero-hop path that,
        // at depth 0, outranks any incoming-root path.
        if (IsRoot(nodesById[target.Id]))
            foundRoots.Add((target.Id, 0));

        while (queue.Count > 0)
        {
            var (currentId, depth) = queue.Dequeue();

            if (!string.Equals(currentId, target.Id, StringComparison.Ordinal)
                && IsRoot(nodesById[currentId]))
            {
                foundRoots.Add((currentId, depth));
            }

            if (depth >= maxDepth)
                continue;

            if (!reverseAdjacency.TryGetValue(currentId, out var incoming))
                continue;

            foreach (var edge in incoming)
            {
                if (!visited.Add(edge.Source))
                    continue;

                predecessors[edge.Source] = (currentId, edge);
                queue.Enqueue((edge.Source, depth + 1));
            }
        }

        return foundRoots
            .OrderBy(root => root.Depth)
            .ThenBy(root => nodesById[root.RootId].Label, StringComparer.Ordinal)
            .ThenBy(root => root.RootId, StringComparer.Ordinal)
            .Take(maxPaths)
            .Select(root => BuildPath(root.RootId, root.Depth, nodesById, predecessors))
            .ToList();
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

    /// <summary>
    /// Reconstructs a root-to-target chain. <paramref name="rootId"/> is the root end; the predecessor
    /// map points toward the target, so walking it forward yields root → … → target directly.
    /// </summary>
    private static RetentionPath BuildPath(
        string rootId,
        int depth,
        Dictionary<string, DgmlNode> nodesById,
        Dictionary<string, (string? TowardTargetNodeId, DgmlEdge? Edge)> predecessors)
    {
        var segments = new List<RetentionPathSegment>();
        var currentId = rootId;
        string? incomingEdgeLabel = null;

        while (true)
        {
            var node = nodesById[currentId];
            segments.Add(new RetentionPathSegment(
                node.Id,
                node.Label,
                node.Category,
                incomingEdgeLabel));

            var predecessor = predecessors[currentId];
            if (predecessor.TowardTargetNodeId is null)
                break;

            incomingEdgeLabel = predecessor.Edge?.Label;
            currentId = predecessor.TowardTargetNodeId;
        }

        var rootNode = nodesById[rootId];
        return new RetentionPath(
            rootNode.Id,
            rootNode.Label,
            rootNode.Category,
            depth,
            new ReadOnlyCollection<RetentionPathSegment>(segments));
    }
}
