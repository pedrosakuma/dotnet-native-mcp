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
        Assert.NotNull(image);

        // The NativeAOT publish with StripSymbols=false always includes .debug_line data.
        var debugLineSection = image.Sections.FirstOrDefault(s => s.Name == ".debug_line");
        Assert.NotNull(debugLineSection);
        Assert.True(debugLineSection.FileSize > 0, ".debug_line must be non-empty");

        // DwarfLineReader must parse at least one row from the section.
        var rows = DwarfLineReader.Read(image);
        Assert.NotEmpty(rows);

        // Every row must have a non-empty file name and a positive line number.
        foreach (var row in rows.Take(10))
        {
            Assert.NotEmpty(row.File);
            Assert.True(row.Line > 0, $"Expected line > 0 for file {row.File}");
        }
    }

    [Fact]
    public void SourceResolver_WhenFixtureHasDwarf_ResolvesSymbolToSourceLocation()
    {
        var binaryPath = FixturePaths.SampleAot;
        if (binaryPath is null)
            return; // fixture not built — skip

        var bytes = File.ReadAllBytes(binaryPath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), binaryPath);
        Assert.NotNull(image);

        var resolver = new SourceResolver();

        // Probe symbol addresses; at least one must resolve when DWARF is present.
        SourceLocation? found = null;
        foreach (var sym in image.Symbols.Take(500))
        {
            var va = image.ImageBase + sym.Rva;
            var loc = resolver.TrySourceFor(image, va);
            if (loc is not null)
            {
                found = loc;
                break;
            }
        }

        // The fixture has DWARF (.debug_line section is present), so at least one symbol
        // must resolve to a valid source location.
        Assert.NotNull(found);
        Assert.NotEmpty(found.File);
        Assert.True(found.StartLine >= 1, $"Expected StartLine >= 1 but got {found.StartLine}");
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
