using DotnetNativeMcp.Cli.Output;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Security;
using DotnetNativeMcp.Core.Symbols;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Cli.Tests;

public sealed class ResolveCommandTests
{
    [Fact]
    public void Resolve_SyntheticBatch_ReturnsResolvedRows()
    {
        var image = CreateImage(
            "synthetic",
            "synthetic.so",
            new NativeSymbol(0, "S_P_Sample_Main", "Sample.Main", 0x1010, 0x20, ".text", true));
        var registry = new StaticRegistry((CanonicalPath(ImagePath), image));

        var result = ResolveCommandFactory.Resolve(
            registry,
            new SourceResolver(),
            PathAccessPolicy.Permissive,
            ImagePath,
            ["0x1010", "0x1015"]);

        result.IsError.Should().BeFalse(result.Error?.Message ?? string.Empty);
        result.Data!.Resolutions.Should().HaveCount(2);
        result.Data.Resolutions[0].MangledName.Should().Be("S_P_Sample_Main");
        result.Data.Resolutions[1].Displacement.Should().Be(5);
    }

    [Fact]
    public void Resolve_SampleAotBatch_ReturnsResolvedRowsWithSource()
    {
        var fixturePath = CliFixturePaths.SampleAot;
        if (fixturePath is null)
        {
            return;
        }

        var load = NativeImageLoader.Load(fixturePath);
        load.IsError.Should().BeFalse(load.Error?.Message ?? string.Empty);

        var image = load.Data!;
        var sourceResolver = new SourceResolver();
        var target = image.Symbols
            .Select(symbol => new
            {
                Symbol = symbol,
                Source = sourceResolver.TrySourceFor(image, image.ImageBase + symbol.Rva),
            })
            .FirstOrDefault(candidate => candidate.Source is not null);

        target.Should().NotBeNull("the SampleAot fixture should resolve at least one source location");

        var result = ResolveCommandFactory.Resolve(
            new NativeBinaryRegistry(PathAccessPolicy.Permissive),
            new SourceResolver(),
            PathAccessPolicy.Permissive,
            fixturePath,
            [$"0x{target!.Symbol.Rva:x}", $"0x{target.Symbol.Rva + 1:x}"]);

        result.IsError.Should().BeFalse(result.Error?.Message ?? string.Empty);
        result.Data!.Resolutions.Should().HaveCount(2);
        result.Data.Resolutions.Should().OnlyContain(row => row.Error == null);
        result.Data.Resolutions[0].MangledName.Should().Be(target.Symbol.Name);
        result.Data.Resolutions[0].Source.Should().NotBeNull();
    }

    [Fact]
    public async Task Resolve_TableOutput_RendersAddressSymbolAndSourceColumns()
    {
        var result = BuildSyntheticResult();

        var writer = new StringWriter();
        await new TableOutputWriter(writer).WriteAsync(result);

        var output = writer.ToString();
        output.Should().Contain("address");
        output.Should().Contain("symbol");
        output.Should().Contain("source");
        output.Should().Contain("Sample.Main");
    }

    [Fact]
    public async Task Resolve_JsonOutput_PreservesResolutionPayload()
    {
        var result = BuildSyntheticResult();

        var writer = new StringWriter();
        await new JsonOutputWriter(writer).WriteAsync(result);

        var output = writer.ToString();
        output.Should().Contain("\"resolutions\"");
        output.Should().Contain("\"mangledName\"");
        output.Should().Contain("\"source\"");
    }

