using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Disassembly;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Security;
using DotnetNativeMcp.Core.Symbols;
using DotnetNativeMcp.Core.Xref;
using DotnetNativeMcp.Server.Tools;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Server.Tests;

public sealed class NativeToolsPathPolicyTests
{
    private const string Handle = "i:deadbeef:00000000";

    [Fact]
    public void GetSizeBreakdown_EnforcingPolicy_OutOfRootMstatOverride_ReturnsPathNotAllowed()
    {
        var tools = MakeEnforcingTools(out _);

        var result = tools.GetSizeBreakdown(Handle, "method", 25, "/etc/passwd");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.PathNotAllowed);
    }

    [Fact]
    public void ExplainRetention_EnforcingPolicy_OutOfRootDgmlOverride_ReturnsPathNotAllowed()
    {
        var tools = MakeEnforcingTools(out _);

        var result = tools.ExplainRetention(Handle, "Node", "/etc/hosts");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.PathNotAllowed);
    }

    [Fact]
    public void Disassemble_EnforcingPolicy_OutOfRootImagePath_ReturnsPathNotAllowed()
    {
        var tools = MakeEnforcingTools(out _);

        var result = tools.Disassemble(imagePath: "/etc/shadow", rva: 0, size: 16);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.PathNotAllowed);
    }

    [Fact]
    public void Disassemble_EnforcingPolicy_OutOfRootIlMapPath_ReturnsPathNotAllowed()
    {
        var root = Directory.CreateTempSubdirectory("native-mcp-tool-");
        try
        {
            var blob = Path.Combine(root.FullName, "blob.bin");
            File.WriteAllBytes(blob, new byte[64]);
            var tools = MakeEnforcingTools(root.FullName);

            var result = tools.Disassemble(
                imagePath: blob,
                rva: 0,
                size: 16,
                architecture: "x64",
                baseAddress: 0x1000,
                ilMapPath: "/etc/passwd",
                rawBlob: true);

            result.IsError.Should().BeTrue();
            result.Error!.Kind.Should().Be(ErrorKinds.PathNotAllowed);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void GetSizeBreakdown_EnforcingPolicy_DefaultMstatOutsideRoot_ReturnsPathNotAllowed()
    {
        // No override: the default sidecar is derived from the image's (out-of-root) FilePath
        // and must still be validated, so a symlinked/escaping default cannot be read.
        var tools = MakeEnforcingTools(out _);

        var result = tools.GetSizeBreakdown(Handle, "method", 25);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.PathNotAllowed);
    }

    [Fact]
    public void ExplainRetention_EnforcingPolicy_DefaultDgmlOutsideRoot_ReturnsPathNotAllowed()
    {
        var tools = MakeEnforcingTools(out _);

        var result = tools.ExplainRetention(Handle, "Node");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.PathNotAllowed);
    }

    private static NativeTools MakeEnforcingTools(out string rootPath)
    {
        var dir = Directory.CreateTempSubdirectory("native-mcp-tool-");
        rootPath = dir.FullName;
        return MakeEnforcingTools(rootPath);
    }

    private static NativeTools MakeEnforcingTools(string rootPath)
    {
        var policy = new PathAccessPolicy([rootPath], enforcing: true);
        return new NativeTools(new FixedImageRegistry(), new NativeCallGraphCache(), new SourceResolver(), policy);
    }

    private static NativeImage MakeImage() =>
        new(
            ImageHandle.From("deadbeef", "fixture.so"),
            "/tmp/native-mcp-fixture/fixture.so",
            BinaryFormat.Elf,
            Architecture.X64,
            [new NativeSection(".text", 0x1000, 0x100, 0, 0x100)],
            [],
            new byte[0x100],
            0);

    private sealed class FixedImageRegistry : INativeBinaryRegistry
    {
        private readonly NativeImage _image = MakeImage();

        public NativeResult<NativeImage> Load(string path, string? expectedBuildId = null) =>
            NativeResult.Ok("loaded", _image);

        public NativeResult<string> RegisterHint(string path, string? buildId = null) =>
            NativeResult.Ok("registered", path);

        public bool TryGet(string imageHandle, out NativeImage? image)
        {
            image = _image;
            return true;
        }

        public IReadOnlyList<NativeImage> List() => [_image];
    }
}
