using System.Buffers.Binary;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Tests for <see cref="MachOReader"/>, including thin x64, thin arm64, fat (universal)
/// binary parsing, unsupported feature detection, and end-to-end tool roundtrips.
///
/// All Mach-O bytes are synthesized by <see cref="MachOTestData"/> — no Apple toolchain
/// is required on the build host.
/// </summary>
public class MachOReaderTests
{
    // -------------------------------------------------------------------------
    // IsMachO / IsFatBinary
    // -------------------------------------------------------------------------

    [Fact]
    public void IsMachO_EmptyBytes_ReturnsFalse()
    {
        MachOReader.IsMachO([]).Should().BeFalse();
        MachOReader.IsMachO([0xCF, 0xFA]).Should().BeFalse();
    }

    [Fact]
    public void IsMachO_MagicLE64_ReturnsTrue()
    {
        // 0xFEEDFACF in LE = CF FA ED FE
        MachOReader.IsMachO([0xCF, 0xFA, 0xED, 0xFE]).Should().BeTrue();
    }

    [Fact]
    public void IsMachO_MagicLE32_ReturnsTrue()
    {
        // 0xFEEDFACE in LE = CE FA ED FE
        MachOReader.IsMachO([0xCE, 0xFA, 0xED, 0xFE]).Should().BeTrue();
    }

    [Fact]
    public void IsMachO_ElfMagic_ReturnsFalse()
    {
        MachOReader.IsMachO([0x7F, 0x45, 0x4C, 0x46]).Should().BeFalse();
    }

    [Fact]
    public void IsFatBinary_FatMagic_ReturnsTrue()
    {
        // CA FE BA BE on disk; LE read = 0xBEBAFECA
        MachOReader.IsFatBinary([0xCA, 0xFE, 0xBA, 0xBE]).Should().BeTrue();
    }

    [Fact]
    public void IsFatBinary_FatMagic64_ReturnsTrue()
    {
        // CA FE BA BF on disk; LE read = 0xBFBAFECA
        MachOReader.IsFatBinary([0xCA, 0xFE, 0xBA, 0xBF]).Should().BeTrue();
    }

    [Fact]
    public void IsFatBinary_MachO64Magic_ReturnsFalse()
    {
        MachOReader.IsFatBinary([0xCF, 0xFA, 0xED, 0xFE]).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Read — thin binaries
    // -------------------------------------------------------------------------

    [Fact]
    public void Read_EmptyBytes_ReturnsNull()
    {
        MachOReader.Read(ReadOnlyMemory<byte>.Empty, "empty").Should().BeNull();
    }

    [Fact]
    public void Read_NonMachO_ReturnsNull()
    {
        var elf = new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x00, 0x00 };
        MachOReader.Read(new ReadOnlyMemory<byte>(elf), "test.elf").Should().BeNull();
    }

    [Fact]
    public void Read_MinimalMachO64_Arm64_ParsesArchitecture()
    {
        var bytes = MachOTestData.ThinArm64();
        var image = MachOReader.Read(new ReadOnlyMemory<byte>(bytes), "test");
        image.Should().NotBeNull();
        image!.Architecture.Should().Be(Architecture.Arm64);
        image.Format.Should().Be(BinaryFormat.MachO);
    }

    [Fact]
    public void Read_MinimalMachO64_X64_ParsesArchitecture()
    {
        var bytes = MachOTestData.ThinX64();
        var image = MachOReader.Read(new ReadOnlyMemory<byte>(bytes), "test");
        image.Should().NotBeNull();
        image!.Architecture.Should().Be(Architecture.X64);
    }

    [Fact]
    public void Read_ThinWithLcUuid_BuildIdFromUuid()
    {
        var uuid = new byte[16];
        for (var i = 0; i < 16; i++) uuid[i] = (byte)(i + 1);

        var bytes = MachOTestData.ThinArm64WithUuid(uuid);
        var image = MachOReader.Read(new ReadOnlyMemory<byte>(bytes), "test.dylib")!;
        image.Should().NotBeNull();
        // BuildId.Extract reads LC_UUID; first 16 bytes = 01..10
        image.Handle.BuildIdHex.Should().Be("0102030405060708090a0b0c0d0e0f10");
    }