    [Fact]
    public void Resolve_PathPolicyDenial_ReturnsPathNotAllowedWithoutLoading()
    {
        var allowedRoot = Path.Combine(AppContext.BaseDirectory, "allowed-root");
        var outsidePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "outside-root", "fixture.so"));
        Directory.CreateDirectory(allowedRoot);

        var registry = new RecordingRegistry();
        var policy = new PathAccessPolicy([allowedRoot], enforcing: true);

        var result = ResolveCommandFactory.Resolve(
            registry,
            new SourceResolver(),
            policy,
            outsidePath,
            ["0x1000"]);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.PathNotAllowed);
        registry.LoadCalls.Should().Be(0);
    }

    private const string ImagePath = "/virtual/resolve-image.so";

    private static NativeResult<ResolveCommandData> BuildSyntheticResult()
    {
        var image = CreateImage(
            "synthetic",
            "synthetic.so",
            new NativeSymbol(0, "S_P_Sample_Main", "Sample.Main", 0x1010, 0x20, ".text", true));
        var registry = new StaticRegistry((CanonicalPath(ImagePath), image));

        return ResolveCommandFactory.Resolve(
            registry,
            new SourceResolver(),
            PathAccessPolicy.Permissive,
            ImagePath,
            ["0x1010"]);
    }

    private static NativeResult<ResolveCommandData>? BuildSampleTableResult()
    {
        var fixturePath = CliFixturePaths.SampleAot;
        if (fixturePath is null)
        {
            return null;
        }

        var load = NativeImageLoader.Load(fixturePath);
        if (load.IsError)
        {
            return null;
        }

        var image = load.Data!;
        var sourceResolver = new SourceResolver();
        var target = image.Symbols
            .Select(symbol => new
            {
                Symbol = symbol,
                Source = sourceResolver.TrySourceFor(image, image.ImageBase + symbol.Rva),
            })
            .FirstOrDefault(candidate => candidate.Source is not null);

        return target is null
            ? null
            : ResolveCommandFactory.Resolve(
                new NativeBinaryRegistry(PathAccessPolicy.Permissive),
                new SourceResolver(),
                PathAccessPolicy.Permissive,
                fixturePath,
                [$"0x{target.Symbol.Rva:x}"]);
    }

    private static NativeImage CreateImage(string buildId, string fileName, params NativeSymbol[] symbols)
    {
        var handle = DotnetNativeMcp.Core.Identity.ImageHandle.From(buildId, fileName);
        var section = new NativeSection(".text", 0x1000, 0x100, 0, 0x100);
        return new NativeImage(handle, CanonicalPath(fileName), BinaryFormat.Elf, Architecture.X64, [section], symbols, new byte[0x100], 0);
    }

    private static string CanonicalPath(string path) => Path.GetFullPath(path);

    private sealed class RecordingRegistry : INativeBinaryRegistry
    {
        public int LoadCalls { get; private set; }

        public NativeResult<NativeImage> Load(string path, string? expectedBuildId = null)
        {
            LoadCalls++;
            throw new InvalidOperationException("Load should not be called when path validation fails.");
        }

        public NativeResult<string> RegisterHint(string path, string? buildId = null) =>
            NativeResult.Ok("registered", path);

        public bool TryGet(string imageHandle, out NativeImage? image)
        {
            image = null;
            return false;
        }

        public IReadOnlyList<NativeImage> List() => [];
    }

    private sealed class StaticRegistry(params (string Path, NativeImage Image)[] images) : INativeBinaryRegistry
    {
        private readonly Dictionary<string, NativeImage> _byPath = images.ToDictionary(
            pair => pair.Path,
            pair => pair.Image,
            StringComparer.OrdinalIgnoreCase);

        public NativeResult<NativeImage> Load(string path, string? expectedBuildId = null) =>
            _byPath.TryGetValue(path, out var image)
                ? NativeResult.Ok("loaded", image)
                : NativeResult.Fail<NativeImage>(ErrorKinds.BinaryNotFound, $"Binary not found: '{Path.GetFileName(path)}'.");

        public NativeResult<string> RegisterHint(string path, string? buildId = null) =>
            NativeResult.Ok("registered", path);

        public bool TryGet(string imageHandle, out NativeImage? image)
        {
            image = _byPath.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.Handle.Value, imageHandle, StringComparison.OrdinalIgnoreCase));
            return image is not null;
        }

        public IReadOnlyList<NativeImage> List() => [.. _byPath.Values];
    }
}
