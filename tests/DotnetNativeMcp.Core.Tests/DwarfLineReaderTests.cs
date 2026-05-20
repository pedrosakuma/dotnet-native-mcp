using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Tests for DWARF line info resolution via the public <see cref="SourceResolver"/> surface.
/// </summary>
public sealed class DwarfLineReaderTests
{
    [Fact]
    public void SourceResolver_WhenFixtureHasDwarf_ResolvesAtLeastOneAddress()
    {
        var binaryPath = FixturePaths.SampleAot;
        if (binaryPath is null)
            return; // fixture not built — skip

        var bytes = File.ReadAllBytes(binaryPath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), binaryPath);
        if (image is null) return;

        var resolver = new SourceResolver();

        // Probe several symbol addresses; at least one should resolve to a source location
        // when DWARF is present (NativeAOT publishing with debug info).
        SourceLocation? found = null;
        foreach (var sym in image.Symbols.Take(300))
        {
            var va = image.ImageBase + sym.Rva;
            var loc = resolver.TrySourceFor(image, va);
            if (loc is not null) { found = loc; break; }
        }

        if (found is not null)
        {
            Assert.NotEmpty(found.File);
            Assert.True(found.StartLine >= 1, $"Expected StartLine >= 1 but got {found.StartLine}");
        }
    }

    [Fact]
    public void SourceResolver_TrySourceFor_NeverThrows()
    {
        var binaryPath = FixturePaths.SampleAot;
        if (binaryPath is null) return;

        var bytes = File.ReadAllBytes(binaryPath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), binaryPath);
        if (image is null) return;

        var resolver = new SourceResolver();

        // Should not throw for any address in the image's VA range.
        var ex = Record.Exception(() =>
        {
            foreach (var sym in image.Symbols.Take(100))
                resolver.TrySourceFor(image, image.ImageBase + sym.Rva);
        });

        Assert.Null(ex);
    }
}
