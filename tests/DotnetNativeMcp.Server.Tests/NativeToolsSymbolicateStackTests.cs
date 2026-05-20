using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using DotnetNativeMcp.Server.Tools;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Server.Tests;

public class NativeToolsSymbolicateStackTests
{
    [Fact]
    public void SymbolicateStack_MixedRows_ReturnsPerRowOutcomesAndHint()
    {
        var image = CreateImage(
            "aabb",
            "mixed.so",
            new NativeSymbol(0, "S_P_MyApp_Program__Main", NativeAotSymbolDemangler.Demangle("S_P_MyApp_Program__Main"), 0x1010, 0x20, ".text", true));
        var tools = new NativeTools(new TestBinaryRegistry(image));

        var result = tools.SymbolicateStack(
            [
                new NativeFrameInput("1015"),
                new NativeFrameInput("not-hex"),
                new NativeFrameInput("1050")
            ],
            image.Handle.Value);

        result.IsError.Should().BeFalse();
        result.Data.Should().HaveCount(3);

        var rows = result.Data!;
        rows[0].MangledName.Should().Be("S_P_MyApp_Program__Main");
        rows[0].OffsetFromSymbolStart.Should().Be(5);
        var secondError = rows[1].Error;
        secondError.Should().NotBeNull();
        secondError!.Kind.Should().Be(ErrorKinds.InvalidArgument);
        secondError.Kind.Should().NotBe(ErrorKinds.AddressOutOfRange);
        var thirdError = rows[2].Error;
        thirdError.Should().NotBeNull();
        thirdError!.Kind.Should().Be(ErrorKinds.SymbolNotFound);
        result.Hints.Should().ContainSingle();
        result.Hints[0].NextTool.Should().Be("disassemble");
        result.Hints[0].SuggestedArguments.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["imageHandle"] = image.Handle.Value,
            ["address"] = "0000000000001015",
        });
    }

    [Fact]
    public void SymbolicateStack_PerFrameImageHandleOverrideBeatsDefault()
    {
        var defaultImage = CreateImage(
            "aabb",
            "default.so",
            new NativeSymbol(0, "S_P_Default_Method", NativeAotSymbolDemangler.Demangle("S_P_Default_Method"), 0x1010, 0x20, ".text", true));
        var overrideImage = CreateImage(
            "ccdd",
            "override.so",
            new NativeSymbol(0, "S_P_Override_Method", NativeAotSymbolDemangler.Demangle("S_P_Override_Method"), 0x1010, 0x20, ".text", true));
        var tools = new NativeTools(new TestBinaryRegistry(defaultImage, overrideImage));

        var result = tools.SymbolicateStack(
            [new NativeFrameInput("1010", overrideImage.Handle.Value)],
            defaultImage.Handle.Value);

        result.IsError.Should().BeFalse();
        result.Data.Should().ContainSingle();
        var row = result.Data![0];
        row.ImageHandle.Should().Be(overrideImage.Handle.Value);
        row.MangledName.Should().Be("S_P_Override_Method");
    }

    [Fact]
    public void SymbolicateStack_RoundTripsListNativeSymbolsRvas()
    {
        var image = CreateImage(
            "eeff",
            "roundtrip.so",
            new NativeSymbol(0, "S_P_RoundTrip_First", NativeAotSymbolDemangler.Demangle("S_P_RoundTrip_First"), 0x1010, 0x10, ".text", true),
            new NativeSymbol(1, "S_P_RoundTrip_Second", NativeAotSymbolDemangler.Demangle("S_P_RoundTrip_Second"), 0x1030, 0x10, ".text", true));
        var tools = new NativeTools(new TestBinaryRegistry(image));

        var listed = tools.ListNativeSymbols(image.Handle.Value, pageSize: 10);
        listed.IsError.Should().BeFalse();

        var frames = listed.Data!.Symbols
            .Select(symbol => new NativeFrameInput(symbol.RvaHex))
            .ToList();

        var resolved = tools.SymbolicateStack(frames, image.Handle.Value);

        resolved.IsError.Should().BeFalse();
        resolved.Data!.Select(row => row.MangledName).Should().Equal(listed.Data.Symbols.Select(symbol => symbol.Name));
    }

    private static NativeImage CreateImage(string buildId, string fileName, params NativeSymbol[] symbols)
    {
        var handle = ImageHandle.From(buildId, fileName);
        var section = new NativeSection(".text", 0x1000, 0x100, 0, 0x100);
        return new NativeImage(handle, fileName, BinaryFormat.Elf, Architecture.X64, [section], symbols, new byte[0x100], 0);
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

        public IReadOnlyList<NativeImage> List() => [.. _images.Values];
    }
}
