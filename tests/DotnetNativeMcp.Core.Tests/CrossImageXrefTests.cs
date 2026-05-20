using System.Buffers.Binary;
using System.Text;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Xref;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Tests for <see cref="ElfReader.ResolvePltEntries"/> and
/// <see cref="CrossImageCallGraphScanner"/>.
/// </summary>
public sealed class CrossImageXrefTests
{
    // -------------------------------------------------------------------------
    // Helpers: build minimal synthesized ELF64 binaries
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal valid ELF64 LE x86-64 binary that:
    /// <list type="bullet">
    ///   <item>exports nothing (it is the caller)</item>
    ///   <item>imports <c>lib_func</c> via .dynsym + .rela.plt</item>
    ///   <item>has a .plt.sec section at VA 0x1000 (16 NOP-like bytes)</item>
    ///   <item>has a .text section at VA 0x2000 with a CALL to the plt.sec stub</item>
    /// </list>
    /// imageBase is 0 so VA == RVA.
    /// </summary>
    private static byte[] BuildCallerElf()
    {
        // ---- constants -------------------------------------------------------
        const ulong pltSecVa = 0x1000UL;
        const ulong textVa = 0x2000UL;

        // CALL rel32: source at textVa, target at pltSecVa.
        // rel32 = pltSecVa - (textVa + 5) = 0x1000 - 0x2005 = -0x1005
        var rel32 = unchecked((int)(pltSecVa - (textVa + 5)));
        byte[] call = [0xE8, .. BitConverter.GetBytes(rel32)]; // 5 bytes

        // ---- .dynstr ---------------------------------------------------------
        // \0lib_func\0  (10 bytes)
        byte[] dynstr = [0x00, 0x6C, 0x69, 0x62, 0x5F, 0x66, 0x75, 0x6E, 0x63, 0x00]; // "\0lib_func\0"

        // ---- .dynsym ---------------------------------------------------------
        // Two Elf64_Sym entries (24 bytes each):
        //   [0] null  (all zeros)
        //   [1] lib_func: st_name=1, st_info=0x12 (GLOBAL|FUNC), shndx=0 (UNDEF)
        var dynsym = new byte[48];
        // entry 0: all zeros
        // entry 1:
        BinaryPrimitives.WriteUInt32LittleEndian(dynsym.AsSpan(24), 1);    // st_name = 1
        dynsym[28] = 0x12; // st_info = STB_GLOBAL|STT_FUNC
        dynsym[29] = 0;    // st_other
        BinaryPrimitives.WriteUInt16LittleEndian(dynsym.AsSpan(30), 0);    // st_shndx = SHN_UNDEF
        BinaryPrimitives.WriteUInt64LittleEndian(dynsym.AsSpan(32), 0);    // st_value
        BinaryPrimitives.WriteUInt64LittleEndian(dynsym.AsSpan(40), 0);    // st_size

        // ---- .rela.plt -------------------------------------------------------
        // One Elf64_Rela (24 bytes):
        //   r_offset = some GOT slot (e.g. 0x3018, doesn't affect PLT resolution)
        //   r_info   = (sym_idx=1) << 32 | R_X86_64_JUMP_SLOT(7)
        //   r_addend = 0
        var relaPlt = new byte[24];
        BinaryPrimitives.WriteUInt64LittleEndian(relaPlt.AsSpan(0), 0x3018UL);         // r_offset
        BinaryPrimitives.WriteUInt64LittleEndian(relaPlt.AsSpan(8), (1UL << 32) | 7);  // r_info
        BinaryPrimitives.WriteInt64LittleEndian(relaPlt.AsSpan(16), 0);                // r_addend

        // ---- .plt.sec --------------------------------------------------------
        // 16 NOP bytes (placeholder stubs; doesn't need to be real code).
        byte[] pltSec = new byte[16];
        Array.Fill(pltSec, (byte)0x90); // NOPs

        // ---- .text -----------------------------------------------------------
        // CALL to plt stub (5 bytes).
        byte[] text = call;

        // ---- .shstrtab -------------------------------------------------------
        // Section name string table:
        // 0: \0
        // 1: .dynstr\0    (8 bytes)
        // 9: .dynsym\0    (8 bytes)
        // 17: .rela.plt\0 (10 bytes)
        // 27: .plt.sec\0  (9 bytes)
        // 36: .text\0     (6 bytes)
        // 42: .shstrtab\0 (10 bytes)
        // Total: 52 bytes
        const int shStrNameDynstr = 1;
        const int shStrNameDynsym = 9;
        const int shStrNameRelaPlt = 17;
        const int shStrNamePltSec = 27;
        const int shStrNameText = 36;
        const int shStrNameShstrtab = 42;
        byte[] shstrtab = Encoding.ASCII.GetBytes(
            "\0.dynstr\0.dynsym\0.rela.plt\0.plt.sec\0.text\0.shstrtab\0");

        // ---- File layout -----------------------------------------------------
        // The file is built with fixed offsets. All values are chosen so that
        // section boundaries are safely aligned and the file is self-consistent.

        // Fixed file offsets for each section's data:
        const int offDynstr = 0x0100;
        const int offDynsym = 0x0120;
        const int offRelaPlt = 0x0150;
        const int offPltSec = 0x0180;
        const int offText = 0x0200;
        const int offShstrtab = 0x0220;
        const int offShdr = 0x0280; // section headers (must be 8-byte aligned)

        const int shNum = 7; // NULL + 6 named sections
        const int shEntSize = 64;
        const int totalSize = offShdr + shNum * shEntSize;

        var file = new byte[totalSize];

        // ELF64 header (64 bytes at offset 0):
        // e_ident
        file[0] = 0x7F; file[1] = (byte)'E'; file[2] = (byte)'L'; file[3] = (byte)'F'; // magic
        file[4] = 2;     // ELFCLASS64
        file[5] = 1;     // ELFDATA2LSB (LE)
        file[6] = 1;     // EV_CURRENT
        // [7..15]: zeros (OS/ABI + padding)
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(16), 3);       // e_type = ET_DYN
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(18), 0x3E);    // e_machine = EM_X86_64
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(20), 1);       // e_version = EV_CURRENT
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(24), 0);       // e_entry
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(32), 0);       // e_phoff (no phdrs)
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(40), offShdr); // e_shoff
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(48), 0);       // e_flags
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(52), 64);      // e_ehsize
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(54), 0);       // e_phentsize
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(56), 0);       // e_phnum
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(58), shEntSize); // e_shentsize
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(60), shNum);   // e_shnum
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(62), 6);       // e_shstrndx = index of .shstrtab

        // Copy section data.
        dynstr.CopyTo(file, offDynstr);
        dynsym.CopyTo(file, offDynsym);
        relaPlt.CopyTo(file, offRelaPlt);
        pltSec.CopyTo(file, offPltSec);
        text.CopyTo(file, offText);
        shstrtab.CopyTo(file, offShstrtab);

        // Write section headers (64 bytes each, starting at offShdr).
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
        const uint SHT_DYNSYM = 11;
        const uint SHT_STRTAB = 3;
        const uint SHT_RELA = 4;
        const ulong SHF_ALLOC = 2;
        const ulong SHF_EXECINSTR = 4;

        // [0] NULL
        WriteShdr(0, 0, SHT_NULL, 0, 0, 0, 0, 0, 0, 0, 0);
        // [1] .dynstr — sh_link points to itself (string table for .dynsym links here)
        WriteShdr(1, shStrNameDynstr, SHT_STRTAB, SHF_ALLOC, 0, offDynstr, (ulong)dynstr.Length, 0, 0, 1, 0);
        // [2] .dynsym — sh_link = 1 (.dynstr index), sh_entsize = 24
        WriteShdr(2, shStrNameDynsym, SHT_DYNSYM, SHF_ALLOC, 0, offDynsym, (ulong)dynsym.Length, 1, 1, 8, 24);
        // [3] .rela.plt — sh_link = 2 (.dynsym), sh_info = 4 (.plt.sec index)
        WriteShdr(3, shStrNameRelaPlt, SHT_RELA, SHF_ALLOC, 0, offRelaPlt, (ulong)relaPlt.Length, 2, 4, 8, 24);
        // [4] .plt.sec — code section at VA pltSecVa
        WriteShdr(4, shStrNamePltSec, SHT_PROGBITS, SHF_ALLOC | SHF_EXECINSTR, pltSecVa, offPltSec, (ulong)pltSec.Length, 0, 0, 16, 16);
        // [5] .text — code section at VA textVa
        WriteShdr(5, shStrNameText, SHT_PROGBITS, SHF_ALLOC | SHF_EXECINSTR, textVa, offText, (ulong)text.Length, 0, 0, 16, 0);
        // [6] .shstrtab
        WriteShdr(6, shStrNameShstrtab, SHT_STRTAB, 0, 0, offShstrtab, (ulong)shstrtab.Length, 0, 0, 1, 0);

        return file;
    }

    /// <summary>Creates a minimal callee <see cref="NativeImage"/> that exports <c>lib_func</c>.</summary>
    private static NativeImage MakeCalleeImage()
    {
        var handle = ImageHandle.From("deadbeef01", "liblib.so");
        var sym = new NativeSymbol(0, "lib_func", "lib_func", 0x100, 16, ".text", true);
        var section = new NativeSection(".text", 0x100, 16, 0x100, 16);
        return new NativeImage(handle, "/lib/liblib.so", BinaryFormat.Elf,
            Architecture.X64, [section], [sym], ReadOnlyMemory<byte>.Empty, 0);
    }

    // -------------------------------------------------------------------------
    // ElfReader.ResolvePltEntries
    // -------------------------------------------------------------------------

    [Fact]
    public void ResolvePltEntries_SyntheticCallerElf_MapsLibFuncToExpectedPltVa()
    {
        var elfBytes = BuildCallerElf();
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(elfBytes), "caller");
        image.Should().NotBeNull("ELF parsing must succeed");

        var plt = ElfReader.ResolvePltEntries(image!);

        plt.Should().ContainKey(0x1000UL);
        plt[0x1000UL].Should().Be("lib_func");
    }

    [Fact]
    public void ResolvePltEntries_NonElfImage_ReturnsEmpty()
    {
        var handle = ImageHandle.From("aabb", "fake.dll");
        var image = new NativeImage(handle, "fake.dll", BinaryFormat.Pe,
            Architecture.X64, [], [], ReadOnlyMemory<byte>.Empty, 0x400000);

        ElfReader.ResolvePltEntries(image).Should().BeEmpty();
    }

    [Fact]
    public void ResolvePltEntries_EmptyBytes_ReturnsEmpty()
    {
        var handle = ImageHandle.From("aabb", "empty.so");
        var image = new NativeImage(handle, "empty.so", BinaryFormat.Elf,
            Architecture.X64, [], [], ReadOnlyMemory<byte>.Empty, 0);

        ElfReader.ResolvePltEntries(image).Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // CrossImageCallGraphScanner
    // -------------------------------------------------------------------------

    [Fact]
    public void FindCallers_SyntheticCallerElf_ReturnsCallSiteInText()
    {
        var elfBytes = BuildCallerElf();
        var callerImage = ElfReader.Read(new ReadOnlyMemory<byte>(elfBytes), "caller.so");
        callerImage.Should().NotBeNull();

        var calleeImage = MakeCalleeImage();

        // Build the caller's call graph.
        var callerGraph = NativeCallGraphBuilder.Build(callerImage!);

        // callerGraph[0x1000] should have the call site from .text.
        callerGraph.Should().ContainKey(0x1000UL,
            "the CALL in .text targets the .plt.sec stub at VA 0x1000");

        // Now use the cross-image scanner.
        var results = CrossImageCallGraphScanner.FindCallers(
            callerImage!, callerGraph, "lib_func", null);

        results.Should().ContainSingle("exactly one call site targets lib_func's PLT entry");
        results[0].SourceAddressHex.Should().StartWith("0000000000002000",
            "the CALL instruction is at VA 0x2000");
        results[0].CallerImageBuildId.Should().Be(callerImage!.Handle.BuildIdHex);
        results[0].CallerImagePath.Should().Be("caller.so");
        results[0].Mnemonic.Should().Be("call");
    }

    [Fact]
    public void FindCallers_SymbolNotInPlt_ReturnsEmpty()
    {
        var elfBytes = BuildCallerElf();
        var callerImage = ElfReader.Read(new ReadOnlyMemory<byte>(elfBytes), "caller.so");
        callerImage.Should().NotBeNull();

        var callerGraph = NativeCallGraphBuilder.Build(callerImage!);

        // Ask for a symbol that is NOT in the PLT.
        var results = CrossImageCallGraphScanner.FindCallers(
            callerImage!, callerGraph, "unknown_func", null);

        results.Should().BeEmpty();
    }

    [Fact]
    public void FindCallers_PeImage_ReturnsEmpty()
    {
        var handle = ImageHandle.From("ccdd", "fake.dll");
        var callerImage = new NativeImage(handle, "fake.dll", BinaryFormat.Pe,
            Architecture.X64, [], [], ReadOnlyMemory<byte>.Empty, 0x400000);

        var emptyGraph = new Dictionary<ulong, IReadOnlyList<CallSite>>();
        var results = CrossImageCallGraphScanner.FindCallers(callerImage, emptyGraph, "lib_func", null);

        results.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // NativeCallGraphCache.FindCrossImageCallers
    // -------------------------------------------------------------------------

    [Fact]
    public void FindCrossImageCallers_WithRegistry_ReturnsCrossImageSite()
    {
        var elfBytes = BuildCallerElf();
        var callerImage = ElfReader.Read(new ReadOnlyMemory<byte>(elfBytes), "caller.so");
        callerImage.Should().NotBeNull();

        var calleeImage = MakeCalleeImage();

        var cache = new NativeCallGraphCache();
        var registryFake = new TwoImageRegistry(calleeImage, callerImage!);

        var results = cache.FindCrossImageCallers(calleeImage, "lib_func", null, registryFake);

        results.Should().ContainSingle();
        results[0].CallerImageBuildId.Should().Be(callerImage!.Handle.BuildIdHex);
        results[0].SourceAddressHex.Should().StartWith("0000000000002000");
    }

    [Fact]
    public void FindCrossImageCallers_CrossXrefDisabledViaEnv_ReturnsEmpty()
    {
        var elfBytes = BuildCallerElf();
        var callerImage = ElfReader.Read(new ReadOnlyMemory<byte>(elfBytes), "caller.so");
        callerImage.Should().NotBeNull();

        var calleeImage = MakeCalleeImage();
        var cache = new NativeCallGraphCache();
        var registryFake = new TwoImageRegistry(calleeImage, callerImage!);

        var originalEnv = Environment.GetEnvironmentVariable("DOTNET_NATIVE_MCP_CROSS_XREF");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_CROSS_XREF", "0");

            var results = cache.FindCrossImageCallers(calleeImage, "lib_func", null, registryFake);

            results.Should().BeEmpty("DOTNET_NATIVE_MCP_CROSS_XREF=0 disables cross-image scanning");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_CROSS_XREF", originalEnv);
        }
    }

    [Fact]
    public void IsCrossXrefEnabled_DefaultIsTrue()
    {
        var originalEnv = Environment.GetEnvironmentVariable("DOTNET_NATIVE_MCP_CROSS_XREF");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_CROSS_XREF", null);
            NativeCallGraphCache.IsCrossXrefEnabled.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_CROSS_XREF", originalEnv);
        }
    }

    [Fact]
    public void IsCrossXrefEnabled_SetToZero_ReturnsFalse()
    {
        var originalEnv = Environment.GetEnvironmentVariable("DOTNET_NATIVE_MCP_CROSS_XREF");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_CROSS_XREF", "0");
            NativeCallGraphCache.IsCrossXrefEnabled.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_CROSS_XREF", originalEnv);
        }
    }

    // -------------------------------------------------------------------------
    // Fake registry
    // -------------------------------------------------------------------------

    private sealed class TwoImageRegistry(NativeImage first, NativeImage second) : INativeBinaryRegistry
    {
        public NativeResult<NativeImage> Load(string path, string? expectedBuildId = null)
            => throw new NotSupportedException();

        public void RegisterHint(string path, string? buildId = null) { }

        public bool TryGet(string imageHandle, out NativeImage? image)
        {
            if (imageHandle == first.Handle.Value) { image = first; return true; }
            if (imageHandle == second.Handle.Value) { image = second; return true; }
            image = null;
            return false;
        }

        public IReadOnlyList<NativeImage> List() => [first, second];
    }
}