    [Fact]
    public void Read_ThinWithSymtab_ParsesSymbols()
    {
        var bytes = MachOTestData.ThinArm64WithSymbol("_RhpNewFast", 0x1000UL);
        var image = MachOReader.Read(new ReadOnlyMemory<byte>(bytes), "test.dylib")!;
        image.Should().NotBeNull();
        // Leading '_' should be stripped
        image.Symbols.Should().ContainSingle(s => s.Name == "RhpNewFast" && s.Rva == 0x1000UL);
    }

    [Fact]
    public void Read_MinimalMachO64_NoSections_EmptyCollections()
    {
        var bytes = MachOTestData.ThinArm64();
        var image = MachOReader.Read(new ReadOnlyMemory<byte>(bytes), "test")!;
        image.Should().NotBeNull();
        image.Sections.Should().BeEmpty();
        image.Symbols.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // CheckUnsupportedFeatures
    // -------------------------------------------------------------------------

    [Fact]
    public void CheckUnsupportedFeatures_Thin64_ReturnsNull()
    {
        var result = MachOReader.CheckUnsupportedFeatures(MachOTestData.ThinArm64());
        result.Should().BeNull();
    }

    [Fact]
    public void CheckUnsupportedFeatures_Thin32_ReturnsError()
    {
        var result = MachOReader.CheckUnsupportedFeatures(MachOTestData.Thin32Bit());
        result.Should().NotBeNull();
        result!.Should().Contain("32-bit");
    }

    [Fact]
    public void CheckUnsupportedFeatures_ChainedFixups_ReturnsError()
    {
        var result = MachOReader.CheckUnsupportedFeatures(MachOTestData.ThinArm64WithChainedFixups());
        result.Should().NotBeNull();
        result!.Should().Contain("LC_DYLD_CHAINED_FIXUPS");
    }

    [Fact]
    public void CheckUnsupportedFeatures_LlvmBitcode_ReturnsError()
    {
        var result = MachOReader.CheckUnsupportedFeatures(MachOTestData.ThinArm64WithLlvmSegment());
        result.Should().NotBeNull();
        result!.Should().Contain("__LLVM");
    }

    [Fact]
    public void CheckUnsupportedFeatures_EmptyBytes_ReturnsNull()
    {
        MachOReader.CheckUnsupportedFeatures([]).Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // ParseFatSlice
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseFatSlice_FatWithBothArches_DefaultPrefersArm64()
    {
        var (fat, arm64Offset, arm64Size, _, _) = MachOTestData.FatBinary();
        var result = MachOReader.ParseFatSlice(fat);
        result.IsError.Should().BeFalse();
        result.Data.Offset.Should().Be((uint)arm64Offset);
        result.Data.Size.Should().Be((uint)arm64Size);
        result.Data.Arch.Should().Be(Architecture.Arm64);
    }

    [Fact]
    public void ParseFatSlice_FatWithBothArches_PreferX64()
    {
        var (fat, _, _, x64Offset, x64Size) = MachOTestData.FatBinary();
        var result = MachOReader.ParseFatSlice(fat, preferred: Architecture.X64);
        result.IsError.Should().BeFalse();
        result.Data.Offset.Should().Be((uint)x64Offset);
        result.Data.Arch.Should().Be(Architecture.X64);
    }

    [Fact]
    public void ParseFatSlice_TooSmall_ReturnsError()
    {
        var result = MachOReader.ParseFatSlice([0xCA, 0xFE]);
        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.MachoFeatureUnsupported);
    }

    [Fact]
    public void ParseFatSlice_ZeroSlices_ReturnsError()
    {
        var bytes = MachOTestData.FatHeaderOnly(nfatArch: 0);
        var result = MachOReader.ParseFatSlice(bytes);
        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.MachoFeatureUnsupported);
    }

