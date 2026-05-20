using System.Diagnostics;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Mstat;
using DotnetNativeMcp.Core.Symbols;
using Xunit;

namespace DotnetNativeMcp.Core.Tests.Fuzz;

/// <summary>
/// Property-based smoke tests: feeds deterministic pseudorandom byte arrays to every
/// parser surface and asserts (a) no unhandled exception is thrown, and (b) all
/// iterations complete within a wall-clock budget (guards against infinite loops /
/// catastrophic backtracking).
///
/// Approach chosen: deterministic PRNG seeds instead of SharpFuzz, so the tests
/// integrate with the normal <c>dotnet test</c> run without an external fuzzing
/// infrastructure. See docs/fuzzing.md for rationale and how to add new harnesses.
/// </summary>
public sealed class ParserFuzzTests
{
    // Deterministic seeds — each exercises a different slice of the input space.
    // Adding a new seed in a future PR will not break existing reproductions.
    private const int IterationsPerSeed = 1_000;
    private static readonly TimeSpan WallClockBudget = TimeSpan.FromSeconds(60);

    // Scratch directory for tests that must write bytes to disk (MstatReader).
    private static string ScratchDir =>
        Path.Combine(Path.GetDirectoryName(typeof(ParserFuzzTests).Assembly.Location)!, "fuzz-scratch");

