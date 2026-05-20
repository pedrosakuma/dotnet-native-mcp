using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Server.Tools;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class ExplainRetentionToolTests
{
    [Fact]
    public void ExplainRetention_UnknownHandle_ReturnsBinaryNotFound()
    {
        var tool = new NativeTools(new NativeBinaryRegistry());

        var result = tool.ExplainRetention("i:deadbeef:00000000", "MyDbContext");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.BinaryNotFound);
    }

    [Fact]
    public void ExplainRetention_MissingDgml_ReturnsDgmlNotFound()
    {
        var registry = new FakeRegistry();
        registry.Add(CreateImage("/workspace/app.bin"));
        var tool = new NativeTools(registry);

        var result = tool.ExplainRetention(registry.ImageHandle!, "MyDbContext", dgmlPath: "/no/such/file.dgml");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.DgmlNotFound);
    }

    [Fact]
    public void ExplainRetention_EmptyTarget_ReturnsInvalidArgument()
    {
        var registry = new FakeRegistry();
        registry.Add(CreateImage("/workspace/app.bin"));
        var tool = new NativeTools(registry);

        var result = tool.ExplainRetention(registry.ImageHandle!, "   ");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void ExplainRetention_BadMaxDepth_ReturnsInvalidArgument()
    {
        var registry = new FakeRegistry();
        registry.Add(CreateImage("/workspace/app.bin"));
        var tool = new NativeTools(registry);

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
            var tool = new NativeTools(registry);

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

        var tool = new NativeTools(registry);
        var result = tool.ExplainRetention(load.Data!.Handle.Value, "Program", maxDepth: 12);

        result.IsError.Should().BeFalse();
        result.Data.Should().NotBeNull();
        result.Data!.TargetMatchCount.Should().BeGreaterThan(0);
        result.Data.Path.Should().NotBeEmpty();
        result.Data.Path[0].EdgeLabelFromPrevious.Should().BeNull();
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

        public IReadOnlyList<NativeImage> List() => [.. _images.Values];

        public void Add(NativeImage image)
        {
            _images[image.Handle.Value] = image;
            ImageHandle = image.Handle.Value;
        }
    }
}
