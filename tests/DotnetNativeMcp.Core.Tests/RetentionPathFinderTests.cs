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
}