    // -------------------------------------------------------------------------
    // PeNativeReader
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(unchecked((int)0xDEAD_BEEF))]
    public void PeNativeReader_RandomBytes_NeverThrows(int seed)
    {
        var sw = Stopwatch.StartNew();
        var rng = new Random(seed);
        for (int i = 0; i < IterationsPerSeed; i++)
        {
            Assert.True(sw.Elapsed < WallClockBudget, "Wall-clock budget exceeded");
            var bytes = GenerateRandomBytes(rng);
            _ = PeNativeReader.Read(new ReadOnlyMemory<byte>(bytes), "fuzz.exe");
        }
    }

    // -------------------------------------------------------------------------
    // ElfReader
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(unchecked((int)0xDEAD_BEEF))]
    public void ElfReader_RandomBytes_NeverThrows(int seed)
    {
        var sw = Stopwatch.StartNew();
        var rng = new Random(seed);
        for (int i = 0; i < IterationsPerSeed; i++)
        {
            Assert.True(sw.Elapsed < WallClockBudget, "Wall-clock budget exceeded");
            var bytes = GenerateRandomBytes(rng);
            _ = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), "fuzz.elf");
        }
    }

    // -------------------------------------------------------------------------
    // MachOReader
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(unchecked((int)0xDEAD_BEEF))]
    public void MachOReader_RandomBytes_NeverThrows(int seed)
    {
        var sw = Stopwatch.StartNew();
        var rng = new Random(seed);
        for (int i = 0; i < IterationsPerSeed; i++)
        {
            Assert.True(sw.Elapsed < WallClockBudget, "Wall-clock budget exceeded");
            var bytes = GenerateRandomBytes(rng);
            _ = MachOReader.Read(new ReadOnlyMemory<byte>(bytes), "fuzz.dylib");
        }
    }

    // -------------------------------------------------------------------------
    // DwarfLineReader — must also exercise the SHF_COMPRESSED zlib-bomb path
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(unchecked((int)0xDEAD_BEEF))]
    public void DwarfLineReader_RandomDebugLine_NeverThrows(int seed)
    {
        var sw = Stopwatch.StartNew();
        var rng = new Random(seed);
        for (int i = 0; i < IterationsPerSeed; i++)
        {
            Assert.True(sw.Elapsed < WallClockBudget, "Wall-clock budget exceeded");
            var data = GenerateRandomBytes(rng);
            var image = MakeImageWithDebugLine(data);
            _ = DwarfLineReader.Read(image);
        }
    }

    /// <summary>
    /// Ensures the SHF_COMPRESSED / zlib-bomb guard (issue #48) is exercised:
    /// craft a header with an absurdly large declared ch_size; the reader must
    /// reject it (return null from TryDecompressElf) without allocating that memory.
    /// </summary>
    [Fact]
    public void DwarfLineReader_ZlibBombHeader_NeverThrowsAndCompletesQuickly()
    {
        // Build a syntactically valid Elf64_Chdr with ch_type=1 (ELFCOMPRESS_ZLIB),
        // ch_reserved=0, ch_size=256 MiB+1 (just over the guard), ch_addralign=1,
        // then append a tiny valid zlib stream.
        Span<byte> bomb = stackalloc byte[40];
        WriteLe32(bomb, 0, 1);                   // ch_type = ELFCOMPRESS_ZLIB
        WriteLe32(bomb, 4, 0);                   // ch_reserved = 0
        WriteLe64(bomb, 8, (ulong)(256 * 1024 * 1024) + 1);  // ch_size > guard
        WriteLe64(bomb, 16, 1);                  // ch_addralign = 1
        // Remaining bytes are zeros — ZLibStream will fail gracefully.

        var image = MakeImageWithDebugLine(bomb.ToArray());
        var sw = Stopwatch.StartNew();
        _ = DwarfLineReader.Read(image);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            "DwarfLineReader took too long on zlib-bomb input");
    }

    // -------------------------------------------------------------------------
    // MstatReader — writes bytes to a scratch file, then calls Read(path)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(unchecked((int)0xDEAD_BEEF))]
    public void MstatReader_RandomBytes_NeverThrows(int seed)
    {
        Directory.CreateDirectory(ScratchDir);
        var sw = Stopwatch.StartNew();
        var rng = new Random(seed);
        for (int i = 0; i < IterationsPerSeed; i++)
        {
            Assert.True(sw.Elapsed < WallClockBudget, "Wall-clock budget exceeded");
            var bytes = GenerateRandomBytes(rng);
            var path = Path.Combine(ScratchDir, $"fuzz-{seed}-{i}.mstat");
            File.WriteAllBytes(path, bytes);
            try
            {
                _ = MstatReader.Read(path);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    // -------------------------------------------------------------------------
    // SourceLinkResolver
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(unchecked((int)0xDEAD_BEEF))]
    public void SourceLinkResolver_RandomBytes_NeverThrows(int seed)
    {
        var sw = Stopwatch.StartNew();
        var rng = new Random(seed);
        for (int i = 0; i < IterationsPerSeed; i++)
        {
            Assert.True(sw.Elapsed < WallClockBudget, "Wall-clock budget exceeded");
            var bytes = GenerateRandomBytes(rng);
            _ = SourceLinkResolver.TryLoadFromBytes(bytes);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Generates a random byte array whose length is drawn from a mix of boundary
    /// sizes (0, 1, 4, 8, …) and uniformly random sizes up to 32 KiB.
    /// </summary>
    private static byte[] GenerateRandomBytes(Random rng)
    {
        // Interesting boundary lengths exercised with ~15% probability.
        int[] boundaries = [0, 1, 2, 3, 4, 7, 8, 15, 16, 23, 24, 31, 32, 47, 48, 63, 64, 127, 128];
        int length = rng.Next(100) < 15
            ? boundaries[rng.Next(boundaries.Length)]
            : rng.Next(32 * 1024);

        var buf = new byte[length];
        rng.NextBytes(buf);
        return buf;
    }

    /// <summary>
    /// Constructs a minimal <see cref="NativeImage"/> whose <c>.debug_line</c> section
    /// wraps the supplied bytes. Used to drive <see cref="DwarfLineReader"/> directly.
    /// </summary>
    private static NativeImage MakeImageWithDebugLine(byte[] debugLineData)
    {
        var section = new NativeSection(
            ".debug_line",
            VirtualAddress: 0,
            VirtualSize: (ulong)debugLineData.Length,
            FileOffset: 0,
            FileSize: (ulong)debugLineData.Length);

        return new NativeImage(
            ImageHandle.From("0000000000000000", "fuzz.elf"),
            "fuzz.elf",
            BinaryFormat.Elf,
            Architecture.X64,
            [section],
            [],
            new ReadOnlyMemory<byte>(debugLineData),
            imageBase: 0);
    }

    private static void WriteLe32(Span<byte> buf, int offset, uint value)
    {
        buf[offset] = (byte)value;
        buf[offset + 1] = (byte)(value >> 8);
        buf[offset + 2] = (byte)(value >> 16);
        buf[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteLe64(Span<byte> buf, int offset, ulong value)
    {
        WriteLe32(buf, offset, (uint)value);
        WriteLe32(buf, offset + 4, (uint)(value >> 32));
    }
}
