using System.Buffers.Binary;
using DotnetNativeMcp.Core.Disassembly;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Regression tests for issue #97 — bounds-check / integer-overflow hardening in
/// native binary parsers and raw-bytes disassembly. Each scenario was reachable
/// (unhandled exception or wrong slice) on the pre-fix code.
/// </summary>
public class BoundsHardeningTests
{
    // -------------------------------------------------------------------------
    // Mach-O LC_SYMTAB: uint overflow on symoff + nsyms * entrySize
    // -------------------------------------------------------------------------

    [Fact]
    public void MachOReader_SymtabWithOverflowingNsyms_DoesNotThrow()
    {
        // Build a thin arm64 Mach-O with an LC_SYMTAB whose (symoff + nsyms*16)
        // wraps the 32-bit space (pre-fix the check used uint arithmetic and
        // accepted the load command, then cast to int → negative index).
        var file = BuildThinArm64WithSymtab(
            symoff: 0xFFFF_FFF0u,
            nsyms: 0x1000_0000u, // nsyms * 16 = 0x10_0000_0000 → wraps uint
            stroff: 64,
            strsize: 4);

        Action act = () => MachOReader.Read(new ReadOnlyMemory<byte>(file), "/tmp/test.macho");

        act.Should().NotThrow();
    }

    [Fact]
    public void MachOReader_SymtabWithOversizedStroff_DoesNotThrow()
    {
        // stroff just above int.MaxValue would cast to a negative value when
        // slicing (int)stroff..(int)(stroff + strsize) prior to the fix.
        var file = BuildThinArm64WithSymtab(
            symoff: 64,
            nsyms: 0,
            stroff: 0x8000_0000u,
            strsize: 0x10);

        Action act = () => MachOReader.Read(new ReadOnlyMemory<byte>(file), "/tmp/test.macho");

        act.Should().NotThrow();
    }

    // -------------------------------------------------------------------------
    // ELF section header bounds
    // -------------------------------------------------------------------------

