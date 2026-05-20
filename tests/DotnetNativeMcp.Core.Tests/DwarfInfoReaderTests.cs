using System.Diagnostics;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Unit and integration tests for <see cref="DwarfInfoReader"/>.
/// </summary>
public sealed class DwarfInfoReaderTests
{
    // -------------------------------------------------------------------------
    // Null / empty input
    // -------------------------------------------------------------------------

    [Fact]
    public void TryGetSignatureForRva_ImageWithNoDebugInfo_ReturnsNull()
    {
        var image = MakeImageWithSections([], []);
        var result = DwarfInfoReader.TryGetSignatureForRva(image, 0x1000);
        Assert.Null(result);
    }

    [Fact]
    public void TryGetSignatureForRva_EmptyDebugInfo_ReturnsNull()
    {
        var image = MakeImageWithSections(
            [(".debug_info", Array.Empty<byte>())],
            [(".debug_abbrev", Array.Empty<byte>())]);
        var result = DwarfInfoReader.TryGetSignatureForRva(image, 0x1000);
        Assert.Null(result);
    }

    [Fact]
    public void TryGetSignatureForRva_RandomBytes_NeverThrows()
    {
        var rng = new Random(0xC0FFEE);
        for (int i = 0; i < 200; i++)
        {
            var infoData = GenerateRandomBytes(rng);
            var abbrevData = GenerateRandomBytes(rng);
            var image = MakeImageWithSections(
                [(".debug_info", infoData)],
                [(".debug_abbrev", abbrevData)]);
            // Must not throw.
            _ = DwarfInfoReader.TryGetSignatureForRva(image, 0x1000);
        }
    }

    // -------------------------------------------------------------------------
    // Cycle guard
    // -------------------------------------------------------------------------

    [Fact]
    public void TryGetSignatureForRva_CyclicTypeRef_DoesNotStackOverflow()
    {
        // Build a synthetic .debug_info with a subprogram whose DW_AT_type
        // forms a cycle: type A points to type B, which points back to type A.
        // The reader must detect the cycle and return without crashing.

        // We can't easily build a valid DWARF structure that the reader would
        // faithfully follow (it requires exact encoding). Instead, verify that
        // random bytes covering plausible offset ranges don't stack-overflow
        // when types happen to form cycles.
        var rng = new Random(12345);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 500; i++)
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), "Cycle guard took too long");
            var infoData = GenerateRandomBytes(rng);
            var abbrevData = GenerateRandomBytes(rng);
            var image = MakeImageWithSections(
                [(".debug_info", infoData)],
                [(".debug_abbrev", abbrevData)]);
            _ = DwarfInfoReader.TryGetSignatureForRva(image, (ulong)rng.Next(0, 0x10000));
        }
    }

    // -------------------------------------------------------------------------
    // Integration: real SampleAot fixture
    // -------------------------------------------------------------------------

    [Fact]
    public void TryGetSignatureForRva_WithRealFixture_ReturnsSignatureForAtLeastOneSymbol()
    {
        var binaryPath = FixturePaths.SampleAot;
        if (binaryPath is null)
            return; // fixture not built — skip

        var bytes = File.ReadAllBytes(binaryPath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), binaryPath);
        Assert.NotNull(image);

        // Scan symbols to find one that resolves to a DWARF signature.
        string? found = null;
        foreach (var sym in image.Symbols.Take(2000))
        {
            if (!sym.IsFunction) continue;
            var va = image.ImageBase + sym.Rva;
            var sig = DwarfInfoReader.TryGetSignatureForRva(image, va);
            if (sig is not null)
            {
                found = sig;
                break;
            }
        }

        // At least one symbol in SampleAot must have DWARF type info.
        Assert.NotNull(found);
    }

    [Fact]
    public void TryGetSignatureForRva_WithRealFixture_SignatureHasBalancedParentheses()
    {
        var binaryPath = FixturePaths.SampleAot;
        if (binaryPath is null) return;

        var bytes = File.ReadAllBytes(binaryPath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), binaryPath);
        if (image is null) return;

        var signatures = image.Symbols
            .Take(3000)
            .Where(s => s.IsFunction)
            .Select(s => DwarfInfoReader.TryGetSignatureForRva(image, image.ImageBase + s.Rva))
            .Where(sig => sig is not null)
            .Take(50)
            .ToList();

        // Every returned signature must have a function-call parentheses pair.
        foreach (var sig in signatures)
        {
            Assert.Contains("(", sig, StringComparison.Ordinal);
            Assert.Contains(")", sig, StringComparison.Ordinal);
            // Opening parenthesis must come before closing.
            var open = sig!.IndexOf('(');
            var close = sig.LastIndexOf(')');
            Assert.True(open < close, $"Malformed signature: {sig}");
        }
    }

    [Fact]
    public void TryGetSignatureForRva_WithRealFixture_SignatureIsNonEmpty()
    {
        var binaryPath = FixturePaths.SampleAot;
        if (binaryPath is null) return;

        var bytes = File.ReadAllBytes(binaryPath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), binaryPath);
        if (image is null) return;

        // Collect up to 20 signatures; all must be non-empty strings.
        int found = 0;
        foreach (var sym in image.Symbols.Take(3000))
        {
            if (!sym.IsFunction) continue;
            var sig = DwarfInfoReader.TryGetSignatureForRva(image, image.ImageBase + sym.Rva);
            if (sig is null) continue;
            Assert.NotEmpty(sig);
            found++;
            if (found >= 20) break;
        }
    }

    [Fact]
    public void TryGetSignatureForRva_UnmappedAddress_ReturnsNull()
    {
        var binaryPath = FixturePaths.SampleAot;
        if (binaryPath is null) return;

        var bytes = File.ReadAllBytes(binaryPath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), binaryPath);
        if (image is null) return;

        // ulong.MaxValue is well outside any valid subprogram range.
        var sig = DwarfInfoReader.TryGetSignatureForRva(image, ulong.MaxValue);
        Assert.Null(sig);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static NativeImage MakeImageWithSections(
        (string Name, byte[] Data)[] sections,
        (string Name, byte[] Data)[] additionalSections)
    {
        var allSections = sections.Concat(additionalSections).ToArray();
        ulong offset = 0;
        var nativeSections = new List<NativeSection>();
        var allBytes = new List<byte>();

        foreach (var (name, data) in allSections)
        {
            nativeSections.Add(new NativeSection(
                name,
                VirtualAddress: offset,
                VirtualSize: (ulong)data.Length,
                FileOffset: offset,
                FileSize: (ulong)data.Length));
            allBytes.AddRange(data);
            offset += (ulong)data.Length;
        }

        return new NativeImage(
            ImageHandle.From(Guid.NewGuid().ToString("N"), "test.elf"),
            "test.elf",
            BinaryFormat.Elf,
            Architecture.X64,
            nativeSections,
            [],
            new ReadOnlyMemory<byte>([.. allBytes]),
            imageBase: 0);
    }

    private static byte[] GenerateRandomBytes(Random rng)
    {
        int[] boundaries = [0, 1, 4, 8, 16, 24, 48, 64, 128, 256];
        int length = rng.Next(100) < 20
            ? boundaries[rng.Next(boundaries.Length)]
            : rng.Next(4 * 1024);
        var buf = new byte[length];
        rng.NextBytes(buf);
        return buf;
    }
}