    [Fact]
    public void ParseFatSlice_NoSupportedArch_ReturnsError()
    {
        // Build a fat binary that only has an unsupported CPU type (e.g. MIPS = 0x00000008)
        var bytes = MachOTestData.FatHeaderWithMips();
        var result = MachOReader.ParseFatSlice(bytes);
        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.MachoFeatureUnsupported);
        result.Error.Message.Should().Contain("no supported architecture");
    }

    [Fact]
    public void ParseFatSlice_FatArm64Only_FallsBackToArm64WhenPreferredNotFound()
    {
        // Fat with only arm64, requesting x64: should fall back to arm64
        var arm64Only = MachOTestData.FatArm64Only();
        var result = MachOReader.ParseFatSlice(arm64Only, preferred: Architecture.X64);
        // x64 not found -> falls back to arm64
        result.IsError.Should().BeFalse();
        result.Data.Arch.Should().Be(Architecture.Arm64);
    }

    [Fact]
    public void ParseFatSlice_OverflowingOffsetPlusSize_Rejected()
    {
        // Craft a fat_arch entry where offset + size overflows uint32 (e.g. 0xFFFFFFFC + 8 wraps to 4).
        // The parser must reject this slice rather than accepting it due to wrap-around.
        // fat_header(8) + fat_arch(20) + actual bytes(4)
        var bytes = MachOTestData.FatHeaderWithOverflowingOffset();
        var result = MachOReader.ParseFatSlice(bytes);
        // The only arch entry has an overflowing offset so no valid slice is found.
        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.MachoFeatureUnsupported);
    }

    // -------------------------------------------------------------------------
    // LooksLikeManagedNativeBuild
    // -------------------------------------------------------------------------

    [Fact]
    public void LooksLikeManagedNativeBuild_NoMarkers_ReturnsFalse()
    {
        var bytes = MachOTestData.ThinArm64();
        var image = MachOReader.Read(new ReadOnlyMemory<byte>(bytes), "test")!;
        MachOReader.LooksLikeManagedNativeBuild(image).Should().BeFalse();
    }

    [Fact]
    public void LooksLikeManagedNativeBuild_WithMarker_ReturnsTrue()
    {
        var bytes = MachOTestData.ThinArm64WithSymbol("_RhpNewFast", 0x1000);
        var image = MachOReader.Read(new ReadOnlyMemory<byte>(bytes), "test")!;
        MachOReader.LooksLikeManagedNativeBuild(image).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // ReadImportedFunctions — non-MachO image returns error
    // -------------------------------------------------------------------------

    [Fact]
    public void ReadImportedFunctions_NonMachOImage_ReturnsError()
    {
        var bytes = new byte[64];
        bytes[0] = 0x7F; bytes[1] = (byte)'E'; bytes[2] = (byte)'L'; bytes[3] = (byte)'F';
        bytes[4] = 2; bytes[5] = 1;
        var elfImage = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), "fake.elf");
        if (elfImage is null) return;
        var result = MachOReader.ReadImportedFunctions(elfImage);
        result.IsError.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // BuildId round-trip via LC_UUID
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildId_ExtractMachO_ReturnsLcUuid_WhenPresent()
    {
        var lcUuid = new byte[24];
        lcUuid[0] = 0x1B; lcUuid[1] = 0x00; lcUuid[2] = 0x00; lcUuid[3] = 0x00;
        lcUuid[4] = 24; lcUuid[5] = 0x00; lcUuid[6] = 0x00; lcUuid[7] = 0x00;
        for (var i = 0; i < 16; i++) lcUuid[8 + i] = (byte)(i + 1);

        var header = new byte[32 + 24];
        header[0] = 0xCF; header[1] = 0xFA; header[2] = 0xED; header[3] = 0xFE;
        header[4] = 0x0C; header[5] = 0x00; header[6] = 0x00; header[7] = 0x01;
        header[16] = 1;
        header[20] = 24;
        Array.Copy(lcUuid, 0, header, 32, 24);

        var buildId = BuildId.Extract(header, "test.dylib");
        buildId.Should().Be("0102030405060708090a0b0c0d0e0f10");
    }

    // -------------------------------------------------------------------------
    // End-to-end: fat binary parsed as NativeImage
    // -------------------------------------------------------------------------

    [Fact]
    public void FatBinary_RoundTrip_ParsesArm64SliceCorrectly()
    {
        // Build a fat binary where the arm64 slice has a NativeAOT marker symbol
        var (fat, _, _, _, _) = MachOTestData.FatBinaryWithMarkers();
        var sliceResult = MachOReader.ParseFatSlice(fat);
        sliceResult.IsError.Should().BeFalse();

        var (offset, size, arch) = sliceResult.Data;
        arch.Should().Be(Architecture.Arm64);

        var sliceMemory = new ReadOnlyMemory<byte>(fat, (int)offset, (int)size);
        var image = MachOReader.Read(sliceMemory, "test-fat.dylib");
        image.Should().NotBeNull();
        image!.Architecture.Should().Be(Architecture.Arm64);
        image.Format.Should().Be(BinaryFormat.MachO);
        MachOReader.LooksLikeManagedNativeBuild(image).Should().BeTrue();
    }

    [Fact]
    public void FatBinary_Sections_ParsedCorrectly()
    {
        var (fat, arm64Offset, arm64Size, _, _) = MachOTestData.FatBinaryWithTextSection();
        var sliceMemory = new ReadOnlyMemory<byte>(fat, arm64Offset, arm64Size);
        var image = MachOReader.Read(sliceMemory, "with-section.dylib")!;
        image.Should().NotBeNull();
        image.Sections.Should().NotBeEmpty();
        image.Sections[0].Name.Should().Contain("__TEXT");
    }
}

