using DotnetNativeMcp.Core.Dgml;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class RetentionPathFinderTests
{
    [Fact]
    public void FindShortestPath_SingleRoot_ReturnsRootToTargetChain()
    {
        var graph = new DgmlGraph(
            "synthetic.dgml",
            [
                new DgmlNode("root", "App root", "Root"),
                new DgmlNode("json", "Newtonsoft.Json", null),
                new DgmlNode("db", "MyDbContext", null),
            ],
            [
                new DgmlEdge("root", "json", "Uses"),
                new DgmlEdge("json", "db", "Reflects"),
            ]);

        var path = RetentionPathFinder.FindShortestPath(graph, "db", maxDepth: 4);

        path.Select(segment => segment.NodeId).Should().Equal("root", "json", "db");
        path[1].IncomingEdgeLabel.Should().Be("Uses");
        path[2].IncomingEdgeLabel.Should().Be("Reflects");
    }

    [Fact]
    public void FindShortestPath_MultipleRoots_PicksShortestPath()
    {
        var graph = new DgmlGraph(
            "synthetic.dgml",
            [
                new DgmlNode("slow-root", "Slow root", "Root"),
                new DgmlNode("fast-root", "Fast root", "Root"),
                new DgmlNode("middle", "Middle", null),
                new DgmlNode("target", "Target Type", null),
            ],
            [
                new DgmlEdge("slow-root", "middle", null),
                new DgmlEdge("middle", "target", null),
                new DgmlEdge("fast-root", "target", null),
            ]);

        var path = RetentionPathFinder.FindShortestPath(graph, "Target Type", maxDepth: 4);

        path.Select(segment => segment.NodeId).Should().Equal("fast-root", "target");
    }

    [Fact]
    public void FindShortestPath_TargetNotFound_ReturnsEmpty()
    {
        var graph = new DgmlGraph(
            "synthetic.dgml",
            [new DgmlNode("root", "App root", "Root")],
            []);

        var path = RetentionPathFinder.FindShortestPath(graph, "Missing", maxDepth: 2);

        path.Should().BeEmpty();
    }

    [Fact]
    public void FindShortestPath_NoPathToTarget_ReturnsEmpty()
    {
        var graph = new DgmlGraph(
            "synthetic.dgml",
            [
                new DgmlNode("root", "App root", "Root"),
                new DgmlNode("helper", "Helper", null),
                new DgmlNode("isolated", "Target Type", null),
            ],
            [
                new DgmlEdge("isolated", "helper", null),
                new DgmlEdge("helper", "isolated", null),
            ]);

        var path = RetentionPathFinder.FindShortestPath(graph, "Target Type", maxDepth: 2);

        path.Should().BeEmpty();
    }

    [Fact]
    public void FindShortestPath_Cycle_DoesNotLoopAndStillFindsTarget()
    {
        var graph = new DgmlGraph(
            "synthetic.dgml",
            [
                new DgmlNode("root", "App root", "Root"),
                new DgmlNode("a", "A", null),
                new DgmlNode("b", "B", null),
                new DgmlNode("target", "Target Type", null),
            ],
            [
                new DgmlEdge("root", "a", null),
                new DgmlEdge("a", "b", null),
                new DgmlEdge("b", "a", null),
                new DgmlEdge("b", "target", "Keeps"),
            ]);

        var path = RetentionPathFinder.FindShortestPath(graph, "Target Type", maxDepth: 6);

        path.Select(segment => segment.NodeId).Should().Equal("root", "a", "b", "target");
        path[^1].IncomingEdgeLabel.Should().Be("Keeps");
    }

    [Fact]
    public void FindRetentionPaths_MultipleRoots_ReturnsOneShortestPathPerRoot()
    {
        var graph = new DgmlGraph(
            "synthetic.dgml",
            [
                new DgmlNode("slow-root", "Slow root", "Root"),
                new DgmlNode("fast-root", "Fast root", "Root"),
                new DgmlNode("middle", "Middle", null),
                new DgmlNode("target", "Target Type", null),
            ],
            [
                new DgmlEdge("slow-root", "middle", "Uses"),
                new DgmlEdge("middle", "target", "Reflects"),
                new DgmlEdge("fast-root", "target", "call"),
            ]);

        var paths = RetentionPathFinder.FindRetentionPaths(graph, "Target Type", maxDepth: 6, maxPaths: 5);

        paths.Should().HaveCount(2);
        // Shortest-first: the single-hop fast-root path ranks ahead of the two-hop slow-root path.
        paths[0].RootId.Should().Be("fast-root");
        paths[0].Depth.Should().Be(1);
        paths[0].Segments.Select(s => s.NodeId).Should().Equal("fast-root", "target");
        paths[0].Segments[^1].IncomingEdgeLabel.Should().Be("call");

        paths[1].RootId.Should().Be("slow-root");
        paths[1].Depth.Should().Be(2);
        paths[1].Segments.Select(s => s.NodeId).Should().Equal("slow-root", "middle", "target");
        paths[1].Segments[^1].IncomingEdgeLabel.Should().Be("Reflects");
    }

    [Fact]
    public void FindRetentionPaths_MaxPathsOne_ReturnsOnlyShortest()
    {
        var graph = new DgmlGraph(
            "synthetic.dgml",
            [
                new DgmlNode("slow-root", "Slow root", "Root"),
                new DgmlNode("fast-root", "Fast root", "Root"),
                new DgmlNode("middle", "Middle", null),
                new DgmlNode("target", "Target Type", null),
            ],
            [
                new DgmlEdge("slow-root", "middle", null),
                new DgmlEdge("middle", "target", null),
                new DgmlEdge("fast-root", "target", null),
            ]);

        var paths = RetentionPathFinder.FindRetentionPaths(graph, "Target Type", maxDepth: 6, maxPaths: 1);

        paths.Should().HaveCount(1);
        paths[0].RootId.Should().Be("fast-root");
    }

    [Fact]
    public void FindTargets_AmbiguousQuery_ReturnsAllMatchesCapped()
    {
        var graph = new DgmlGraph(
            "synthetic.dgml",
            [
                new DgmlNode("0", "List<int>", null),
                new DgmlNode("1", "List<string> backing", null),
                new DgmlNode("2", "Unrelated", null),
            ],
            []);

        var targets = RetentionPathFinder.FindTargets(graph, "List", maxResults: 25);

        targets.Select(t => t.NodeId).Should().Equal("0", "1");
    }

    [Fact]
    public void FindTargets_ExactIdMatch_TakesPrecedenceOverLabelSubstring()
    {
        var graph = new DgmlGraph(
            "synthetic.dgml",
            [
                new DgmlNode("MyType", "alpha", null),
                new DgmlNode("other", "MyType reference", null),
            ],
            []);

        var targets = RetentionPathFinder.FindTargets(graph, "MyType", maxResults: 25);

        targets.Should().HaveCount(1);
        targets[0].NodeId.Should().Be("MyType");
    }

    [Fact]
    public void FindRetentionPaths_TargetIsAlsoRoot_PrefersZeroHopPath()
    {
        // Target is itself a Root but also reached by another root via an incoming edge.
        // The canonical shortest retention path is the zero-hop [target].
        var graph = new DgmlGraph(
            "synthetic.dgml",
            [
                new DgmlNode("other-root", "Other root", "Root"),
                new DgmlNode("target", "Target Type", "Root"),
            ],
            [
                new DgmlEdge("other-root", "target", "call"),
            ]);

        var paths = RetentionPathFinder.FindRetentionPaths(graph, "Target Type", maxDepth: 6, maxPaths: 1);

        paths.Should().HaveCount(1);
        paths[0].RootId.Should().Be("target");
        paths[0].Depth.Should().Be(0);
        paths[0].Segments.Select(s => s.NodeId).Should().Equal("target");
    }
}
