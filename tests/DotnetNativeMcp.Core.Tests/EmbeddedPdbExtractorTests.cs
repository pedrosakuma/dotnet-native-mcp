using DotnetNativeMcp.Core.Symbols;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Tests for <see cref="EmbeddedPdbExtractor"/> — extracts an embedded portable PDB
/// from a managed PE built with <c>&lt;DebugType&gt;embedded&lt;/DebugType&gt;</c>.
/// </summary>
public sealed class EmbeddedPdbExtractorTests
{
    [Fact]
    public void TryExtractFromPe_EmptyBytes_ReturnsNull()
    {
        Assert.Null(EmbeddedPdbExtractor.TryExtractFromPe(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void TryExtractFromPe_NotMzBytes_ReturnsNull()
    {
        var data = new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0x02 }; // ELF magic
        Assert.Null(EmbeddedPdbExtractor.TryExtractFromPe(new ReadOnlyMemory<byte>(data)));
    }

    [Fact]
    public void TryExtractFromPe_TruncatedMz_ReturnsNull()
    {
        // Only MZ, no PE header.
        var data = new byte[] { 0x4D, 0x5A };
        Assert.Null(EmbeddedPdbExtractor.TryExtractFromPe(new ReadOnlyMemory<byte>(data)));
    }

    [Fact]
    public void TryExtractFromPe_RandomBytes_ReturnsNull()
    {
        var rng = new Random(42);
        var data = new byte[1024];
        data[0] = 0x4D; data[1] = 0x5A; // MZ
        rng.NextBytes(data.AsSpan(2));
        // Must not throw, must return null (random data is not a valid PE with embedded PDB).
        Assert.Null(EmbeddedPdbExtractor.TryExtractFromPe(new ReadOnlyMemory<byte>(data)));
    }

    [Fact]
    public void TryExtractFromPe_WithEmbeddedPdbFixture_ReturnsBsjbMagicBytes()
    {
        var dllPath = FixturePaths.EmbeddedPdbDll;
        if (dllPath is null)
            return; // fixture not built — skip

        var bytes = File.ReadAllBytes(dllPath);
        var pdbBytes = EmbeddedPdbExtractor.TryExtractFromPe(new ReadOnlyMemory<byte>(bytes));

        // Must return non-null and start with BSJB portable PDB magic.
        Assert.NotNull(pdbBytes);
        Assert.True(pdbBytes.Length >= 4);
        var magic = BitConverter.ToUInt32(pdbBytes, 0);
        Assert.Equal(0x424A5342u, magic); // BSJB
    }

    [Fact]
    public void TryExtractFromPe_WithNativeAotElf_ReturnsNull()
    {
        // NativeAOT ELF binaries are not PE — must return null cleanly.
        var elfPath = FixturePaths.SampleAot;
        if (elfPath is null)
            return;

        var bytes = File.ReadAllBytes(elfPath);
        var result = EmbeddedPdbExtractor.TryExtractFromPe(new ReadOnlyMemory<byte>(bytes));
        Assert.Null(result); // ELF is not a PE — no embedded PDB
    }

    [Fact]
    public void TryExtractFromPe_WithSiblingPdbDll_ReturnsNull()
    {
        // A DLL without embedded PDB (uses sibling .pdb sidecar) must return null.
        var sampleDll = FixturePaths.SampleAot;
        if (sampleDll is null) return;

        var dllPath = Path.ChangeExtension(sampleDll, ".dll");
        if (!File.Exists(dllPath)) return; // SampleAot is an ELF executable anyway

        var bytes = File.ReadAllBytes(dllPath);
        var result = EmbeddedPdbExtractor.TryExtractFromPe(new ReadOnlyMemory<byte>(bytes));
        // ELF binary — not a PE at all.
        Assert.Null(result);
    }
}