    [Fact]
    public void NativeImage_GetSectionBytes_OffsetBeyondInt_ReturnsEmpty()
    {
        // Section with FileOffset = 5 GiB into a 1-byte buffer must not throw.
        var image = BuildImageWithSection(fileOffset: 5UL * 1024 * 1024 * 1024, fileSize: 1024);

        image.GetSectionBytes(image.Sections[0]).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void NativeImage_GetSectionBytes_OffsetBeyondFile_ReturnsEmpty()
    {
        // Section with FileOffset just past the end of the buffer.
        var image = BuildImageWithSection(fileOffset: 4096, fileSize: 1024, rawLength: 2048);

        image.GetSectionBytes(image.Sections[0]).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void NativeImage_RvaToFileOffset_OffsetBeyondInt_ReturnsNull()
    {
        var image = BuildImageWithSection(
            fileOffset: 0x7FFF_FFF0,
            fileSize: 0x100,
            virtualAddress: 0x1000,
            virtualSize: 0x100,
            rawLength: 0x2000);

        // rva = 0x1010 → fileOffset = 0x7FFF_FFF0 + 0x10 = 0x8000_0000 (> int.MaxValue).
        image.RvaToFileOffset(0x1010).Should().BeNull();
    }

    [Fact]
    public void NativeImage_RvaToFileOffset_AdditionWouldWrap_ReturnsNull()
    {
        // Section with VirtualSize spanning all of u64 plus a nonzero FileOffset:
        // a far-out RVA could wrap (rva - VA) + FileOffset into a small value
        // that would otherwise pass the bounds check.
        var section = new NativeSection(
            Name: ".text",
            VirtualAddress: 0,
            VirtualSize: ulong.MaxValue,
            FileOffset: 2,
            FileSize: 1);
        var image = new NativeImage(
            handle: ImageHandle.From("test", "test.bin"),
            filePath: "/tmp/test.bin",
            format: BinaryFormat.Pe,
            architecture: Architecture.X64,
            sections: new[] { section },
            symbols: Array.Empty<NativeSymbol>(),
            rawBytes: new ReadOnlyMemory<byte>(new byte[4]),
            imageBase: 0);

        image.RvaToFileOffset(ulong.MaxValue - 1).Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // ELF symtab bounds
    // -------------------------------------------------------------------------

    [Fact]
    public void ElfReader_SymtabWithOverflowingOffset_DoesNotThrow()
    {
        // Smallest possible ELF64 with one SHT_SYMTAB section whose sh_offset is
        // just under ulong.MaxValue and sh_size is small: pre-fix the addition
        // wrapped and the slice cast to int produced a negative index.
        var file = BuildElf64WithSymtab(symOff: ulong.MaxValue - 1, symSize: 4);

        Action act = () => ElfReader.Read(new ReadOnlyMemory<byte>(file), "/tmp/test.elf");

        act.Should().NotThrow();
    }

    private static byte[] BuildElf64WithSymtab(ulong symOff, ulong symSize)
    {
        // Synthesize a tiny valid-ish ELF64 header pointing at one section header.
        // The section header table sits after the 64-byte ELF header; one section
        // header (SHT_SYMTAB) occupies the next 64 bytes.
        const int ehSize = 64;
        const int shEntSize = 64;
        const int shNum = 2; // index 0 = SHT_NULL (required by ELF), index 1 = SHT_SYMTAB
        var file = new byte[ehSize + shEntSize * shNum];

        // ELF magic + class=64 + data=LE
        file[0] = 0x7F;
        file[1] = (byte)'E';
        file[2] = (byte)'L';
        file[3] = (byte)'F';
        file[4] = 2;   // EI_CLASS = ELFCLASS64
        file[5] = 1;   // EI_DATA  = ELFDATA2LSB
        file[6] = 1;   // EI_VERSION

        // e_type=2 ET_EXEC, e_machine=62 EM_X86_64, e_version=1
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(16), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(18), 62);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(20), 1);

        // e_shoff = 64, e_shentsize = 64, e_shnum = 2, e_shstrndx = 0
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(40), ehSize);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(58), shEntSize);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(60), shNum);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(62), 0);

        // Section header 0 left zero (SHT_NULL).

        // Section header 1: SHT_SYMTAB with attacker-controlled offset/size.
        var sh1 = file.AsSpan(ehSize + shEntSize);
        BinaryPrimitives.WriteUInt32LittleEndian(sh1[0..], 0);   // sh_name = 0
        BinaryPrimitives.WriteUInt32LittleEndian(sh1[4..], 2);   // sh_type = SHT_SYMTAB
        BinaryPrimitives.WriteUInt64LittleEndian(sh1[24..], symOff);
        BinaryPrimitives.WriteUInt64LittleEndian(sh1[32..], symSize);
        BinaryPrimitives.WriteUInt32LittleEndian(sh1[40..], 0);  // sh_link = 0 (no strtab)

        return file;
    }

    [Fact]
    public void ElfReader_ShoffNearMaxValue_DoesNotThrow()
    {
        // e_shoff near ulong.MaxValue: pre-fix, ReadSectionHeader computed
        // start = shOff + index*entSize and the +64 size check wrapped, then
        // cast to int yielded a negative slice index.
        var file = BuildElf64HeaderOnly(eShoff: ulong.MaxValue - 32, eShnum: 1, ePhoff: 0, ePhnum: 0);

        Action act = () => ElfReader.Read(new ReadOnlyMemory<byte>(file), "/tmp/test.elf");

        act.Should().NotThrow();
    }

    [Fact]
    public void ElfReader_PhoffNearMaxValue_DoesNotThrow()
    {
        // e_phoff near ulong.MaxValue: pre-fix, ComputeImageBase computed
        // hdrStart + 56 and wrapped past the bounds check.
        var file = BuildElf64HeaderOnly(eShoff: 0, eShnum: 0, ePhoff: ulong.MaxValue - 32, ePhnum: 1);

        Action act = () => ElfReader.Read(new ReadOnlyMemory<byte>(file), "/tmp/test.elf");

        act.Should().NotThrow();
    }

    private static byte[] BuildElf64HeaderOnly(ulong eShoff, ushort eShnum, ulong ePhoff, ushort ePhnum)
    {
        var file = new byte[64];
        file[0] = 0x7F; file[1] = (byte)'E'; file[2] = (byte)'L'; file[3] = (byte)'F';
        file[4] = 2; file[5] = 1; file[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(16), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(18), 62);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(32), ePhoff);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(40), eShoff);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(54), 56);     // e_phentsize
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(56), ePhnum);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(58), 64);     // e_shentsize
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(60), eShnum);
        return file;
    }

    // -------------------------------------------------------------------------
    // RawDisassembler raw-bytes mode: negative rva / overflowing offset+size
    // -------------------------------------------------------------------------

    [Fact]
    public void RawDisassembler_NegativeRva_ReturnsError()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, new byte[256]);
            var result = RawDisassembler.Disassemble(tmp, rva: -1, size: 16, arch: Architecture.X64, baseAddress: null, maxInstructions: 4);

            result.IsError.Should().BeTrue();
            result.Error!.Kind.Should().Be(ErrorKinds.AddressOutOfRange);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void RawDisassembler_NonPositiveSize_ReturnsError()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, new byte[256]);
            var result = RawDisassembler.Disassemble(tmp, rva: 0, size: 0, arch: Architecture.X64, baseAddress: null, maxInstructions: 4);

            result.IsError.Should().BeTrue();
            result.Error!.Kind.Should().Be(ErrorKinds.AddressOutOfRange);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void RawDisassembler_OffsetPlusSizeOverflowsInt_ReturnsError()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, new byte[256]);
            // fileOffset = rva = int.MaxValue - 8; size = 32. Pre-fix the int
            // addition wrapped to a small negative number that passed the
            // "fileOffset + size > rawBytes.Length" check.
            var result = RawDisassembler.Disassemble(
                tmp,
                rva: int.MaxValue - 8,
                size: 32,
                arch: Architecture.X64,
                baseAddress: null,
                maxInstructions: 4);

            result.IsError.Should().BeTrue();
            result.Error!.Kind.Should().Be(ErrorKinds.AddressOutOfRange);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // -------------------------------------------------------------------------
    // helpers
    // -------------------------------------------------------------------------

    private static byte[] BuildThinArm64WithSymtab(uint symoff, uint nsyms, uint stroff, uint strsize)
    {
        const int headerSize = 32;
        const int lcSymtabSize = 24;
        var file = new byte[headerSize + lcSymtabSize + 64];

        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0), 0xFEEDFACF);  // magic
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), 0x0100000C);  // cputype = arm64
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12), 2);          // MH_EXECUTE
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(16), 1);          // ncmds = 1
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(20), lcSymtabSize);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(24), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(28), 0);

        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(32), 0x2);        // LC_SYMTAB
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(36), lcSymtabSize);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(40), symoff);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(44), nsyms);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(48), stroff);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(52), strsize);

        return file;
    }

    private static NativeImage BuildImageWithSection(
        ulong fileOffset,
        ulong fileSize,
        ulong virtualAddress = 0x1000,
        ulong virtualSize = 0,
        int rawLength = 4096)
    {
        var section = new NativeSection(
            Name: ".text",
            VirtualAddress: virtualAddress,
            VirtualSize: virtualSize == 0 ? fileSize : virtualSize,
            FileOffset: fileOffset,
            FileSize: fileSize);

        return new NativeImage(
            handle: ImageHandle.From("test", "test.bin"),
            filePath: "/tmp/test.bin",
            format: BinaryFormat.Pe,
            architecture: Architecture.X64,
            sections: new[] { section },
            symbols: Array.Empty<NativeSymbol>(),
            rawBytes: new ReadOnlyMemory<byte>(new byte[rawLength]),
            imageBase: 0);
    }
}
