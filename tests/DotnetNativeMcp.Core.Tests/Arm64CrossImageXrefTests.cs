using System.Buffers.Binary;
using System.Text;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Xref;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Cross-image xref tests for AArch64 ELF callers. The AArch64 PLT0 resolver stub is 32 bytes
/// (vs 16 on x86-64), and PLTn function entries are 16 bytes by default but 24 bytes with BTI/PAC
/// hardening. <see cref="ElfReader.ResolvePltEntries"/> must account for both, otherwise every PLT
/// entry VA is offset and cross-image matches silently vanish.
/// </summary>
public sealed class Arm64CrossImageXrefTests
{
    private const ulong PltVa = 0x1000UL;
    private const ulong TextVa = 0x2000UL;
    private const int Aarch64PltHeaderSize = 32;

    [Fact]
    public void ResolvePltEntries_Aarch64_SkipsThirtyTwoBytePlt0Header()
    {
        var elfBytes = BuildAarch64CallerElf(pltStride: 16, ["lib_func"]);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(elfBytes), "arm64caller.so");
        image.Should().NotBeNull("AArch64 ELF parsing must succeed");
        image!.Architecture.Should().Be(Architecture.Arm64);

        var plt = ElfReader.ResolvePltEntries(image);

        var expected = PltVa + Aarch64PltHeaderSize;
        plt.Should().ContainKey(expected,
            "the first PLT entry follows the 32-byte AArch64 PLT0 header");
        plt[expected].Should().Be("lib_func");
        plt.Should().NotContainKey(PltVa + 16,
            "a 16-byte skip would be the x86-64 layout, not AArch64");
    }

    [Fact]
    public void ResolvePltEntries_Aarch64BtiPac_UsesTwentyFourByteStride()
    {
        // BTI/PAC-hardened AArch64: PLT0 = 32 bytes, each PLTn = 24 bytes.
        var elfBytes = BuildAarch64CallerElf(pltStride: 24, ["lib_func", "lib_func2"]);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(elfBytes), "arm64bti.so");
        image.Should().NotBeNull();

        var plt = ElfReader.ResolvePltEntries(image!);

        var entry0 = PltVa + Aarch64PltHeaderSize;        // 0x1020
        var entry1 = PltVa + Aarch64PltHeaderSize + 24;   // 0x1038
        plt.Should().ContainKey(entry0);
        plt[entry0].Should().Be("lib_func");
        plt.Should().ContainKey(entry1, "the second entry sits 24 bytes after the first under BTI/PAC");
        plt[entry1].Should().Be("lib_func2");
        plt.Should().NotContainKey(PltVa + Aarch64PltHeaderSize + 16,
            "a 16-byte stride would mis-locate the second entry");
    }

    [Fact]
    public void FindCallers_Aarch64CallerElf_ResolvesBlToPltEntry()
    {
        var elfBytes = BuildAarch64CallerElf(pltStride: 16, ["lib_func"]);
        var callerImage = ElfReader.Read(new ReadOnlyMemory<byte>(elfBytes), "arm64caller.so");
        callerImage.Should().NotBeNull();

        var callerGraph = NativeCallGraphBuilder.Build(callerImage!);
        var entryVa = PltVa + Aarch64PltHeaderSize;
        callerGraph.Should().ContainKey(entryVa, "the BL in .text targets the function's PLT entry");

        var results = CrossImageCallGraphScanner.FindCallers(
            callerImage!, callerGraph, "lib_func", null);

        results.Should().ContainSingle("exactly one BL targets lib_func's PLT entry");
        results[0].SourceAddressHex.Should().StartWith("0000000000002000",
            "the BL instruction is at VA 0x2000");
        results[0].Mnemonic.Should().Be("bl");
        results[0].CallerImagePath.Should().Be("arm64caller.so");
    }

    [Fact]
    public void ResolvePltEntries_Aarch64BtiPacWithTlsdesc_IgnoresTlsdescWhenDerivingStride()
    {
        // binutils can append lazy TLSDESC relocations to .rela.plt and a separate 32-byte
        // TLSDESC stub to .plt. That inflates the raw .rela.plt count, but only JUMP_SLOT
        // relocations occupy regular PLTn slots — so stride derivation must ignore the TLSDESC
        // entry. Layout here: PLT0 (32) + 2 BTI/PAC entries (24 each) + 1 TLSDESC stub (32) = 112.
        var elfBytes = BuildAarch64CallerElf(pltStride: 24, ["lib_func", "lib_func2"], tlsdescStubCount: 1);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(elfBytes), "arm64tlsdesc.so");
        image.Should().NotBeNull();

        var plt = ElfReader.ResolvePltEntries(image!);

        var entry0 = PltVa + Aarch64PltHeaderSize;        // 0x1020
        var entry1 = PltVa + Aarch64PltHeaderSize + 24;   // 0x1038
        plt.Should().ContainKey(entry0);
        plt[entry0].Should().Be("lib_func");
        plt.Should().ContainKey(entry1, "the TLSDESC reloc must not collapse the stride back to 16");
        plt[entry1].Should().Be("lib_func2");
        plt.Should().NotContainKey(PltVa + Aarch64PltHeaderSize + 16,
            "a 16-byte stride would mis-locate the second BTI/PAC entry");
    }

    [Fact]
    public void ResolvePltEntries_Aarch64Default16WithTlsdesc_DoesNotMisclassifyAsBtiPac()
    {
        // Regression for the ambiguous-fit bug: default 16-byte PLTn entries plus a trailing
        // 32-byte TLSDESC stub. Layout: PLT0 (32) + 4 * 16 + 32 = 128. A naive "smallest leftover"
        // heuristic would pick the 24-byte stride (128 - 32 - 4*24 == 0) and shift every entry past
        // the first. Subtracting the TLSDESC stub bytes first makes 16 the only whole-entry fit.
        var elfBytes = BuildAarch64CallerElf(
            pltStride: 16, ["fn0", "fn1", "fn2", "fn3"], tlsdescStubCount: 1);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(elfBytes), "arm64default-tlsdesc.so");
        image.Should().NotBeNull();

        var plt = ElfReader.ResolvePltEntries(image!);

        for (var i = 0; i < 4; i++)
        {
            var expected = PltVa + Aarch64PltHeaderSize + (ulong)(i * 16);
            plt.Should().ContainKey(expected, $"entry {i} sits at a 16-byte stride");
            plt[expected].Should().Be($"fn{i}");
        }
    }

    /// <summary>
    /// Encodes an AArch64 <c>BL</c> (branch-with-link) from <paramref name="pc"/> to
    /// <paramref name="target"/>: <c>100101 imm26</c> where <c>imm26 = (target - pc) &gt;&gt; 2</c>.
    /// </summary>
    private static byte[] EncodeBl(ulong pc, ulong target)
    {
        var offset = (long)target - (long)pc;
        var imm26 = (uint)((offset >> 2) & 0x03FF_FFFF);
        var instr = 0x9400_0000u | imm26;
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, instr);
        return bytes;
    }

    /// <summary>
    /// Builds a minimal AArch64 ELF64 that imports each name in <paramref name="imports"/> via
    /// .dynsym + .rela.plt, carries a <c>.plt</c> (32-byte PLT0 header + one
    /// <paramref name="pltStride"/>-byte entry per import) at VA 0x1000, and a <c>.text</c> at
    /// VA 0x2000 with a BL to the first import's PLT entry. imageBase is 0 so VA == RVA.
    /// </summary>
    private static byte[] BuildAarch64CallerElf(int pltStride, string[] imports, int tlsdescStubCount = 0)
    {
        const uint R_AARCH64_TLSDESC = 1031;
        var importCount = imports.Length;

        // ---- .dynstr: "\0" + each name NUL-terminated ------------------------
        var dynstrBuilder = new List<byte> { 0x00 };
        var nameOffsets = new int[importCount];
        for (var i = 0; i < importCount; i++)
        {
            nameOffsets[i] = dynstrBuilder.Count;
            dynstrBuilder.AddRange(Encoding.ASCII.GetBytes(imports[i]));
            dynstrBuilder.Add(0x00);
        }
        var dynstr = dynstrBuilder.ToArray();

        // ---- .dynsym: [0] null, then one UNDEF GLOBAL|FUNC per import --------
        var dynsym = new byte[24 * (importCount + 1)];
        for (var i = 0; i < importCount; i++)
        {
            var baseOff = 24 * (i + 1);
            BinaryPrimitives.WriteUInt32LittleEndian(dynsym.AsSpan(baseOff), (uint)nameOffsets[i]); // st_name
            dynsym[baseOff + 4] = 0x12;                                                             // GLOBAL|FUNC
            BinaryPrimitives.WriteUInt16LittleEndian(dynsym.AsSpan(baseOff + 6), 0);                // UNDEF
        }

        // ---- .rela.plt: one Elf64_Rela per import (JUMP_SLOT), then optional TLSDESC relocs ---
        var relaPlt = new byte[24 * (importCount + tlsdescStubCount)];
        for (var i = 0; i < importCount; i++)
        {
            var baseOff = 24 * i;
            BinaryPrimitives.WriteUInt64LittleEndian(relaPlt.AsSpan(baseOff), 0x3000UL + (ulong)(i * 8)); // r_offset
            BinaryPrimitives.WriteUInt64LittleEndian(relaPlt.AsSpan(baseOff + 8),
                ((ulong)(i + 1) << 32) | 1026);                                                           // JUMP_SLOT
        }
        for (var i = 0; i < tlsdescStubCount; i++)
        {
            // TLSDESC relocs carry no FUNC symbol (sym index 0) and append a separate 32-byte stub.
            var baseOff = 24 * (importCount + i);
            BinaryPrimitives.WriteUInt64LittleEndian(relaPlt.AsSpan(baseOff), 0x4000UL + (ulong)(i * 8)); // r_offset
            BinaryPrimitives.WriteUInt64LittleEndian(relaPlt.AsSpan(baseOff + 8), R_AARCH64_TLSDESC);     // sym 0
        }

        // ---- .plt: 32-byte PLT0 header + importCount * pltStride + TLSDESC stubs --------------
        // NOP-fill, then (for 24-byte BTI/PAC-hardened entries) stamp a `bti c` landing pad at the
        // start of each function entry so the stride detector can recognise the hardened layout.
        var plt = new byte[Aarch64PltHeaderSize + importCount * pltStride + tlsdescStubCount * 32];
        for (var i = 0; i + 4 <= plt.Length; i += 4)
            BinaryPrimitives.WriteUInt32LittleEndian(plt.AsSpan(i), 0xD503_201Fu); // NOP
        if (pltStride == 24)
        {
            for (var i = 0; i < importCount; i++)
                BinaryPrimitives.WriteUInt32LittleEndian(
                    plt.AsSpan(Aarch64PltHeaderSize + i * pltStride), 0xD503_245Fu); // bti c
        }

        // ---- .text: BL from 0x2000 → first import's PLT entry at 0x1020 ------
        byte[] text = EncodeBl(TextVa, PltVa + Aarch64PltHeaderSize);

        // ---- .shstrtab ------------------------------------------------------
        const int shStrNameDynstr = 1;
        const int shStrNameDynsym = 9;
        const int shStrNameRelaPlt = 17;
        const int shStrNamePlt = 27;
        const int shStrNameText = 32;
        const int shStrNameShstrtab = 38;
        byte[] shstrtab = Encoding.ASCII.GetBytes(
            "\0.dynstr\0.dynsym\0.rela.plt\0.plt\0.text\0.shstrtab\0");

        // ---- Fixed file layout (generous gaps so several imports + aux stubs fit) ----
        const int offDynstr = 0x0100;
        const int offDynsym = 0x0140;
        const int offRelaPlt = 0x0200;
        const int offPlt = 0x0280;
        const int offText = 0x0300;
        const int offShstrtab = 0x0320;
        const int offShdr = 0x0380;

        const int shNum = 7; // NULL + 6 named sections
        const int shEntSize = 64;
        var totalSize = offShdr + shNum * shEntSize;

        var file = new byte[totalSize];

        // ELF64 header.
        file[0] = 0x7F; file[1] = (byte)'E'; file[2] = (byte)'L'; file[3] = (byte)'F';
        file[4] = 2; // ELFCLASS64
        file[5] = 1; // ELFDATA2LSB
        file[6] = 1; // EV_CURRENT
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(16), 3);         // e_type = ET_DYN
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(18), 0xB7);      // e_machine = EM_AARCH64
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(20), 1);         // e_version
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(40), offShdr);   // e_shoff
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(52), 64);        // e_ehsize
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(58), shEntSize); // e_shentsize
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(60), shNum);     // e_shnum
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(62), 6);         // e_shstrndx

        dynstr.CopyTo(file, offDynstr);
        dynsym.CopyTo(file, offDynsym);
        relaPlt.CopyTo(file, offRelaPlt);
        plt.CopyTo(file, offPlt);
        text.CopyTo(file, offText);
        shstrtab.CopyTo(file, offShstrtab);

        void WriteShdr(int idx, uint nameOff, uint type, ulong flags, ulong addr,
                       ulong off, ulong size, uint link, uint info, ulong align, ulong entSize)
        {
            var pos = offShdr + idx * shEntSize;
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(pos + 0), nameOff);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(pos + 4), type);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(pos + 8), flags);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(pos + 16), addr);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(pos + 24), off);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(pos + 32), size);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(pos + 40), link);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(pos + 44), info);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(pos + 48), align);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(pos + 56), entSize);
        }

        const uint SHT_NULL = 0;
        const uint SHT_PROGBITS = 1;
        const uint SHT_STRTAB = 3;
        const uint SHT_RELA = 4;
        const uint SHT_DYNSYM = 11;
        const ulong SHF_ALLOC = 2;
        const ulong SHF_EXECINSTR = 4;

        WriteShdr(0, 0, SHT_NULL, 0, 0, 0, 0, 0, 0, 0, 0);
        WriteShdr(1, shStrNameDynstr, SHT_STRTAB, SHF_ALLOC, 0, offDynstr, (ulong)dynstr.Length, 0, 0, 1, 0);
        WriteShdr(2, shStrNameDynsym, SHT_DYNSYM, SHF_ALLOC, 0, offDynsym, (ulong)dynsym.Length, 1, 1, 8, 24);
        // .rela.plt sh_info = 4 (.plt index).
        WriteShdr(3, shStrNameRelaPlt, SHT_RELA, SHF_ALLOC, 0, offRelaPlt, (ulong)relaPlt.Length, 2, 4, 8, 24);
        WriteShdr(4, shStrNamePlt, SHT_PROGBITS, SHF_ALLOC | SHF_EXECINSTR, PltVa, offPlt, (ulong)plt.Length, 0, 0, 16, 0);
        WriteShdr(5, shStrNameText, SHT_PROGBITS, SHF_ALLOC | SHF_EXECINSTR, TextVa, offText, (ulong)text.Length, 0, 0, 4, 0);
        WriteShdr(6, shStrNameShstrtab, SHT_STRTAB, 0, 0, offShstrtab, (ulong)shstrtab.Length, 0, 0, 1, 0);

        return file;
    }
}
