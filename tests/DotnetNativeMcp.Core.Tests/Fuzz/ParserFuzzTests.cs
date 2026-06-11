using System.Diagnostics;
using DotnetNativeMcp.Core.Dgml;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Mstat;
using DotnetNativeMcp.Core.R2R;
using DotnetNativeMcp.Core.Strings;
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
    // MachOReader — fat binary and unsupported feature checks
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(unchecked((int)0xDEAD_BEEF))]
    public void MachOReader_ParseFatSlice_RandomBytes_NeverThrows(int seed)
    {
        var sw = Stopwatch.StartNew();
        var rng = new Random(seed);
        for (int i = 0; i < IterationsPerSeed; i++)
        {
            Assert.True(sw.Elapsed < WallClockBudget, "Wall-clock budget exceeded");
            var bytes = GenerateRandomBytes(rng);
            _ = MachOReader.ParseFatSlice(bytes);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(unchecked((int)0xDEAD_BEEF))]
    public void MachOReader_CheckUnsupportedFeatures_RandomBytes_NeverThrows(int seed)
    {
        var sw = Stopwatch.StartNew();
        var rng = new Random(seed);
        for (int i = 0; i < IterationsPerSeed; i++)
        {
            Assert.True(sw.Elapsed < WallClockBudget, "Wall-clock budget exceeded");
            var bytes = GenerateRandomBytes(rng);
            _ = MachOReader.CheckUnsupportedFeatures(bytes);
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
    // EmbeddedPdbExtractor
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(unchecked((int)0xDEAD_BEEF))]
    public void EmbeddedPdbExtractor_RandomBytes_NeverThrows(int seed)
    {
        var sw = Stopwatch.StartNew();
        var rng = new Random(seed);
        for (int i = 0; i < IterationsPerSeed; i++)
        {
            Assert.True(sw.Elapsed < WallClockBudget, "Wall-clock budget exceeded");
            var bytes = GenerateRandomBytes(rng);
            // Prepend MZ magic so the extractor attempts PE parsing.
            if (bytes.Length >= 2) { bytes[0] = 0x4D; bytes[1] = 0x5A; }
            _ = EmbeddedPdbExtractor.TryExtractFromPe(new ReadOnlyMemory<byte>(bytes));
        }
    }

    // -------------------------------------------------------------------------
    // DwarfInfoReader — must exercise compressed and uncompressed paths
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(unchecked((int)0xDEAD_BEEF))]
    public void DwarfInfoReader_RandomDebugInfo_NeverThrows(int seed)
    {
        var sw = Stopwatch.StartNew();
        var rng = new Random(seed);
        for (int i = 0; i < IterationsPerSeed; i++)
        {
            Assert.True(sw.Elapsed < WallClockBudget, "Wall-clock budget exceeded");
            var infoData = GenerateRandomBytes(rng);
            var abbrevData = GenerateRandomBytes(rng);
            var image = MakeImageWithDebugInfo(infoData, abbrevData);
            _ = DwarfInfoReader.TryGetSignatureForRva(image, (ulong)rng.Next(0, 0x100000));
        }
    }

    /// <summary>
    /// Ensures the SHF_COMPRESSED guard path in <see cref="DwarfInfoReader"/> is
    /// exercised: craft a ch_size bomb header; the reader must reject cleanly.
    /// </summary>
    [Fact]
    public void DwarfInfoReader_ZlibBombInDebugInfo_DoesNotThrowOrHang()
    {
        Span<byte> bomb = stackalloc byte[40];
        WriteLe32(bomb, 0, 1);
        WriteLe32(bomb, 4, 0);
        WriteLe64(bomb, 8, (ulong)(256 * 1024 * 1024) + 1);
        WriteLe64(bomb, 16, 1);

        var image = MakeImageWithDebugInfo(bomb.ToArray(), Array.Empty<byte>());
        var sw = Stopwatch.StartNew();
        _ = DwarfInfoReader.TryGetSignatureForRva(image, 0x1000);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "DwarfInfoReader took too long on zlib-bomb input");
    }

    // -------------------------------------------------------------------------
    // StringExtractor (direct random bytes)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(unchecked((int)0xDEAD_BEEF))]
    public void StringExtractor_RandomBytes_NeverThrows(int seed)
    {
        var sw = Stopwatch.StartNew();
        var rng = new Random(seed);
        for (int i = 0; i < IterationsPerSeed; i++)
        {
            Assert.True(sw.Elapsed < WallClockBudget, "Wall-clock budget exceeded");
            var bytes = GenerateRandomBytes(rng);
            int minLen = 1 + rng.Next(8);
            _ = StringExtractor.Extract(bytes, baseRva: 0, ".rodata", minLen, ascii: true, utf16: true, out _);
        }
    }

    // -------------------------------------------------------------------------
    // NativeAotSymbolDemangler (random mangled-like strings)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(unchecked((int)0xDEAD_BEEF))]
    public void NativeAotSymbolDemangler_RandomStrings_NeverThrows(int seed)
    {
        var sw = Stopwatch.StartNew();
        var rng = new Random(seed);
        for (int i = 0; i < IterationsPerSeed; i++)
        {
            Assert.True(sw.Elapsed < WallClockBudget, "Wall-clock budget exceeded");
            var s = GenerateMangledLikeString(rng);
            _ = NativeAotSymbolDemangler.Demangle(s);
            _ = NativeAotSymbolDemangler.LooksLikeNativeAotMangled(s);
            _ = NativeAotSymbolDemangler.Classify(s);
        }
    }

    // -------------------------------------------------------------------------
    // MapFileReader / DgmlReader (random bytes written to a scratch file)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(unchecked((int)0xDEAD_BEEF))]
    public void MapFileReader_RandomBytes_NeverThrows(int seed)
    {
        Directory.CreateDirectory(ScratchDir);
        var scratch = Path.Combine(ScratchDir, $"fuzz-map-{seed}.map");
        var sw = Stopwatch.StartNew();
        var rng = new Random(seed);
        try
        {
            for (int i = 0; i < IterationsPerSeed; i++)
            {
                Assert.True(sw.Elapsed < WallClockBudget, "Wall-clock budget exceeded");
                File.WriteAllBytes(scratch, GenerateRandomBytes(rng));
                _ = MapFileReader.TryMerge(scratch, []);
            }
        }
        finally
        {
            if (File.Exists(scratch)) File.Delete(scratch);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(unchecked((int)0xDEAD_BEEF))]
    public void DgmlReader_RandomBytes_NeverThrows(int seed)
    {
        Directory.CreateDirectory(ScratchDir);
        var scratch = Path.Combine(ScratchDir, $"fuzz-graph-{seed}.dgml");
        var sw = Stopwatch.StartNew();
        var rng = new Random(seed);
        try
        {
            for (int i = 0; i < IterationsPerSeed; i++)
            {
                Assert.True(sw.Elapsed < WallClockBudget, "Wall-clock budget exceeded");
                File.WriteAllBytes(scratch, GenerateRandomBytes(rng));
                _ = DgmlReader.Read(scratch);
            }
        }
        finally
        {
            if (File.Exists(scratch)) File.Delete(scratch);
        }
    }

    // -------------------------------------------------------------------------
    // Import readers / resolvers (synthetic image with random raw bytes)
    //
    // These readers re-parse image.RawBytes from scratch, so feeding a NativeImage
    // whose Format is set but whose bytes are random exercises the same header /
    // offset-chasing code that runs on a real binary.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(unchecked((int)0xDEAD_BEEF))]
    public void ImportReaders_RandomBytes_NeverThrows(int seed)
    {
        var sw = Stopwatch.StartNew();
        var rng = new Random(seed);
        BinaryFormat[] formats = [BinaryFormat.Elf, BinaryFormat.Pe, BinaryFormat.MachO];
        for (int i = 0; i < IterationsPerSeed; i++)
        {
            Assert.True(sw.Elapsed < WallClockBudget, "Wall-clock budget exceeded");
            var bytes = GenerateRandomBytes(rng);
            var format = formats[rng.Next(formats.Length)];
            RunImageReaders(MakeRawImage(format, bytes, Architecture.X64));
        }
    }

    // -------------------------------------------------------------------------
    // Mutation fuzzing over real fixtures.
    //
    // Random bytes almost always bounce off the format header, so they only
    // exercise shallow code. Bit-flipping a real, well-formed binary keeps the
    // header valid and drives corrupted size/count/offset fields deep into the
    // parsers — where the genuinely dangerous offset-chasing lives.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(unchecked((int)0xDEAD_BEEF))]
    public void MutationFuzz_RealFixtures_NeverThrows(int seed)
    {
        string?[] fixtures =
        [
            FixturePaths.SampleAot,
            FixturePaths.MachOX64Object,
            FixturePaths.MachOArm64Object,
            FixturePaths.MachOArm64RichObject,
            FixturePaths.EmbeddedPdbDll,
        ];

        var available = fixtures.Where(f => f is not null && File.Exists(f)).Select(f => f!).ToArray();
        if (available.Length == 0) return; // fixtures not built — nothing to mutate

        var sw = Stopwatch.StartNew();
        var rng = new Random(seed);
        var originals = available.Select(File.ReadAllBytes).ToArray();

        for (int i = 0; i < MutationIterationsPerSeed; i++)
        {
            Assert.True(sw.Elapsed < WallClockBudget, "Wall-clock budget exceeded");
            var original = originals[rng.Next(originals.Length)];
            var mutated = MutateBytes(original, rng);
            var mem = new ReadOnlyMemory<byte>(mutated);

            // Each Read re-validates the format header; the wrong-format ones bail fast.
            RunImageReaders(SafeRead(ElfReader.Read, mem, "fuzz.elf"));
            RunImageReaders(SafeRead(PeNativeReader.Read, mem, "fuzz.dll"));
            RunImageReaders(SafeRead(MachOReader.Read, mem, "fuzz.o"));
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Runs every downstream reader for the image's format, asserting none throw.</summary>
    private static void RunImageReaders(NativeImage? image)
    {
        if (image is null) return;

        switch (image.Format)
        {
            case BinaryFormat.Elf:
                _ = ElfReader.ReadImportedFunctions(image);
                _ = ElfReader.ReadImportedLibraries(image);
                _ = ElfReader.ResolvePltEntries(image);
                break;

            case BinaryFormat.Pe:
                _ = PeNativeReader.ReadImportedFunctions(image);
                _ = PeNativeReader.ReadImportedLibraries(image);
                RunR2RReaders(image);
                break;

            case BinaryFormat.MachO:
                _ = MachOReader.ReadImportedFunctions(image);
                _ = MachOReader.ReadImportedLibraries(image);
                _ = MachOReader.ResolveStubEntries(image);
                _ = MachOReader.ReadExports(image);
                break;
        }

        foreach (var symbol in image.Symbols.Take(64))
            _ = NativeAotSymbolDemangler.Demangle(symbol.Name);
    }

    /// <summary>Drives every ReadyToRun section reader behind a successfully-parsed header.</summary>
    private static void RunR2RReaders(NativeImage image)
    {
        var header = ReadyToRunReader.ReadHeader(image);
        if (header.IsError || header.Data is null) return;
        var hdr = header.Data;

        _ = ReadyToRunReader.ReadImportSections(image, hdr);
        _ = ReadyToRunReader.ReadComponentAssemblies(image, hdr);
        _ = ReadyToRunReader.ReadManifestAssemblyMvids(image, hdr);
        _ = ReadyToRunReader.ReadCompilerIdentifier(image, hdr);
        _ = ReadyToRunReader.ReadOwnerCompositeExecutable(image, hdr);
        _ = ReadyToRunReader.ReadManifestMetadata(image, hdr);
        _ = ReadyToRunReader.ReadMethodDefEntryPoints(image, hdr, limit: 4096);
        _ = ReadyToRunReader.ReadAvailableTypes(image, hdr, limit: 4096);
        _ = ReadyToRunReader.ReadEnclosingTypeMap(image, hdr, limit: 4096);
        _ = ReadyToRunReader.ReadMethodIsGenericMap(image, hdr, limit: 4096);
        _ = ReadyToRunReader.ReadTypeGenericInfoMap(image, hdr, limit: 4096);
        _ = ReadyToRunReader.ReadHotColdMap(image, hdr, limit: 4096);
        _ = ReadyToRunReader.ReadRuntimeFunctions(image, hdr);
        _ = ReadyToRunReader.FindRuntimeFunction(image, hdr, rva: 0x1000);
    }

    /// <summary>Invokes a format reader and converts an unexpected throw into a test failure.</summary>
    private static NativeImage? SafeRead(
        Func<ReadOnlyMemory<byte>, string, NativeImage?> read,
        ReadOnlyMemory<byte> bytes,
        string path)
    {
        try
        {
            return read(bytes, path);
        }
        catch (Exception ex)
        {
            Assert.Fail($"Reader threw on mutated input: {ex.GetType().Name}: {ex.Message}");
            return null; // unreachable
        }
    }

    /// <summary>Clones <paramref name="original"/> and applies 1–16 random byte mutations.</summary>
    private static byte[] MutateBytes(byte[] original, Random rng)
    {
        var buf = (byte[])original.Clone();
        if (buf.Length == 0) return buf;

        int mutations = 1 + rng.Next(16);
        for (int m = 0; m < mutations; m++)
        {
            int pos = rng.Next(buf.Length);
            buf[pos] = (rng.Next(4)) switch
            {
                0 => (byte)rng.Next(256),
                1 => 0x00,
                2 => 0xFF,
                _ => (byte)(buf[pos] ^ (1 << rng.Next(8))),
            };
        }
        return buf;
    }

    /// <summary>Builds a <see cref="NativeImage"/> with the given format and raw bytes but no pre-parsed sections.</summary>
    private static NativeImage MakeRawImage(BinaryFormat format, byte[] raw, Architecture arch) =>
        new(
            ImageHandle.From("0000000000000000", "fuzz.bin"),
            "fuzz.bin",
            format,
            arch,
            [],
            [],
            new ReadOnlyMemory<byte>(raw),
            imageBase: 0);

    /// <summary>
    /// Generates a string biased toward NativeAOT-mangling structural characters so the
    /// fuzzer reaches the generic-bracket and segment-splitting code paths.
    /// </summary>
    private static string GenerateMangledLikeString(Random rng)
    {
        const string alphabet = "abcdeXYZ_<>.,0129__S_P_CoreLib_System_";
        int length = rng.Next(64);
        var chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = alphabet[rng.Next(alphabet.Length)];
        return new string(chars);
    }

    private const int MutationIterationsPerSeed = 500;

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

    /// <summary>
    /// Constructs a minimal <see cref="NativeImage"/> with <c>.debug_info</c> and
    /// <c>.debug_abbrev</c> sections. Used to drive <see cref="DwarfInfoReader"/> directly.
    /// </summary>
    private static NativeImage MakeImageWithDebugInfo(byte[] debugInfoData, byte[] debugAbbrevData)
    {
        var allData = debugInfoData.Concat(debugAbbrevData).ToArray();
        var sections = new List<NativeSection>
        {
            new(".debug_info",
                VirtualAddress: 0,
                VirtualSize: (ulong)debugInfoData.Length,
                FileOffset: 0,
                FileSize: (ulong)debugInfoData.Length),
            new(".debug_abbrev",
                VirtualAddress: (ulong)debugInfoData.Length,
                VirtualSize: (ulong)debugAbbrevData.Length,
                FileOffset: (ulong)debugInfoData.Length,
                FileSize: (ulong)debugAbbrevData.Length),
        };

        return new NativeImage(
            ImageHandle.From(Guid.NewGuid().ToString("N"), "fuzz.elf"),
            "fuzz.elf",
            BinaryFormat.Elf,
            Architecture.X64,
            sections,
            [],
            new ReadOnlyMemory<byte>(allData),
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
