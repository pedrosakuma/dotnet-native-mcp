using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using DotnetNativeMcp.Server.Tools;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class ExplainRetentionToolTests
{
    [Fact]
    public void ExplainRetention_UnknownHandle_ReturnsBinaryNotFound()
    {
        var tool = new NativeTools(new NativeBinaryRegistry(), new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());

        var result = tool.ExplainRetention("i:deadbeef:00000000", "MyDbContext");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.BinaryNotFound);
    }

    [Fact]
    public void ExplainRetention_MissingDgml_ReturnsDgmlNotFound()
    {
        var registry = new FakeRegistry();
        registry.Add(CreateImage("/workspace/app.bin"));
        var tool = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());

        var result = tool.ExplainRetention(registry.ImageHandle!, "MyDbContext", dgmlPath: "/no/such/file.dgml");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.DgmlNotFound);
    }

    [Fact]
    public void ExplainRetention_EmptyTarget_ReturnsInvalidArgument()
    {
        var registry = new FakeRegistry();
        registry.Add(CreateImage("/workspace/app.bin"));
        var tool = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());

        var result = tool.ExplainRetention(registry.ImageHandle!, "   ");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void ExplainRetention_BadMaxDepth_ReturnsInvalidArgument()
    {
        var registry = new FakeRegistry();
        registry.Add(CreateImage("/workspace/app.bin"));
        var tool = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());

        var result = tool.ExplainRetention(registry.ImageHandle!, "MyDbContext", maxDepth: 65);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void ExplainRetention_SyntheticDgml_ReturnsShortestPath()
    {
        var dgmlPath = WriteScratchFile("synthetic.dgml", """
            <DirectedGraph xmlns="http://schemas.microsoft.com/vs/2009/dgml">
              <Nodes>
                <Node Id="root" Label="App root" Category="Root" />
                <Node Id="json" Label="Newtonsoft.Json reflection hook" />
                <Node Id="db" Label="MyDbContext" />
              </Nodes>
              <Links>
                <Link Source="root" Target="json" Label="Uses" />
                <Link Source="json" Target="db" Label="Reflects" />
              </Links>
            </DirectedGraph>
            """);

        try
        {
            var registry = new FakeRegistry();
            registry.Add(CreateImage("/workspace/app.bin"));
            var tool = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());

            var result = tool.ExplainRetention(registry.ImageHandle!, "MyDbContext", dgmlPath: dgmlPath, maxDepth: 4);

            result.IsError.Should().BeFalse();
            result.Data.Should().NotBeNull();
            result.Data!.TargetMatchCount.Should().Be(1);
            result.Data.MatchedNodeId.Should().Be("db");
            result.Data.Path.Select(node => node.Id).Should().Equal("root", "json", "db");
            result.Data.Path[1].EdgeLabelFromPrevious.Should().Be("Uses");
            result.Data.Path[2].EdgeLabelFromPrevious.Should().Be("Reflects");
        }
        finally
        {
            File.Delete(dgmlPath);
        }
    }

    [Fact]
    public void ExplainRetention_SampleAot_ReturnsAtLeastOneRootChain()
    {
        var fixturePath = FixturePaths.SampleAot;
        var dgmlPath = FixturePaths.SampleAotDgml;
        if (fixturePath is null || dgmlPath is null || !File.Exists(fixturePath) || !File.Exists(dgmlPath))
            return;

        var registry = new NativeBinaryRegistry();
        var load = registry.Load(fixturePath);
        load.IsError.Should().BeFalse();

        var tool = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());
        var result = tool.ExplainRetention(load.Data!.Handle.Value, "Program", maxDepth: 12);

        result.IsError.Should().BeFalse();
        result.Data.Should().NotBeNull();
        result.Data!.TargetMatchCount.Should().BeGreaterThan(0);
        result.Data.Path.Should().NotBeEmpty();
        result.Data.Path[0].EdgeLabelFromPrevious.Should().BeNull();
    }

    [Fact]
    public void ExplainRetention_MultipleRoots_ReturnsRankedPathsAndCandidates()
    {
        var dgmlPath = WriteScratchFile("multi.dgml", """
            <DirectedGraph xmlns="http://schemas.microsoft.com/vs/2009/dgml">
              <Nodes>
                <Node Id="entry" Label="Program.Main" Category="Root" />
                <Node Id="refl" Label="ReflectionRoot" Category="Root" />
                <Node Id="mid" Label="Middle" />
                <Node Id="target" Label="MyDbContext" />
              </Nodes>
              <Links>
                <Link Source="entry" Target="target" Reason="call" />
                <Link Source="refl" Target="mid" Reason="Reflectable type" />
                <Link Source="mid" Target="target" Reason="Field written outside initializer" />
              </Links>
            </DirectedGraph>
            """);

        try
        {
            var registry = new FakeRegistry();
            registry.Add(CreateImage("/workspace/app.bin"));
            var tool = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());

            var result = tool.ExplainRetention(registry.ImageHandle!, "MyDbContext", dgmlPath: dgmlPath, maxDepth: 6, maxPaths: 5);

            result.IsError.Should().BeFalse();
            result.Data!.MatchedNodeId.Should().Be("target");
            result.Data.Paths.Should().HaveCount(2);
            result.Data.Paths[0].RootId.Should().Be("entry");
            result.Data.Paths[0].Depth.Should().Be(1);
            result.Data.Paths[0].Nodes[^1].EdgeLabelFromPrevious.Should().Be("call");
            result.Data.Paths[1].RootId.Should().Be("refl");
            result.Data.Paths[1].Nodes[1].EdgeLabelFromPrevious.Should().Be("Reflectable type");
            // Path mirrors the shortest path (Paths[0]).
            result.Data.Path.Select(n => n.Id).Should().Equal("entry", "target");
        }
        finally
        {
            File.Delete(dgmlPath);
        }
    }

    [Fact]
    public void ExplainRetention_AmbiguousQuery_SurfacesCandidates()
    {
        var dgmlPath = WriteScratchFile("ambiguous.dgml", """
            <DirectedGraph xmlns="http://schemas.microsoft.com/vs/2009/dgml">
              <Nodes>
                <Node Id="root" Label="App root" Category="Root" />
                <Node Id="a" Label="System.Collections.Generic.List`1[int]" />
                <Node Id="b" Label="System.Collections.Generic.List`1[string]" />
              </Nodes>
              <Links>
                <Link Source="root" Target="a" Reason="call" />
                <Link Source="root" Target="b" Reason="call" />
              </Links>
            </DirectedGraph>
            """);

        try
        {
            var registry = new FakeRegistry();
            registry.Add(CreateImage("/workspace/app.bin"));
            var tool = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());

            var result = tool.ExplainRetention(registry.ImageHandle!, "List`1", dgmlPath: dgmlPath, maxDepth: 4);

            result.IsError.Should().BeFalse();
            result.Data!.TargetMatchCount.Should().Be(2);
            result.Data.Candidates.Select(c => c.Id).Should().Equal("a", "b");
            result.Data.MatchedNodeId.Should().Be("a");
        }
        finally
        {
            File.Delete(dgmlPath);
        }
    }

    [Fact]
    public void ExplainRetention_BadMaxPaths_ReturnsInvalidArgument()
    {
        var registry = new FakeRegistry();
        registry.Add(CreateImage("/workspace/app.bin"));
        var tool = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());

        var result = tool.ExplainRetention(registry.ImageHandle!, "MyDbContext", maxPaths: 0);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void ExplainRetention_ReflectionPath_ClassifiedReflectionDriven()
    {
        var dgmlPath = WriteScratchFile("reflection.dgml", """
            <DirectedGraph xmlns="http://schemas.microsoft.com/vs/2009/dgml">
              <Nodes>
                <Node Id="root" Label="ReflectionRoot" Category="Root" />
                <Node Id="mid" Label="Middle" />
                <Node Id="target" Label="MyDbContext" />
              </Nodes>
              <Links>
                <Link Source="root" Target="mid" Reason="call" />
                <Link Source="mid" Target="target" Reason="Reflectable type" />
              </Links>
            </DirectedGraph>
            """);

        try
        {
            var registry = new FakeRegistry();
            registry.Add(CreateImage("/workspace/app.bin"));
            var tool = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());

            var result = tool.ExplainRetention(registry.ImageHandle!, "MyDbContext", dgmlPath: dgmlPath, maxDepth: 6);

            result.IsError.Should().BeFalse();
            result.Data!.Paths.Should().HaveCount(1);
            result.Data.Paths[0].ReflectionDriven.Should().BeTrue();
            result.Data.Paths[0].Classification.Should().Be("reflection-driven");
            // Root has no incoming edge; intermediate edge is code; final edge is reflection.
            result.Data.Paths[0].Nodes[0].EdgeKind.Should().BeNull();
            result.Data.Paths[0].Nodes[1].EdgeKind.Should().Be("DirectCode");
            result.Data.Paths[0].Nodes[2].EdgeKind.Should().Be("Reflection");
        }
        finally
        {
            File.Delete(dgmlPath);
        }
    }

    [Fact]
    public void ExplainRetention_AllCodePath_ClassifiedStructural()
    {
        var dgmlPath = WriteScratchFile("structural.dgml", """
            <DirectedGraph xmlns="http://schemas.microsoft.com/vs/2009/dgml">
              <Nodes>
                <Node Id="root" Label="Program.Main" Category="Root" />
                <Node Id="target" Label="MyService" />
              </Nodes>
              <Links>
                <Link Source="root" Target="target" Reason="call" />
              </Links>
            </DirectedGraph>
            """);

        try
        {
            var registry = new FakeRegistry();
            registry.Add(CreateImage("/workspace/app.bin"));
            var tool = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());

            var result = tool.ExplainRetention(registry.ImageHandle!, "MyService", dgmlPath: dgmlPath, maxDepth: 6);

            result.IsError.Should().BeFalse();
            result.Data!.Paths.Should().HaveCount(1);
            result.Data.Paths[0].ReflectionDriven.Should().BeFalse();
            result.Data.Paths[0].Classification.Should().Be("structural");
        }
        finally
        {
            File.Delete(dgmlPath);
        }
    }

    [Fact]
    public void ExplainRetention_SampleAot_PricesNodesFromSiblingMstat()
    {
        var fixturePath = FixturePaths.SampleAot;
        var dgmlPath = FixturePaths.SampleAotDgml;
        var mstatPath = FixturePaths.SampleAotMstat;
        if (fixturePath is null || dgmlPath is null || mstatPath is null
            || !File.Exists(fixturePath) || !File.Exists(dgmlPath) || !File.Exists(mstatPath))
            return;

        var registry = new NativeBinaryRegistry();
        var load = registry.Load(fixturePath);
        load.IsError.Should().BeFalse();

        var tool = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());
        var result = tool.ExplainRetention(load.Data!.Handle.Value, "OnFirstChanceException", maxDepth: 32);

        result.IsError.Should().BeFalse();
        result.Data!.SizeCostNote.Should().BeNull("the sibling .mstat sidecar should resolve and price nodes");

        if (result.Data.Paths.Count == 0)
            return;

        var path = result.Data.Paths[0];
        path.PricedNodeCount.Should().BeLessThanOrEqualTo(path.Nodes.Count);
        path.PricedBytes.Should().Be(path.Nodes.Where(n => n.SizeBytes.HasValue).Sum(n => n.SizeBytes!.Value));

        // The target node is the last on the path; it is a CoreLib method mstat prices.
        var target = path.Nodes[^1];
        target.Label.Should().EndWith("AppContext__OnFirstChanceException");
        target.SizeBytes.Should().NotBeNull();
        target.SizeMatchKind.Should().Be("method");
        target.SizeBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ExplainRetention_SizeCostDisabled_LeavesNodesUnpriced()
    {
        var dgmlPath = WriteScratchFile("nosize.dgml", """
            <DirectedGraph xmlns="http://schemas.microsoft.com/vs/2009/dgml">
              <Nodes>
                <Node Id="root" Label="Program.Main" Category="Root" />
                <Node Id="target" Label="MyService" />
              </Nodes>
              <Links>
                <Link Source="root" Target="target" Reason="call" />
              </Links>
            </DirectedGraph>
            """);

        try
        {
            var registry = new FakeRegistry();
            registry.Add(CreateImage("/workspace/app.bin"));
            var tool = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());

            var result = tool.ExplainRetention(registry.ImageHandle!, "MyService", dgmlPath: dgmlPath, maxDepth: 6, includeSizeCost: false);

            result.IsError.Should().BeFalse();
            result.Data!.SizeCostNote.Should().BeNull();
            result.Data.Paths.Should().HaveCount(1);
            result.Data.Paths[0].PricedBytes.Should().Be(0);
            result.Data.Paths[0].PricedNodeCount.Should().Be(0);
            result.Data.Paths[0].Nodes.Should().OnlyContain(n => n.SizeBytes == null);
        }
        finally
        {
            File.Delete(dgmlPath);
        }
    }

    private static string WriteScratchFile(string fileName, string content)
    {
        var directory = Path.Combine(Path.GetDirectoryName(typeof(ExplainRetentionToolTests).Assembly.Location)!, "scratch");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}-{fileName}");
        File.WriteAllText(path, content);
        return path;
    }

    private static NativeImage CreateImage(string filePath) =>
        new(
            ImageHandle.From("deadbeef", Path.GetFileName(filePath)),
            filePath,
            BinaryFormat.Elf,
            Architecture.X64,
            [],
            [],
            ReadOnlyMemory<byte>.Empty,
            0);

    private sealed class FakeRegistry : INativeBinaryRegistry
    {
        private readonly Dictionary<string, NativeImage> _images = new(StringComparer.OrdinalIgnoreCase);

        public string? ImageHandle { get; private set; }

        public NativeResult<NativeImage> Load(string path, string? expectedBuildId = null) =>
            throw new NotSupportedException();

        public bool TryGet(string imageHandle, out NativeImage? image) =>
            _images.TryGetValue(imageHandle, out image);

        public DotnetNativeMcp.Core.NativeResult<string> RegisterHint(string path, string? buildId = null) => DotnetNativeMcp.Core.NativeResult.Ok("registered", path);

        public IReadOnlyList<NativeImage> List() => [.. _images.Values];

        public void Add(NativeImage image)
        {
            _images[image.Handle.Value] = image;
            ImageHandle = image.Handle.Value;
        }
    }
}