/// <summary>
/// Helpers that synthesize minimal valid Mach-O byte arrays for tests.
/// No Apple toolchain is required; all bytes are hand-crafted.
/// </summary>
internal static class MachOTestData
{
    private const int CpuTypeX86_64 = 0x01000007;
    private const int CpuTypeArm64 = 0x0100000C;

    // -------------------------------------------------------------------------
    // Thin binaries
    // -------------------------------------------------------------------------

    /// <summary>Minimal valid thin arm64 Mach-O 64-bit header with zero load commands.</summary>
    public static byte[] ThinArm64() => BuildThin64(CpuTypeArm64);

    /// <summary>Minimal valid thin x64 Mach-O 64-bit header with zero load commands.</summary>
    public static byte[] ThinX64() => BuildThin64(CpuTypeX86_64);

    /// <summary>
    /// Thin arm64 binary containing one <c>LC_UUID</c> load command carrying the given UUID bytes.
    /// </summary>
    public static byte[] ThinArm64WithUuid(byte[] uuid)
    {
        var lc = new byte[24];
        WriteLe32(lc, 0, 0x1B);   // LC_UUID
        WriteLe32(lc, 4, 24);
        Array.Copy(uuid, 0, lc, 8, Math.Min(uuid.Length, 16));

        return BuildThin64WithLoadCommands(CpuTypeArm64, lc);
    }

    /// <summary>Thin arm64 binary containing one <c>LC_SYMTAB</c> with the specified symbol.</summary>
    public static byte[] ThinArm64WithSymbol(string mangledName, ulong rva)
    {
        // String table: \0 + mangledName + \0
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(mangledName);
        var strTable = new byte[1 + nameBytes.Length + 1];
        Array.Copy(nameBytes, 0, strTable, 1, nameBytes.Length);

        // nlist_64: n_strx(4)+n_type(1)+n_sect(1)+n_desc(2)+n_value(8) = 16 bytes
        // n_type=0x0F (N_SECT|N_EXT), n_sect=0 (no section reference)
        var symEntry = new byte[16];
        WriteLe32(symEntry, 0, 1);    // n_strx = 1 (skip leading \0)
        symEntry[4] = 0x0F;           // N_SECT | N_EXT (defined, external)
        symEntry[5] = 0;              // n_sect
        WriteLe64(symEntry, 8, rva);

        // The LC_SYMTAB command itself and the symbol/string data must be located
        // after the header and load command region. We compute offsets accordingly.
        // Layout: header(32) + LC_SYMTAB(24) + [padding to 64] + symEntry(16) + strTable
        const int headerSize = 32;
        const int lcSymtabSize = 24;
        const int symOff = headerSize + lcSymtabSize;
        var strOff = symOff + symEntry.Length;
        var totalSize = strOff + strTable.Length;

        var file = new byte[totalSize];

        // mach_header_64
        WriteLe32(file, 0, 0xFEEDFACF);          // magic
        WriteLe32(file, 4, (uint)CpuTypeArm64);   // cputype
        WriteLe32(file, 8, 0);                    // cpusubtype
        WriteLe32(file, 12, 2);                   // filetype=MH_EXECUTE
        WriteLe32(file, 16, 1);                   // ncmds=1
        WriteLe32(file, 20, lcSymtabSize);         // sizeofcmds
        WriteLe32(file, 24, 0);                   // flags
        WriteLe32(file, 28, 0);                   // reserved

        // LC_SYMTAB at offset 32
        WriteLe32(file, 32, 0x2);                 // LC_SYMTAB
        WriteLe32(file, 36, lcSymtabSize);
        WriteLe32(file, 40, (uint)symOff);         // symoff
        WriteLe32(file, 44, 1);                   // nsyms=1
        WriteLe32(file, 48, (uint)strOff);         // stroff
        WriteLe32(file, 52, (uint)strTable.Length);// strsize

        Array.Copy(symEntry, 0, file, symOff, symEntry.Length);
        Array.Copy(strTable, 0, file, strOff, strTable.Length);
        return file;
    }

