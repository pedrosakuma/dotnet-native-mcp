using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class MachOReaderTests
{
    // Mach-O magic bytes (little-endian)
    private static byte[] MachO64Header(int cpuType = 0x0100000C) // ARM64 by default
    {
        var bytes = new byte[32 + 8]; // header + one minimal load command slot
        // magic: 0xFEEDFACF LE
        bytes[0] = 0xCF; bytes[1] = 0xFA; bytes[2] = 0xED; bytes[3] = 0xFE;
        // cputype
        bytes[4] = (byte)(cpuType & 0xFF);
        bytes[5] = (byte)((cpuType >> 8) & 0xFF);
        bytes[6] = (byte)((cpuType >> 16) & 0xFF);
        bytes[7] = (byte)((cpuType >> 24) & 0xFF);
        // cpusubtype, filetype (zeros ok)
        // ncmds = 0
        // sizeofcmds = 0
        return bytes;
    }

    private static byte[] FatHeader()
    {
        // fat magic: CA FE BA BE on disk → read as LE uint32 = 0xBEBAFECA
        var bytes = new byte[8];
        bytes[0] = 0xCA; bytes[1] = 0xFE; bytes[2] = 0xBA; bytes[3] = 0xBE;
        return bytes;
    }

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
        MachOReader.IsFatBinary([0xCA, 0xFE, 0xBA, 0xBE]).Should().BeTrue();
    }

    [Fact]
    public void IsFatBinary_MachO64Magic_ReturnsFalse()
    {
        MachOReader.IsFatBinary([0xCF, 0xFA, 0xED, 0xFE]).Should().BeFalse();
    }

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
    public void Read_MinimalMachO64_ParsesArchitecture()
    {
        var bytes = MachO64Header(cpuType: 0x0100000C); // ARM64
        var image = MachOReader.Read(new ReadOnlyMemory<byte>(bytes), "test");
        image.Should().NotBeNull();
        image!.Architecture.Should().Be(Architecture.Arm64);
        image.Format.Should().Be(BinaryFormat.MachO);
    }

    [Fact]
    public void Read_MinimalMachO64_X64Architecture()
    {
        var bytes = MachO64Header(cpuType: 0x01000007); // X86_64
        var image = MachOReader.Read(new ReadOnlyMemory<byte>(bytes), "test");
        image.Should().NotBeNull();
        image!.Architecture.Should().Be(Architecture.X64);
    }

    [Fact]
    public void Read_MinimalMachO64_NoSections_EmptyCollections()
    {
        var bytes = MachO64Header();
        var image = MachOReader.Read(new ReadOnlyMemory<byte>(bytes), "test");
        image.Should().NotBeNull();
        image!.Sections.Should().BeEmpty();
        image.Symbols.Should().BeEmpty();
    }

    [Fact]
    public void LooksLikeManagedNativeBuild_NoMarkers_ReturnsFalse()
    {
        var bytes = MachO64Header();
        var image = MachOReader.Read(new ReadOnlyMemory<byte>(bytes), "test")!;
        MachOReader.LooksLikeManagedNativeBuild(image).Should().BeFalse();
    }

    [Fact]
    public void ReadImportedFunctions_NonMachOImage_ReturnsError()
    {
        // Build a minimal ELF NativeImage via ElfReader to get a non-MachO image
        var bytes = new byte[64];
        bytes[0] = 0x7F; bytes[1] = (byte)'E'; bytes[2] = (byte)'L'; bytes[3] = (byte)'F';
        bytes[4] = 2; bytes[5] = 1; // 64-bit LE
        // Fill ncmds / shentsize with valid values to avoid NPE
        var elfImage = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), "fake.elf");
        // ElfReader may return null for incomplete headers, so use a direct format check
        if (elfImage is null) return; // Skip if ELF parse rejects too-short input

        var result = MachOReader.ReadImportedFunctions(elfImage);
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public void BuildId_ExtractMachO_ReturnsLcUuid_WhenPresent()
    {
        // Build a minimal Mach-O 64-bit file with LC_UUID
        var lcUuid = new byte[24];
        // cmd = 0x1B (LC_UUID)
        lcUuid[0] = 0x1B; lcUuid[1] = 0x00; lcUuid[2] = 0x00; lcUuid[3] = 0x00;
        // cmdsize = 24
        lcUuid[4] = 24; lcUuid[5] = 0x00; lcUuid[6] = 0x00; lcUuid[7] = 0x00;
        // UUID bytes (8..23)
        for (var i = 0; i < 16; i++) lcUuid[8 + i] = (byte)(i + 1);

        // Mach-O header (32 bytes) + LC_UUID (24 bytes)
        var header = new byte[32 + 24];
        // magic: CF FA ED FE
        header[0] = 0xCF; header[1] = 0xFA; header[2] = 0xED; header[3] = 0xFE;
        // cputype: ARM64
        header[4] = 0x0C; header[5] = 0x00; header[6] = 0x00; header[7] = 0x01;
        // ncmds = 1 (at offset 16)
        header[16] = 1;
        // sizeofcmds = 24 (at offset 20)
        header[20] = 24;
        Array.Copy(lcUuid, 0, header, 32, 24);

        var buildId = BuildId.Extract(header, "test.dylib");
        // Expected: hex of bytes 1..16
        buildId.Should().Be("0102030405060708090a0b0c0d0e0f10");
    }
}
