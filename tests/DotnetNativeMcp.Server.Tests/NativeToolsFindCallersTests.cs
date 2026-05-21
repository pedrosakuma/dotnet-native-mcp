using System.Buffers.Binary;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using DotnetNativeMcp.Core.Xref;
using DotnetNativeMcp.Server.Tools;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Server.Tests;

public class NativeToolsFindCallersTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal x64 image with a single .text section.
    /// The <paramref name="code"/> is placed at RVA 0 (file offset 0).
    /// Symbols can be injected to test name-based lookup.
    /// </summary>
    private static NativeImage CreateImage(
        byte[] code,
        ulong imageBase = 0x400000,
        Architecture arch = Architecture.X64,
        params NativeSymbol[] symbols)
    {
        var handle = ImageHandle.From("testfc", "test.so");
        var section = new NativeSection(".text", 0, (ulong)code.Length, 0, (ulong)code.Length);
        return new NativeImage(handle, "test.so", BinaryFormat.Elf, arch,
            [section], symbols, new ReadOnlyMemory<byte>(code), imageBase);
    }

    private static NativeTools MakeTools(params NativeImage[] images)
    {
        var cache = new NativeCallGraphCache();
        return new NativeTools(new TestBinaryRegistry(images), cache, new SourceResolver());
    }

    // ---------------------------------------------------------------------------
    // Bad handle → binary_not_found
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_BadHandle_ReturnsBinaryNotFound()
    {
        var tools = MakeTools();

        var result = tools.FindNativeCallers("bad-handle", "main");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.BinaryNotFound);
    }

    // ---------------------------------------------------------------------------
    // Empty target → invalid_argument
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_EmptyTarget_ReturnsInvalidArgument()
    {
        var image = CreateImage([0x90]);
        var tools = MakeTools(image);

        var result = tools.FindNativeCallers(image.Handle.Value, "   ");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // ---------------------------------------------------------------------------
    // ARM64 image with BL → succeeds and finds caller
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_Arm64ImageWithBL_ReturnsCallers()
    {
        // ARM64: BL +4 (94000001) at VA 0x400000, target = 0x400004 (within section).
        // Use a unique handle so the disk xref cache does not collide with x64 tests.
        var handle = ImageHandle.From("testfc-arm64bl", "arm64bl.elf");
        var code = new byte[] { 0x01, 0x00, 0x00, 0x94, 0x1F, 0x20, 0x03, 0xD5 };
        var section = new NativeSection(".text", 0, (ulong)code.Length, 0, (ulong)code.Length);
        var image = new NativeImage(handle, "arm64bl.elf", BinaryFormat.Elf, Architecture.Arm64,
            [section], [], new ReadOnlyMemory<byte>(code), 0x400000);

        var tools = MakeTools(image);

        var result = tools.FindNativeCallers(image.Handle.Value, "0x400004");

        result.IsError.Should().BeFalse();
        result.Data!.Callers.Should().HaveCount(1);
        result.Data.Callers[0].Mnemonic.Should().Be("bl");
    }

    // ---------------------------------------------------------------------------
    // Unknown symbol name → symbol_not_found
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_UnknownSymbolName_ReturnsSymbolNotFound()
    {
        var image = CreateImage([0x90, 0xC3]);
        var tools = MakeTools(image);

        var result = tools.FindNativeCallers(image.Handle.Value, "no_such_symbol");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.SymbolNotFound);
    }

    // ---------------------------------------------------------------------------
    // Address outside all sections → address_out_of_range
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_AddressOutOfRange_ReturnsAddressOutOfRange()
    {
        var image = CreateImage([0x90, 0xC3]);
        var tools = MakeTools(image);

        // Address 0x1 is an RVA that maps outside the 2-byte section.
        var result = tools.FindNativeCallers(image.Handle.Value, "0x1");

        // The section has VirtualSize=2, so RVA 1 is still inside (byte 0 and 1).
        // Use an RVA outside the section (e.g. RVA 10 >> imageBase 0x400000).
        var result2 = tools.FindNativeCallers(image.Handle.Value, "0xDEAD");

        result2.IsError.Should().BeTrue();
        result2.Error!.Kind.Should().Be(ErrorKinds.AddressOutOfRange);
    }

    // ---------------------------------------------------------------------------
    // No callers found → success with empty list
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_NoCaller_ReturnsEmptyCallerList()
    {
        // NOP + RET only — no branch instructions; the target at RVA 0 has no callers.
        var code = new byte[] { 0x90, 0xC3 };
        var sym = new NativeSymbol(0, "my_func", "my_func", 0, 2, ".text", true);
        var image = CreateImage(code, symbols: sym);
        var tools = MakeTools(image);

        var result = tools.FindNativeCallers(image.Handle.Value, "my_func");

        result.IsError.Should().BeFalse();
        result.Data!.TotalCallers.Should().Be(0);
        result.Data.Callers.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // Happy path: CALL targeting a known symbol
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_BySymbolName_ReturnsCallerSite()
    {
        // Layout (imageBase = 0x400000):
        //   offset 0: E8 05 00 00 00  → CALL 0x40000A  (caller)
        //   offset 5: 90              → NOP
        //   offset 6: 90 90 90 90     → 4× NOP padding
        //   offset 10 (0x40000A):  C3 → RET  ← target function
        var code = new byte[]
        {
            0xE8, 0x05, 0x00, 0x00, 0x00,  // CALL 0x40000A
            0x90, 0x90, 0x90, 0x90, 0x90,  // 5 NOPs
            0xC3,                          // RET (target)
        };

        // Symbol "my_target" at RVA 10 (= VA 0x40000A)
        var sym = new NativeSymbol(0, "my_target", "my_target", 10, 1, ".text", true);
        var image = CreateImage(code, symbols: sym);
        var tools = MakeTools(image);

        var result = tools.FindNativeCallers(image.Handle.Value, "my_target");

        result.IsError.Should().BeFalse();
        result.Data!.TotalCallers.Should().Be(1);
        result.Data.TargetSymbol.Should().Be("my_target");
        result.Data.TargetAddressHex.Should().Be("000000000040000a");

        var site = result.Data.Callers[0];
        site.Mnemonic.Should().Be("call");
        site.RawBytes.Should().Be("e805000000");
        site.SourceAddressHex.Should().Be("0000000000400000");
    }

    // ---------------------------------------------------------------------------
    // Happy path: address-based lookup (hex with 0x prefix)
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_ByHexAddress_ReturnsCallerSite()
    {
        var code = new byte[]
        {
            0xE8, 0x05, 0x00, 0x00, 0x00,  // CALL 0x40000A
            0x90, 0x90, 0x90, 0x90, 0x90,
            0xC3,
        };
        var image = CreateImage(code);
        var tools = MakeTools(image);

        // Target by VA
        var result = tools.FindNativeCallers(image.Handle.Value, "0x40000a");

        result.IsError.Should().BeFalse();
        result.Data!.TotalCallers.Should().Be(1);
        result.Data.Callers[0].Mnemonic.Should().Be("call");
    }

    // ---------------------------------------------------------------------------
    // Happy path: address-based lookup (decimal)
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_ByDecimalAddress_ReturnsCallerSite()
    {
        // imageBase = 0x400000 = 4194304
        // target RVA = 10, target VA = 4194304 + 10 = 4194314
        var code = new byte[]
        {
            0xE8, 0x05, 0x00, 0x00, 0x00,
            0x90, 0x90, 0x90, 0x90, 0x90,
            0xC3,
        };
        var image = CreateImage(code);
        var tools = MakeTools(image);

        var result = tools.FindNativeCallers(image.Handle.Value, "4194314");

        result.IsError.Should().BeFalse();
        result.Data!.TotalCallers.Should().Be(1);
    }

    // ---------------------------------------------------------------------------
    // Hints: happy path should include disassemble hint
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_WithCallers_ProvidesDisassembleHint()
    {
        var code = new byte[]
        {
            0xE8, 0x05, 0x00, 0x00, 0x00,
            0x90, 0x90, 0x90, 0x90, 0x90,
            0xC3,
        };
        var sym = new NativeSymbol(0, "tgt", "tgt", 10, 1, ".text", true);
        var image = CreateImage(code, symbols: sym);
        var tools = MakeTools(image);

        var result = tools.FindNativeCallers(image.Handle.Value, "tgt");

        result.IsError.Should().BeFalse();
        result.Hints.Should().ContainSingle();
        result.Hints[0].NextTool.Should().Be("disassemble");
    }

    // ---------------------------------------------------------------------------
    // resolveSource=true → Source field is null only when no PDB (expected in unit tests)
    // resolveSource=false → Source field is always null on every CallSite
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_ResolveSourceTrue_SourceIsNullWhenNoPdb()
    {
        var code = new byte[]
        {
            0xE8, 0x05, 0x00, 0x00, 0x00,
            0x90, 0x90, 0x90, 0x90, 0x90,
            0xC3,
        };
        var sym = new NativeSymbol(0, "tgt", "tgt", 10, 1, ".text", true);
        var image = CreateImage(code, symbols: sym);
        var tools = MakeTools(image);

        // resolveSource=true (default) — no PDB present, so Source is null but no error.
        var result = tools.FindNativeCallers(image.Handle.Value, "tgt", resolveSource: true);

        result.IsError.Should().BeFalse();
        result.Data!.TotalCallers.Should().Be(1);
        // Source will be null because no PDB is available in the test image.
        result.Data.Callers[0].Source.Should().BeNull();
    }

    [Fact]
    public void FindNativeCallers_ResolveSourceFalse_SourceIsNullOnEveryCallSite()
    {
        var code = new byte[]
        {
            0xE8, 0x05, 0x00, 0x00, 0x00,
            0x90, 0x90, 0x90, 0x90, 0x90,
            0xC3,
        };
        var sym = new NativeSymbol(0, "tgt", "tgt", 10, 1, ".text", true);
        var image = CreateImage(code, symbols: sym);
        var tools = MakeTools(image);

        var result = tools.FindNativeCallers(image.Handle.Value, "tgt", resolveSource: false);

        result.IsError.Should().BeFalse();
        result.Data!.TotalCallers.Should().Be(1);
        result.Data.Callers.Should().OnlyContain(s => s.Source == null);
    }

    // ---------------------------------------------------------------------------
    // Cache: second call returns cached result (no re-scan)
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_SecondCall_ReturnsCachedResult()
    {
        var code = new byte[]
        {
            0xE8, 0x05, 0x00, 0x00, 0x00,
            0x90, 0x90, 0x90, 0x90, 0x90,
            0xC3,
        };
        var sym = new NativeSymbol(0, "tgt", "tgt", 10, 1, ".text", true);
        var image = CreateImage(code, symbols: sym);
        var tools = MakeTools(image);

        var result1 = tools.FindNativeCallers(image.Handle.Value, "tgt");
        var result2 = tools.FindNativeCallers(image.Handle.Value, "tgt");

        result1.IsError.Should().BeFalse();
        result2.IsError.Should().BeFalse();
        result1.Data!.TotalCallers.Should().Be(result2.Data!.TotalCallers);
    }


    // ---------------------------------------------------------------------------
    // Disk cache: hit/miss coverage
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_DiskCacheHit_ReturnsSameResult()
    {
        // Arrange: isolated cache directory via XDG_CACHE_HOME.
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "dotnet-native-mcp-server-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheDir);
        var prevXdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var prevDisable = Environment.GetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", cacheDir);
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE", null);

            var code = new byte[]
            {
                0xE8, 0x05, 0x00, 0x00, 0x00,
                0x90, 0x90, 0x90, 0x90, 0x90,
                0xC3,
            };
            var sym = new NativeSymbol(0, "tgt", "tgt", 10, 1, ".text", true);
            var image = CreateImage(code, symbols: sym);

            // First call -- cache miss --> builds and persists.
            var tools1 = MakeTools(image);
            var result1 = tools1.FindNativeCallers(image.Handle.Value, "tgt");

            // Second call with a *fresh* in-memory cache -- must hit disk.
            var tools2 = MakeTools(image);
            var result2 = tools2.FindNativeCallers(image.Handle.Value, "tgt");

            result1.IsError.Should().BeFalse();
            result2.IsError.Should().BeFalse();
            result1.Data!.TotalCallers.Should().Be(result2.Data!.TotalCallers);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", prevXdg);
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE", prevDisable);
            try { Directory.Delete(cacheDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void FindNativeCallers_DiskCacheDisabled_SkipsDisk()
    {
        var prevDisable = Environment.GetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE", "0");

            var code = new byte[]
            {
                0xE8, 0x05, 0x00, 0x00, 0x00,
                0x90, 0x90, 0x90, 0x90, 0x90,
                0xC3,
            };
            var sym = new NativeSymbol(0, "tgt", "tgt", 10, 1, ".text", true);
            var image = CreateImage(code, symbols: sym);
            var tools = MakeTools(image);

            // Should succeed even when cache is disabled.
            var result = tools.FindNativeCallers(image.Handle.Value, "tgt");

            result.IsError.Should().BeFalse();
            result.Data!.TotalCallers.Should().Be(1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE", prevDisable);
        }
    }

    [Fact]
    public void FindNativeCallers_MachOCrossImage_ReturnsCrossImageCaller()
    {
        var callerImage = MachOReader.Read(NativeToolsFindCallersMachOTestData.BuildCallerX64(), "caller-x64")!;
        var calleeImage = MachOReader.Read(NativeToolsFindCallersMachOTestData.BuildCalleeWithExportTrieX64(), "callee-trie")!;
        var tools = MakeTools(calleeImage, callerImage);

        var result = tools.FindNativeCallers(calleeImage.Handle.Value, "foo", crossImage: true);

        result.IsError.Should().BeFalse(result.Error?.Message ?? string.Empty);
        result.Data!.Callers.Should().ContainSingle();
        result.Data.Callers[0].IsCrossImage.Should().BeTrue();
        result.Data.Callers[0].CallerImagePath.Should().Be("caller-x64");
        result.Data.Callers[0].SourceAddressHex.Should().Be("0000000000001000");
    }

    // ---------------------------------------------------------------------------
    // Test registry
    // ---------------------------------------------------------------------------

    private sealed class TestBinaryRegistry(params NativeImage[] images) : INativeBinaryRegistry
    {
        private readonly Dictionary<string, NativeImage> _images =
            images.ToDictionary(img => img.Handle.Value, StringComparer.OrdinalIgnoreCase);

        public NativeResult<NativeImage> Load(string path, string? expectedBuildId = null) =>
            throw new NotSupportedException();

        public void RegisterHint(string path, string? buildId = null) { }

        public bool TryGet(string imageHandle, out NativeImage? image)
        {
            var found = _images.TryGetValue(imageHandle, out var resolved);
            image = resolved;
            return found;
        }

        public IReadOnlyList<NativeImage> List() => [.. _images.Values];
    }
}

internal static class NativeToolsFindCallersMachOTestData
{
    private const int CpuTypeX86_64 = 0x01000007;
    private const uint LcSegment64 = 0x19;
    private const uint LcSymtab = 0x2;
    private const uint LcDysymtab = 0xB;
    private const uint LcDyldInfoOnly = 0x80000022;
    private const uint SectionTypeSymbolStubs = 0x8;
    private const int HeaderSize = 32;
    private const int Segment64SizeWithThreeSections = 72 + (3 * 80);
    private const int Segment64SizeWithOneSection = 72 + 80;
    private const int SymtabCommandSize = 24;
    private const int DysymtabCommandSize = 80;
    private const int DyldInfoCommandSize = 48;

    public static byte[] BuildCallerX64()
    {
        var rel32 = unchecked((int)(0x2000UL - (0x1000UL + 5)));
        var rel32Bytes = BitConverter.GetBytes(rel32);
        var text = new byte[] { 0xE8, rel32Bytes[0], rel32Bytes[1], rel32Bytes[2], rel32Bytes[3], 0xC3 };
        var stubs = new byte[] { 0xFF, 0x25, 0x00, 0x00, 0x00, 0x00 };
        var stubHelper = new byte[] { 0x90, 0x90, 0x90, 0x90 };
        var loadCommandsSize = Segment64SizeWithThreeSections + SymtabCommandSize + DysymtabCommandSize;
        const int textOffset = 0x200;
        const int stubsOffset = 0x300;
        const int stubHelperOffset = 0x340;
        const int indirectOffset = 0x380;
        const int symtabOffset = 0x390;
        const int strtabOffset = 0x3A0;

        var strtab = BuildStringTable("_foo");
        var symtab = BuildUndefinedExternalSymbol(1);
        var file = new byte[strtabOffset + strtab.Length];

        WriteMachHeader(file, 3, loadCommandsSize);
        var commandOffset = HeaderSize;
        WriteSegment64(file, commandOffset, "__TEXT", 0x1000UL, 0x1400UL, 0, 0x400UL, [
            new SectionSpec("__text", "__TEXT", 0x1000UL, (ulong)text.Length, textOffset, 0x80000400u, 0, 0),
            new SectionSpec("__stubs", "__TEXT", 0x2000UL, (ulong)stubs.Length, stubsOffset, SectionTypeSymbolStubs, 0, (uint)stubs.Length),
            new SectionSpec("__stub_helper", "__TEXT", 0x2100UL, (ulong)stubHelper.Length, stubHelperOffset, 0, 1, 0),
        ]);

        commandOffset += Segment64SizeWithThreeSections;
        WriteSymtabCommand(file, commandOffset, symtabOffset, 1, strtabOffset, strtab.Length);
        commandOffset += SymtabCommandSize;
        WriteDysymtabCommand(file, commandOffset, indirectOffset, 1);

        text.CopyTo(file, textOffset);
        stubs.CopyTo(file, stubsOffset);
        stubHelper.CopyTo(file, stubHelperOffset);
        WriteLe32(file, indirectOffset, 0);
        symtab.CopyTo(file, symtabOffset);
        strtab.CopyTo(file, strtabOffset);
        return file;
    }

    public static byte[] BuildCalleeWithExportTrieX64()
    {
        var loadCommandsSize = Segment64SizeWithOneSection + SymtabCommandSize + DyldInfoCommandSize;
        const int textOffset = 0x200;
        const int symtabOffset = 0x280;
        const int strtabOffset = 0x280;
        var strtab = new byte[] { 0x00 };
        var exportTrie = BuildSingleExportTrie("_foo", 0x40);
        var exportTrieOffset = strtabOffset + strtab.Length;
        var file = new byte[exportTrieOffset + exportTrie.Length];

        WriteMachHeader(file, 3, loadCommandsSize);
        var commandOffset = HeaderSize;
        WriteSegment64(file, commandOffset, "__TEXT", 0x40UL, 0x100UL, 0, 0x100UL, [
            new SectionSpec("__text", "__TEXT", 0x40UL, 4, textOffset, 0x80000400u, 0, 0),
        ]);

        commandOffset += Segment64SizeWithOneSection;
        WriteSymtabCommand(file, commandOffset, symtabOffset, 0, strtabOffset, strtab.Length);
        commandOffset += SymtabCommandSize;
        WriteDyldInfoCommand(file, commandOffset, exportTrieOffset, exportTrie.Length);

        file[textOffset] = 0xC3;
        strtab.CopyTo(file, strtabOffset);
        exportTrie.CopyTo(file, exportTrieOffset);
        return file;
    }

    private static byte[] BuildSingleExportTrie(string symbolName, ulong address)
    {
        var edgeBytes = System.Text.Encoding.ASCII.GetBytes(symbolName);
        var addressUleb = EncodeUleb128(address);
        var childNode = new List<byte> { (byte)(1 + addressUleb.Length), 0x00 };
        childNode.AddRange(addressUleb);
        childNode.Add(0x00);

        var root = new List<byte> { 0x00, 0x01 };
        root.AddRange(edgeBytes);
        root.Add(0x00);
        root.Add((byte)(root.Count + 1));
        root.AddRange(childNode);
        return [.. root];
    }

    private static byte[] EncodeUleb128(ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            var b = (byte)(value & 0x7Fu);
            value >>= 7;
            if (value != 0)
                b |= 0x80;
            bytes.Add(b);
        }
        while (value != 0);

        return [.. bytes];
    }

    private static byte[] BuildStringTable(string symbolName)
    {
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(symbolName);
        var strtab = new byte[1 + nameBytes.Length + 1];
        nameBytes.CopyTo(strtab, 1);
        return strtab;
    }

    private static byte[] BuildUndefinedExternalSymbol(uint stringOffset)
    {
        var entry = new byte[16];
        WriteLe32(entry, 0, stringOffset);
        entry[4] = 0x01;
        return entry;
    }

    private static void WriteMachHeader(byte[] file, uint ncmds, int sizeofcmds)
    {
        WriteLe32(file, 0, 0xFEEDFACF);
        WriteLe32(file, 4, (uint)CpuTypeX86_64);
        WriteLe32(file, 8, 0);
        WriteLe32(file, 12, 2);
        WriteLe32(file, 16, ncmds);
        WriteLe32(file, 20, (uint)sizeofcmds);
        WriteLe32(file, 24, 0);
        WriteLe32(file, 28, 0);
    }

    private static void WriteSegment64(
        byte[] file,
        int offset,
        string segmentName,
        ulong vmaddr,
        ulong vmsize,
        ulong fileoff,
        ulong filesize,
        IReadOnlyList<SectionSpec> sections)
    {
        WriteLe32(file, offset, LcSegment64);
        WriteLe32(file, offset + 4, (uint)(72 + (sections.Count * 80)));
        WriteAscii(file, offset + 8, segmentName);
        WriteLe64(file, offset + 24, vmaddr);
        WriteLe64(file, offset + 32, vmsize);
        WriteLe64(file, offset + 40, fileoff);
        WriteLe64(file, offset + 48, filesize);
        WriteLe32(file, offset + 56, 7);
        WriteLe32(file, offset + 60, 5);
        WriteLe32(file, offset + 64, (uint)sections.Count);
        WriteLe32(file, offset + 68, 0);

        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            var sectionOffset = offset + 72 + (i * 80);
            WriteAscii(file, sectionOffset, section.SectionName);
            WriteAscii(file, sectionOffset + 16, section.SegmentName);
            WriteLe64(file, sectionOffset + 32, section.Address);
            WriteLe64(file, sectionOffset + 40, section.Size);
            WriteLe32(file, sectionOffset + 48, (uint)section.FileOffset);
            WriteLe32(file, sectionOffset + 64, section.Flags);
            WriteLe32(file, sectionOffset + 68, (uint)section.Reserved1);
            WriteLe32(file, sectionOffset + 72, section.Reserved2);
        }
    }

    private static void WriteSymtabCommand(byte[] file, int offset, int symoff, uint nsyms, int stroff, int strsize)
    {
        WriteLe32(file, offset, LcSymtab);
        WriteLe32(file, offset + 4, SymtabCommandSize);
        WriteLe32(file, offset + 8, (uint)symoff);
        WriteLe32(file, offset + 12, nsyms);
        WriteLe32(file, offset + 16, (uint)stroff);
        WriteLe32(file, offset + 20, (uint)strsize);
    }

    private static void WriteDysymtabCommand(byte[] file, int offset, int indirectSymOff, uint nIndirectSyms)
    {
        WriteLe32(file, offset, LcDysymtab);
        WriteLe32(file, offset + 4, DysymtabCommandSize);
        WriteLe32(file, offset + 56, (uint)indirectSymOff);
        WriteLe32(file, offset + 60, nIndirectSyms);
    }

    private static void WriteDyldInfoCommand(byte[] file, int offset, int exportOff, int exportSize)
    {
        WriteLe32(file, offset, LcDyldInfoOnly);
        WriteLe32(file, offset + 4, DyldInfoCommandSize);
        WriteLe32(file, offset + 40, (uint)exportOff);
        WriteLe32(file, offset + 44, (uint)exportSize);
    }

    private static void WriteLe32(byte[] buffer, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), value);

    private static void WriteLe64(byte[] buffer, int offset, ulong value)
        => BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(offset), value);

    private static void WriteAscii(byte[] buffer, int offset, string value)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(value);
        Array.Copy(bytes, 0, buffer, offset, Math.Min(bytes.Length, buffer.Length - offset));
    }

    private sealed record SectionSpec(
        string SectionName,
        string SegmentName,
        ulong Address,
        ulong Size,
        int FileOffset,
        uint Flags,
        int Reserved1,
        uint Reserved2);
}