    /// <summary>32-bit thin Mach-O header (MH_MAGIC, 0xFEEDFACE).</summary>
    public static byte[] Thin32Bit()
    {
        var bytes = new byte[28 + 8];
        WriteLe32(bytes, 0, 0xFEEDFACE);           // 32-bit magic
        WriteLe32(bytes, 4, 0x0000000C);            // CPU_TYPE_ARM (bare, without ABI64 bit)
        return bytes;
    }

    /// <summary>Thin arm64 binary with <c>LC_DYLD_CHAINED_FIXUPS</c> present.</summary>
    public static byte[] ThinArm64WithChainedFixups()
    {
        var lc = new byte[8];
        WriteLe32(lc, 0, 0x80000034); // LC_DYLD_CHAINED_FIXUPS
        WriteLe32(lc, 4, 8);
        return BuildThin64WithLoadCommands(CpuTypeArm64, lc);
    }

    /// <summary>Thin arm64 binary with an <c>__LLVM</c> <c>LC_SEGMENT_64</c>.</summary>
    public static byte[] ThinArm64WithLlvmSegment()
    {
        // LC_SEGMENT_64: cmd(4)+cmdsize(4)+segname(16)+rest(44) = 72 bytes minimum (0 sections)
        var lc = new byte[72];
        WriteLe32(lc, 0, 0x19);  // LC_SEGMENT_64
        WriteLe32(lc, 4, 72);
        WriteAscii(lc, 8, "__LLVM");
        return BuildThin64WithLoadCommands(CpuTypeArm64, lc);
    }

    // -------------------------------------------------------------------------
    // Fat binaries
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fat binary containing both arm64 and x64 slices (each a minimal thin header).
    /// Returns (fatBytes, arm64Offset, arm64Size, x64Offset, x64Size).
    /// </summary>
    public static (byte[] Fat, int Arm64Offset, int Arm64Size, int X64Offset, int X64Size) FatBinary()
    {
        var arm64Slice = ThinArm64();
        var x64Slice = ThinX64();
        return BuildFat(arm64Slice, CpuTypeArm64, x64Slice, CpuTypeX86_64);
    }

    /// <summary>Fat binary with both slices carrying NativeAOT marker symbols.</summary>
    public static (byte[] Fat, int Arm64Offset, int Arm64Size, int X64Offset, int X64Size) FatBinaryWithMarkers()
    {
        var arm64Slice = ThinArm64WithSymbol("_RhpNewFast", 0x1000);
        var x64Slice = ThinArm64WithSymbol("_RhpNewFast", 0x2000); // reuse arm64 builder; cpu overridden via fat header
        return BuildFat(arm64Slice, CpuTypeArm64, x64Slice, CpuTypeX86_64);
    }

    /// <summary>Fat binary with arm64 slice that has a __TEXT,__text section.</summary>
    public static (byte[] Fat, int Arm64Offset, int Arm64Size, int X64Offset, int X64Size) FatBinaryWithTextSection()
    {
        var arm64Slice = ThinArm64WithTextSection();
        var x64Slice = ThinX64();
        return BuildFat(arm64Slice, CpuTypeArm64, x64Slice, CpuTypeX86_64);
    }

    /// <summary>Fat binary containing only arm64.</summary>
    public static byte[] FatArm64Only()
    {
        var arm64Slice = ThinArm64();
        var (fat, _, _, _, _) = BuildFat(arm64Slice, CpuTypeArm64, null, 0);
        return fat;
    }

