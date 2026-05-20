using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// End-to-end integration tests for <see cref="DwarfLineReader"/> against the real
/// NativeAOT SampleAot ELF binary. Tests skip cleanly when the fixture is not built.
/// </summary>
public sealed class DwarfLineReaderIntegrationTests
{
    [Fact]
    public void Read_WithRealFixture_ReturnsNonEmptyRowList()
    {
        var binaryPath = FixturePaths.SampleAot;
        if (binaryPath is null)
            return; // AOT toolchain unavailable — skip

        var bytes = File.ReadAllBytes(binaryPath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), binaryPath);
        Assert.NotNull(image);

        var rows = DwarfLineReader.Read(image);

        // NativeAOT with StripSymbols=false always embeds DWARF line tables.
        Assert.NotEmpty(rows);
    }

    [Fact]
    public void Read_WithRealFixture_RowsAreSortedByAddress()
    {
        var binaryPath = FixturePaths.SampleAot;
        if (binaryPath is null)
            return;

        var bytes = File.ReadAllBytes(binaryPath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), binaryPath);
        if (image is null) return;

        var rows = DwarfLineReader.Read(image);
        if (rows.Count < 2) return;

        for (var i = 1; i < rows.Count; i++)
            Assert.True(rows[i].Address >= rows[i - 1].Address,
                $"Rows not sorted at index {i}: {rows[i - 1].Address:X} > {rows[i].Address:X}");
    }

    [Fact]
    public void Read_WithRealFixture_AllRowsHavePositiveLineNumbers()
    {
        var binaryPath = FixturePaths.SampleAot;
        if (binaryPath is null)
            return;

        var bytes = File.ReadAllBytes(binaryPath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), binaryPath);
        if (image is null) return;

        var rows = DwarfLineReader.Read(image);

        foreach (var row in rows)
        {
            Assert.True(row.Line > 0, $"Row at address {row.Address:X} has non-positive line: {row.Line}");
            Assert.NotEmpty(row.File);
        }
    }

    [Fact]
    public void FindRow_WithRealFixture_MatchesKnownSymbolAddress()
    {
        var binaryPath = FixturePaths.SampleAot;
        if (binaryPath is null)
            return;

        var bytes = File.ReadAllBytes(binaryPath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), binaryPath);
        Assert.NotNull(image);

        var rows = DwarfLineReader.Read(image);
        Assert.NotEmpty(rows);

        // FindRow must return a row for at least one symbol address.
        DwarfLineReader.LineRow? found = null;
        foreach (var sym in image.Symbols.Take(1000))
        {
            var va = image.ImageBase + sym.Rva;
            var row = DwarfLineReader.FindRow(rows, va);
            if (row is not null)
            {
                found = row;
                break;
            }
        }

        Assert.NotNull(found);
        Assert.NotEmpty(found.Value.File);
        Assert.True(found.Value.Line > 0);
    }

    [Fact]
    public void FindRow_WithAddressBeforeFirstRow_ReturnsNull()
    {
        var binaryPath = FixturePaths.SampleAot;
        if (binaryPath is null)
            return;

        var bytes = File.ReadAllBytes(binaryPath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), binaryPath);
        if (image is null) return;

        var rows = DwarfLineReader.Read(image);
        if (rows.Count == 0) return;

        // Address before the first DWARF row must return null.
        var beforeFirst = rows[0].Address > 0 ? rows[0].Address - 1 : 0;
        if (beforeFirst < rows[0].Address)
            Assert.Null(DwarfLineReader.FindRow(rows, beforeFirst));
    }
}
