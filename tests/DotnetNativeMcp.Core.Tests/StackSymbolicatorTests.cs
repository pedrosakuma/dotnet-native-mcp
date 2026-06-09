using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class StackSymbolicatorTests
{
    [Fact]
    public void SymbolicateStack_ZeroFrames_ReturnsInvalidArgument()
    {
        var registry = new TestBinaryRegistry();

        var result = StackSymbolicator.SymbolicateStack(registry, []);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void SymbolicateStack_Exactly201Frames_ReturnsInvalidArgument()
    {
        var image = CreateImage();
        var registry = new TestBinaryRegistry(image);
        var frames = Enumerable.Range(0, 201)
            .Select(_ => new NativeFrameInput("1010", image.Handle.Value))
            .ToList();

        var result = StackSymbolicator.SymbolicateStack(registry, frames);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void SymbolicateStack_MalformedHexRow_ReturnsInvalidArgumentPerRow()
    {
        var image = CreateImage();
        var registry = new TestBinaryRegistry(image);

        var result = StackSymbolicator.SymbolicateStack(registry, [new NativeFrameInput("nope", image.Handle.Value)]);

        result.IsError.Should().BeFalse();
        result.Data.Should().ContainSingle();
        var row = result.Data![0];
        row.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
        row.Error.Kind.Should().NotBe(ErrorKinds.AddressOutOfRange);
    }

    [Fact]
    public void SymbolicateStack_AddressInsideSectionWithoutSymbol_ReturnsSymbolNotFoundPerRow()
    {
        var image = CreateImage();
        var registry = new TestBinaryRegistry(image);

        var result = StackSymbolicator.SymbolicateStack(registry, [new NativeFrameInput("1050", image.Handle.Value)]);

        result.IsError.Should().BeFalse();
        result.Data.Should().ContainSingle();
        var row = result.Data![0];
        row.Error!.Kind.Should().Be(ErrorKinds.SymbolNotFound);
        row.SectionName.Should().Be(".text");
    }

    [Fact]
    public void SymbolicateStack_AddressOutsideEverySection_ReturnsAddressOutOfRangePerRow()
    {
        var image = CreateImage();
        var registry = new TestBinaryRegistry(image);

        var result = StackSymbolicator.SymbolicateStack(registry, [new NativeFrameInput("3000", image.Handle.Value)]);

        result.IsError.Should().BeFalse();
        result.Data.Should().ContainSingle();
        result.Data![0].Error!.Kind.Should().Be(ErrorKinds.AddressOutOfRange);
    }

    private static NativeImage CreateImage()
    {
        var handle = ImageHandle.From("aabb", "stack-tests.so");
        var section = new NativeSection(".text", 0x1000, 0x100, 0, 0x100);
        var symbol = new NativeSymbol(
            0,
            "S_P_MyApp_Program__Main",
            NativeAotSymbolDemangler.Demangle("S_P_MyApp_Program__Main"),
            0x1010,
            0x20,
            ".text",
            true);

        return new NativeImage(handle, "stack-tests.so", BinaryFormat.Elf, Architecture.X64, [section], [symbol], new byte[0x100], 0);
    }

    private sealed class TestBinaryRegistry(params NativeImage[] images) : INativeBinaryRegistry
    {
        private readonly Dictionary<string, NativeImage> _images = images.ToDictionary(image => image.Handle.Value, StringComparer.OrdinalIgnoreCase);

        public NativeResult<NativeImage> Load(string path, string? expectedBuildId = null) =>
            throw new NotSupportedException();

        public bool TryGet(string imageHandle, out NativeImage? image)
        {
            var found = _images.TryGetValue(imageHandle, out var resolved);
            image = resolved;
            return found;
        }

        public DotnetNativeMcp.Core.NativeResult<string> RegisterHint(string path, string? buildId = null) => DotnetNativeMcp.Core.NativeResult.Ok("registered", path);

        public IReadOnlyList<NativeImage> List() => [.. _images.Values];
    }
}