    /// <summary>Fat header with <paramref name="nfatArch"/> slices (no actual slice data).</summary>
    public static byte[] FatHeaderOnly(uint nfatArch)
    {
        var bytes = new byte[8];
        // Write FAT_MAGIC in big-endian = CA FE BA BE
        bytes[0] = 0xCA; bytes[1] = 0xFE; bytes[2] = 0xBA; bytes[3] = 0xBE;
        WriteBe32(bytes, 4, nfatArch);
        return bytes;
    }

    /// <summary>Fat binary with only a MIPS slice (CPU_TYPE = 0x00000008), which is unsupported.</summary>
    public static byte[] FatHeaderWithMips()
    {
        // fat_header(8) + one fat_arch(20) + minimal slice(4)
        const int sliceOffset = 8 + 20;
        const int sliceSize = 4;
        var bytes = new byte[sliceOffset + sliceSize];
        bytes[0] = 0xCA; bytes[1] = 0xFE; bytes[2] = 0xBA; bytes[3] = 0xBE; // FAT_MAGIC BE
        WriteBe32(bytes, 4, 1);                      // nfat_arch=1
        WriteBe32(bytes, 8, 0x00000008);             // cputype = CPU_TYPE_MIPS
        WriteBe32(bytes, 12, 0);                     // cpusubtype
        WriteBe32(bytes, 16, sliceOffset);           // offset
        WriteBe32(bytes, 20, sliceSize);             // size
        WriteBe32(bytes, 24, 0);                     // align
        return bytes;
    }

