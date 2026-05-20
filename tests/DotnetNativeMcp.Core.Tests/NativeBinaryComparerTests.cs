using DotnetNativeMcp.Core.Diff;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class NativeBinaryComparerTests
{
    [Fact]
    public void Compare_TracksAddedRemovedAndChangedSymbols()
    {
        var baseline = CreateImage(
            buildId: "aaaa",
            rawSize: 1000,
            sections:
            [
                new NativeSection(".text", 0x1000, 0x100, 0x200, 100),
                new NativeSection(".data", 0x2000, 0x80, 0x400, 50),
            ],
            symbols:
            [
                new NativeSymbol(0, "A", "A", 0x1010, 10, ".text", true),
                new NativeSymbol(1, "B", "B", 0x1020, 20, ".text", true),
                new NativeSymbol(2, "Gone", "Gone", 0x2010, 5, ".data", false),
            ]);

        var current = CreateImage(
            buildId: "bbbb",
            rawSize: 1300,
            sections:
            [
                new NativeSection(".text", 0x1000, 0x100, 0x200, 140),
                new NativeSection(".rdata", 0x3000, 0x40, 0x500, 30),
            ],
            symbols:
            [
                new NativeSymbol(0, "A", "A", 0x1010, 30, ".text", true),
                new NativeSymbol(1, "B", "B", 0x1020, 20, ".text", true),
                new NativeSymbol(2, "New", "New", 0x3010, 40, ".rdata", false),
            ]);

        var result = NativeBinaryComparer.Compare(baseline, current, topN: 50);

        result.IsBuildIdEqual.Should().BeFalse();
        result.TotalBinarySizeDeltaBytes.Should().Be(300);
        result.AddedSymbolCount.Should().Be(1);
        result.RemovedSymbolCount.Should().Be(1);
        result.ChangedSymbolCount.Should().Be(1);
        result.Verdict.Should().Be(BinaryDiffVerdict.Grew);

        result.SectionDeltas.Should().BeEquivalentTo(
            [
                new SectionSizeDelta(".data", 50, 0, -50),
                new SectionSizeDelta(".text", 100, 140, 40),
                new SectionSizeDelta(".rdata", 0, 30, 30),
            ],
            options => options.WithStrictOrdering());

        result.AddedSymbols.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new AddedSymbolDelta("New", "New", 0x3010, 40, ".rdata", false));
        result.RemovedSymbols.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new RemovedSymbolDelta("Gone", "Gone", 0x2010, 5, ".data", false));
        result.ChangedSymbols.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ChangedSymbolDelta("A", "A", 0x1010, 0x1010, 0, 10, 30, 20, ".text", ".text", true));
    }

    [Fact]
    public void Compare_IdenticalImages_ReturnsNoDeltas()
    {
        var image = CreateImage(
            buildId: "same",
            rawSize: 512,
            sections: [new NativeSection(".text", 0x1000, 0x100, 0x200, 128)],
            symbols: [new NativeSymbol(0, "A", "A", 0x1010, 16, ".text", true)]);

        var result = NativeBinaryComparer.Compare(image, image, topN: 10);

        result.IsBuildIdEqual.Should().BeTrue();
        result.IsFormatEqual.Should().BeTrue();
        result.IsArchitectureEqual.Should().BeTrue();
        result.TotalBinarySizeDeltaBytes.Should().Be(0);
        result.SectionDeltas.Should().BeEmpty();
        result.AddedSymbolCount.Should().Be(0);
        result.RemovedSymbolCount.Should().Be(0);
        result.ChangedSymbolCount.Should().Be(0);
        result.Verdict.Should().Be(BinaryDiffVerdict.NoChange);
    }

    [Fact]
    public void Compare_AppliesTopNCapPerSymbolCategory()
    {
        var baseline = CreateImage(
            buildId: "base",
            rawSize: 200,
            sections: [new NativeSection(".text", 0x1000, 0x100, 0x200, 100)],
            symbols:
            [
                new NativeSymbol(0, "ChangedBig", "ChangedBig", 0x1000, 10, ".text", true),
                new NativeSymbol(1, "ChangedSmall", "ChangedSmall", 0x1010, 10, ".text", true),
                new NativeSymbol(2, "RemovedBig", "RemovedBig", 0x1020, 60, ".text", true),
                new NativeSymbol(3, "RemovedSmall", "RemovedSmall", 0x1030, 20, ".text", true),
            ]);

        var current = CreateImage(
            buildId: "curr",
            rawSize: 220,
            sections: [new NativeSection(".text", 0x1000, 0x100, 0x200, 120)],
            symbols:
            [
                new NativeSymbol(0, "ChangedBig", "ChangedBig", 0x1000, 80, ".text", true),
                new NativeSymbol(1, "ChangedSmall", "ChangedSmall", 0x1010, 15, ".text", true),
                new NativeSymbol(2, "AddedBig", "AddedBig", 0x1040, 90, ".text", true),
                new NativeSymbol(3, "AddedSmall", "AddedSmall", 0x1050, 25, ".text", true),
            ]);

        var result = NativeBinaryComparer.Compare(baseline, current, topN: 1);

        result.AddedSymbolCount.Should().Be(2);
        result.RemovedSymbolCount.Should().Be(2);
        result.ChangedSymbolCount.Should().Be(2);

        result.AddedSymbols.Should().ContainSingle();
        result.AddedSymbols[0].Name.Should().Be("AddedBig");
        result.RemovedSymbols.Should().ContainSingle();
        result.RemovedSymbols[0].Name.Should().Be("RemovedBig");
        result.ChangedSymbols.Should().ContainSingle();
        result.ChangedSymbols[0].Name.Should().Be("ChangedBig");
    }

    private static NativeImage CreateImage(
        string buildId,
        int rawSize,
        IReadOnlyList<NativeSection> sections,
        IReadOnlyList<NativeSymbol> symbols,
        BinaryFormat format = BinaryFormat.Elf,
        Architecture architecture = Architecture.X64,
        string filePath = "/workspace/sample") =>
        new(
            ImageHandle.From(buildId, Path.GetFileName(filePath)),
            filePath,
            format,
            architecture,
            sections,
            symbols,
            new byte[rawSize],
            0);
}
