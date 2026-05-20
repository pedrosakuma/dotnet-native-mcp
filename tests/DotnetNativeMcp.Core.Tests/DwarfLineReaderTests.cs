using DotnetNativeMcp.Core.Identity;
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

    // Regression test for https://github.com/pedrosakuma/dotnet-native-mcp/issues/53
    // Root cause: (int)unitLength overflowed for large uint values, producing a negative
    // unitEnd that bypassed the bounds check and caused ReadU32 to be called with a
    // negative index, throwing IndexOutOfRangeException.
    [Fact]
    public void DwarfLineReader_UnitLengthOverflow_NeverThrows()
    {
        // Craft a .debug_line unit with unitLength = 0x80000001 (overflows int when added
        // to any positive offset, yielding a negative unitEnd before the fix).
        Span<byte> data = stackalloc byte[8];
        data[0] = 0x01; data[1] = 0x00; data[2] = 0x00; data[3] = 0x80; // unitLength = 0x80000001
        data[4] = 0x02; data[5] = 0x00;                                   // version = 2
        data[6] = 0x00; data[7] = 0x00;

        var image = MakeImage(data.ToArray());
        var ex = Record.Exception(() => DwarfLineReader.Read(image));
        Assert.Null(ex);
    }

    [Fact]
    public void DwarfLineReader_HeaderLengthOverflow_NeverThrows()
    {
        // version=2 unit where headerLength = 0x7FFFFFFF overflows programStart.
        Span<byte> data = stackalloc byte[12];
        data[0] = 0x08; data[1] = 0x00; data[2] = 0x00; data[3] = 0x00; // unitLength = 8
        data[4] = 0x02; data[5] = 0x00;                                   // version = 2
        data[6] = 0xFF; data[7] = 0xFF; data[8] = 0xFF; data[9] = 0x7F;   // headerLength = 0x7FFFFFFF
        data[10] = 0x00; data[11] = 0x00;

        var image = MakeImage(data.ToArray());
        var ex = Record.Exception(() => DwarfLineReader.Read(image));
        Assert.Null(ex);
    }

    [Fact]
    public void DwarfLineReader_LineRangeZero_NeverThrows()
    {
        // Craft a unit with lineRange=0. Before the fix this reached special-opcode
        // arithmetic that divides by lineRange, causing DivideByZeroException.
        // unit: unitLength=20 (LE), version=2, headerLength=12, min_instruction_length=1,
        //   default_is_stmt=1, line_base=0, line_range=0, opcode_base=0
        Span<byte> data = stackalloc byte[24];
        // unit_length = 20 (bytes after this field)
        data[0] = 0x14; data[1] = 0x00; data[2] = 0x00; data[3] = 0x00;
        // version = 2
        data[4] = 0x02; data[5] = 0x00;
        // header_length = 8 (bytes after this field up to the line program)
        data[6] = 0x08; data[7] = 0x00; data[8] = 0x00; data[9] = 0x00;
        // minimum_instruction_length = 1
        data[10] = 0x01;
        // default_is_stmt = 1
        data[11] = 0x01;
        // line_base = 0
        data[12] = 0x00;
        // line_range = 0  ← the hostile value
        data[13] = 0x00;
        // opcode_base = 1 (no standard opcodes, but special opcodes are >= 1)
        data[14] = 0x01;
        // (end of header; program starts at offset 18)
        // Line program: emit a special opcode (e.g. 0x01 which >= opcodeBase=1).
        data[18] = 0x01; // special opcode → adjusted = 1 - 1 = 0, divides by line_range

        var image = MakeImage(data.ToArray());
        var ex = Record.Exception(() => DwarfLineReader.Read(image));
        Assert.Null(ex);
    }

    private static NativeImage MakeImage(byte[] debugLineData)
    {
        var section = new NativeSection(
            ".debug_line",
            VirtualAddress: 0,
            VirtualSize: (ulong)debugLineData.Length,
            FileOffset: 0,
            FileSize: (ulong)debugLineData.Length);

        return new NativeImage(
            ImageHandle.From("0000000000000000", "test.elf"),
            "test.elf",
            BinaryFormat.Elf,
            Architecture.X64,
            [section],
            [],
            new ReadOnlyMemory<byte>(debugLineData),
            imageBase: 0);
    }
}