    /// <summary>
    /// Fat binary whose single slice has <c>offset = 0xFFFFFFFC</c> and <c>size = 8</c>,
    /// so <c>offset + size</c> wraps around to 4 under uint32 arithmetic. A correct
    /// parser must use 64-bit arithmetic and reject this entry.
    /// </summary>
    public static byte[] FatHeaderWithOverflowingOffset()
    {
        // fat_header(8) + fat_arch(20) + 4 bytes of actual file content
        var bytes = new byte[8 + 20 + 4];
        bytes[0] = 0xCA; bytes[1] = 0xFE; bytes[2] = 0xBA; bytes[3] = 0xBE; // FAT_MAGIC
        WriteBe32(bytes, 4, 1);                         // nfat_arch=1
        WriteBe32(bytes, 8, (uint)CpuTypeArm64);        // cputype
        WriteBe32(bytes, 12, 0);                        // cpusubtype
        WriteBe32(bytes, 16, 0xFFFFFFFC);               // offset = near uint.MaxValue
        WriteBe32(bytes, 20, 8);                        // size = 8; offset+size wraps to 4 in uint32
        WriteBe32(bytes, 24, 0);                        // align
        return bytes;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static byte[] BuildThin64(int cpuType)
    {
        var bytes = new byte[32 + 8]; // header + one minimal load command slot (not a real LC)
        WriteLe32(bytes, 0, 0xFEEDFACF);
        WriteLe32(bytes, 4, (uint)cpuType);
        WriteLe32(bytes, 8, 0);   // cpusubtype
        WriteLe32(bytes, 12, 2);  // filetype=MH_EXECUTE
        WriteLe32(bytes, 16, 0);  // ncmds=0
        WriteLe32(bytes, 20, 0);  // sizeofcmds=0
        WriteLe32(bytes, 24, 0);  // flags
        WriteLe32(bytes, 28, 0);  // reserved
        return bytes;
    }

    private static byte[] BuildThin64WithLoadCommands(int cpuType, byte[] loadCommandBytes)
    {
        const int headerSize = 32;
        var file = new byte[headerSize + loadCommandBytes.Length];
        WriteLe32(file, 0, 0xFEEDFACF);
        WriteLe32(file, 4, (uint)cpuType);
        WriteLe32(file, 8, 0);
        WriteLe32(file, 12, 2);  // MH_EXECUTE
        WriteLe32(file, 16, 1);  // ncmds = 1
        WriteLe32(file, 20, (uint)loadCommandBytes.Length);
        WriteLe32(file, 24, 0);
        WriteLe32(file, 28, 0);  // reserved
        Array.Copy(loadCommandBytes, 0, file, headerSize, loadCommandBytes.Length);
        return file;
    }

    private static byte[] ThinArm64WithTextSection()
    {
        // segment_command_64: cmd(4)+cmdsize(4)+segname(16)+vmaddr(8)+vmsize(8)+fileoff(8)+filesize(8)+maxprot(4)+initprot(4)+nsects(4)+flags(4) = 72
        // section_64: sectname(16)+segname(16)+addr(8)+size(8)+offset(4)+align(4)+reloff(4)+nreloc(4)+flags(4)+res1(4)+res2(4)+res3(4) = 80
        const int lcSize = 72 + 80; // one section
        var lc = new byte[lcSize];
        WriteLe32(lc, 0, 0x19);        // LC_SEGMENT_64
        WriteLe32(lc, 4, lcSize);
        WriteAscii(lc, 8, "__TEXT");    // segname at offset 8
        WriteLe64(lc, 24, 0x100000000UL); // vmaddr
        WriteLe64(lc, 32, 0x1000);     // vmsize
        WriteLe64(lc, 40, 0);          // fileoff
        WriteLe64(lc, 48, 0x1000);     // filesize
        WriteLe32(lc, 56, 7);          // maxprot
        WriteLe32(lc, 60, 5);          // initprot
        WriteLe32(lc, 64, 1);          // nsects=1
        WriteLe32(lc, 68, 0);          // flags

        // section_64 at offset 72
        WriteAscii(lc, 72 + 0, "__text");
        WriteAscii(lc, 72 + 16, "__TEXT");
        WriteLe64(lc, 72 + 32, 0x100000000UL); // addr
        WriteLe64(lc, 72 + 40, 0x100);         // size
        WriteLe32(lc, 72 + 48, 0);             // offset

        return BuildThin64WithLoadCommands(CpuTypeArm64, lc);
    }

    private static (byte[] Fat, int Arm64Offset, int Arm64Size, int X64Offset, int X64Size) BuildFat(
        byte[] slice1, int cpuType1, byte[]? slice2, int cpuType2)
    {
        // Layout: fat_header(8) + fat_arch_1(20) [+ fat_arch_2(20)] + slice1 [+ slice2]
        var nSlices = slice2 is not null ? 2 : 1;
        var tableSize = 8 + nSlices * 20;
        const int alignment = 4096;

        // Align slice1 to 4096
        var slice1Offset = (tableSize + alignment - 1) & ~(alignment - 1);
        var slice2Offset = 0;
        if (slice2 is not null)
        {
            var afterSlice1 = slice1Offset + slice1.Length;
            slice2Offset = (afterSlice1 + alignment - 1) & ~(alignment - 1);
        }

        var totalSize = slice2 is not null
            ? slice2Offset + slice2.Length
            : slice1Offset + slice1.Length;

        var fat = new byte[totalSize];

        // fat_header (big-endian)
        fat[0] = 0xCA; fat[1] = 0xFE; fat[2] = 0xBA; fat[3] = 0xBE;
        WriteBe32(fat, 4, (uint)nSlices);

        // fat_arch for slice1
        WriteBe32(fat, 8, (uint)cpuType1);
        WriteBe32(fat, 12, 0);                    // cpusubtype
        WriteBe32(fat, 16, (uint)slice1Offset);
        WriteBe32(fat, 20, (uint)slice1.Length);
        WriteBe32(fat, 24, 12);                   // align = 2^12 = 4096

        if (slice2 is not null)
        {
            WriteBe32(fat, 28, (uint)cpuType2);
            WriteBe32(fat, 32, 0);
            WriteBe32(fat, 36, (uint)slice2Offset);
            WriteBe32(fat, 40, (uint)slice2.Length);
            WriteBe32(fat, 44, 12);
        }

        Array.Copy(slice1, 0, fat, slice1Offset, slice1.Length);
        if (slice2 is not null)
            Array.Copy(slice2, 0, fat, slice2Offset, slice2.Length);

        return (fat, slice1Offset, slice1.Length, slice2Offset, slice2?.Length ?? 0);
    }

    private static void WriteLe32(byte[] buf, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset), value);

    private static void WriteLe64(byte[] buf, int offset, ulong value)
        => BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(offset), value);

    private static void WriteBe32(byte[] buf, int offset, uint value)
        => BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(offset), value);

    private static void WriteAscii(byte[] buf, int offset, string s)
    {
        var encoded = System.Text.Encoding.ASCII.GetBytes(s);
        Array.Copy(encoded, 0, buf, offset, Math.Min(encoded.Length, buf.Length - offset));
    }
}
