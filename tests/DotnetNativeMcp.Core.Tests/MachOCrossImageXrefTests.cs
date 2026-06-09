using System.Buffers.Binary;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Xref;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public sealed class MachOCrossImageXrefTests
{
    [Fact]
    public void ResolveStubEntries_X64MachO_MapsStubToImportedSymbol()
    {
        var image = MachOReader.Read(MachOXrefTestData.BuildCallerX64(), "caller-x64")!;

        var stubs = MachOReader.ResolveStubEntries(image);

        stubs.Should().ContainSingle();
        stubs.Should().ContainKey(0x2000UL);
        stubs[0x2000UL].Should().Be("foo");
    }

    [Fact]
    public void ResolveStubEntries_Arm64MachO_MapsStubToImportedSymbol()
    {
        var image = MachOReader.Read(MachOXrefTestData.BuildCallerArm64(), "caller-arm64")!;

        var stubs = MachOReader.ResolveStubEntries(image);

        stubs.Should().ContainSingle();
        stubs.Should().ContainKey(0x2000UL);
        stubs[0x2000UL].Should().Be("foo");
    }

    [Fact]
    public void ReadExports_PrefersExportTrie_WhenPresent()
    {
        var image = MachOReader.Read(MachOXrefTestData.BuildCalleeWithExportTrieX64(), "callee-trie")!;

        var exports = MachOReader.ReadExports(image);

        exports.Should().ContainSingle();
        exports["foo"].Should().Be(0x1040UL);
    }

    [Fact]
    public void ReadExports_UsesTextSegmentBase_WhenPageZeroPrecedesText()
    {
        var image = MachOReader.Read(MachOXrefTestData.BuildCalleeWithPageZeroAndExportTrieX64(), "callee-pagezero")!;

        var exports = MachOReader.ReadExports(image);

        exports.Should().ContainSingle();
        exports["foo"].Should().Be(0x1040UL);
    }

    [Fact]
    public void ReadExports_FallsBackToSymtab_WhenTrieAbsent()
    {
        var image = MachOReader.Read(MachOXrefTestData.BuildCalleeWithSymtabArm64(), "callee-symtab")!;

        var exports = MachOReader.ReadExports(image);

        exports.Should().ContainSingle();
        exports["foo"].Should().Be(0x40UL);
    }

    [Fact]
    public void FindCallers_MachOX64Caller_ReturnsCrossImageSite()
    {
        var callerImage = MachOReader.Read(MachOXrefTestData.BuildCallerX64(), "caller-x64")!;
        var calleeImage = MachOReader.Read(MachOXrefTestData.BuildCalleeWithExportTrieX64(), "callee-trie")!;
        var callerGraph = NativeCallGraphBuilder.Build(callerImage);

        var results = CrossImageCallGraphScanner.FindCallers(
            callerImage,
            callerGraph,
            "foo",
            null,
            MachOReader.ReadExports(calleeImage),
            MachOReader.ResolveStubEntries(callerImage));

        results.Should().ContainSingle();
        results[0].CallerImageBuildId.Should().Be(callerImage.Handle.BuildIdHex);
        results[0].CallerImagePath.Should().Be("caller-x64");
        results[0].SourceAddressHex.Should().Be("0000000000001000");
        results[0].Mnemonic.Should().Be("call");
    }

    [Fact]
    public void FindCallers_MachOArm64Caller_ReturnsCrossImageSite()
    {
        var callerImage = MachOReader.Read(MachOXrefTestData.BuildCallerArm64(), "caller-arm64")!;
        var calleeImage = MachOReader.Read(MachOXrefTestData.BuildCalleeWithSymtabArm64(), "callee-symtab")!;
        var callerGraph = NativeCallGraphBuilder.Build(callerImage);

        var results = CrossImageCallGraphScanner.FindCallers(
            callerImage,
            callerGraph,
            "foo",
            null,
            MachOReader.ReadExports(calleeImage),
            MachOReader.ResolveStubEntries(callerImage));

        results.Should().ContainSingle();
        results[0].CallerImageBuildId.Should().Be(callerImage.Handle.BuildIdHex);
        results[0].CallerImagePath.Should().Be("caller-arm64");
        results[0].SourceAddressHex.Should().Be("0000000000001000");
        results[0].Mnemonic.Should().Be("bl");
    }

    [Fact]
    public void FindCrossImageCallers_MachORegistry_ReturnsCrossImageSite()
    {
        var callerImage = MachOReader.Read(MachOXrefTestData.BuildCallerX64(), "caller-x64")!;
        var calleeImage = MachOReader.Read(MachOXrefTestData.BuildCalleeWithExportTrieX64(), "callee-trie")!;
        var cache = new NativeCallGraphCache();
        var registry = new TwoImageRegistry(calleeImage, callerImage);

        var results = cache.FindCrossImageCallers(calleeImage, "foo", null, registry);

        results.Should().ContainSingle();
        results[0].CallerImagePath.Should().Be("caller-x64");
        results[0].SourceAddressHex.Should().Be("0000000000001000");
    }

    [Fact]
    public void GetOrBuildMachOExports_DoesNotPoisonSameImageCallGraphCache()
    {
        var image = MachOReader.Read(MachOXrefTestData.BuildSelfCallerWithExportTrieX64(), "self-trie")!;
        var cache = new NativeCallGraphCache();

        cache.GetOrBuildMachOExports(image).Should().ContainKey("foo");
        var callers = cache.FindCallers(image, 0x1050UL);

        callers.Should().ContainSingle();
        callers[0].SourceAddressHex.Should().Be("0000000000001040");
    }

    [Fact]
    public void FindCrossImageCallers_PreservesExistingSameImageDiskCache()
    {
        var originalXdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var originalDisable = Environment.GetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE");
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache",
            "dotnet-native-mcp-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(cacheDir);
        try
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", cacheDir);
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE", null);

            var calleeImage = MachOReader.Read(MachOXrefTestData.BuildSelfCallerWithExportTrieX64(), "self-trie")!;
            var callerImage = MachOReader.Read(MachOXrefTestData.BuildCallerX64(), "caller-x64")!;
            var registry = new TwoImageRegistry(calleeImage, callerImage);

            var warmCache = new NativeCallGraphCache();
            warmCache.FindCallers(calleeImage, 0x1050UL).Should().ContainSingle();

            var crossCache = new NativeCallGraphCache();
            crossCache.FindCrossImageCallers(calleeImage, "foo", null, registry).Should().ContainSingle();

            var reloadCache = new NativeCallGraphCache();
            var callers = reloadCache.FindCallers(calleeImage, 0x1050UL);
            callers.Should().ContainSingle();
            callers[0].SourceAddressHex.Should().Be("0000000000001040");
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", originalXdg);
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE", originalDisable);
            try { Directory.Delete(cacheDir, recursive: true); } catch { }
        }
    }

    private sealed class TwoImageRegistry(NativeImage first, NativeImage second) : INativeBinaryRegistry
    {
        public NativeResult<NativeImage> Load(string path, string? expectedBuildId = null)
            => throw new NotSupportedException();

        public NativeResult<string> RegisterHint(string path, string? buildId = null)
            => NativeResult.Ok("registered", path);

        public bool TryGet(string imageHandle, out NativeImage? image)
        {
            if (imageHandle == first.Handle.Value)
            {
                image = first;
                return true;
            }

            if (imageHandle == second.Handle.Value)
            {
                image = second;
                return true;
            }

            image = null;
            return false;
        }

        public IReadOnlyList<NativeImage> List() => [first, second];
    }
}

internal static class MachOXrefTestData
{
    private const int CpuTypeX86_64 = 0x01000007;
    private const int CpuTypeArm64 = 0x0100000C;
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
        return BuildCaller(CpuTypeX86_64, text, stubs, stubHelper);
    }

    public static byte[] BuildCallerArm64()
    {
        var text = new byte[] { 0x00, 0x04, 0x00, 0x94, 0xC0, 0x03, 0x5F, 0xD6 };
        var stubs = new byte[12];
        var stubHelper = new byte[] { 0x1F, 0x20, 0x03, 0xD5 };
        return BuildCaller(CpuTypeArm64, text, stubs, stubHelper);
    }

    public static byte[] BuildCalleeWithExportTrieX64()
        => BuildCallee(CpuTypeX86_64, includeExportTrie: true, includeSymtabExport: false);

    public static byte[] BuildCalleeWithPageZeroAndExportTrieX64()
        => BuildCallee(CpuTypeX86_64, includeExportTrie: true, includeSymtabExport: false, includePageZero: true);

    public static byte[] BuildCalleeWithSymtabArm64()
        => BuildCallee(CpuTypeArm64, includeExportTrie: false, includeSymtabExport: true);

    public static byte[] BuildSelfCallerWithExportTrieX64()
        => BuildSelfCallerWithExportTrie(CpuTypeX86_64);

    private static byte[] BuildCaller(int cpuType, byte[] text, byte[] stubs, byte[] stubHelper)
    {
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

        WriteMachHeader(file, cpuType, 3, loadCommandsSize);

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

    private static byte[] BuildCallee(int cpuType, bool includeExportTrie, bool includeSymtabExport, bool includePageZero = false)
    {
        var loadCommandsSize = (includePageZero ? 72 : 0) + Segment64SizeWithOneSection + SymtabCommandSize + (includeExportTrie ? DyldInfoCommandSize : 0);
        const int textOffset = 0x200;
        const int symtabOffset = 0x280;
        var strtabOffset = symtabOffset + (includeSymtabExport ? 16 : 0);
        var strtab = includeSymtabExport ? BuildStringTable("_foo") : [0x00];
        var segmentVmaddr = includeExportTrie ? 0x1000UL : 0x40UL;
        var symbolVmaddr = includeExportTrie ? 0x1040UL : 0x40UL;
        var exportTrie = includeExportTrie ? BuildSingleExportTrie("_foo", 0x40) : [];
        var exportTrieOffset = strtabOffset + strtab.Length;
        var file = new byte[exportTrieOffset + exportTrie.Length];

        WriteMachHeader(file, cpuType, includeExportTrie ? (includePageZero ? 4u : 3u) : (includePageZero ? 3u : 2u), loadCommandsSize);

        var commandOffset = HeaderSize;
        if (includePageZero)
        {
            WriteSegment64(file, commandOffset, "__PAGEZERO", 0, 0x1000UL, 0, 0, []);
            commandOffset += 72;
        }

        WriteSegment64(file, commandOffset, "__TEXT", segmentVmaddr, 0x100UL, 0, 0x100UL, [
            new SectionSpec("__text", "__TEXT", symbolVmaddr, 4, textOffset, 0x80000400u, 0, 0),
        ]);

        commandOffset += Segment64SizeWithOneSection;
        WriteSymtabCommand(file, commandOffset, symtabOffset, includeSymtabExport ? 1u : 0u, strtabOffset, strtab.Length);
        commandOffset += SymtabCommandSize;

        if (includeExportTrie)
            WriteDyldInfoCommand(file, commandOffset, exportTrieOffset, exportTrie.Length);

        file[textOffset] = 0xC3;
        if (includeSymtabExport)
            BuildDefinedExternalSymbol(1, 1, symbolVmaddr).CopyTo(file, symtabOffset);
        strtab.CopyTo(file, strtabOffset);
        exportTrie.CopyTo(file, exportTrieOffset);

        return file;
    }

    private static byte[] BuildSelfCallerWithExportTrie(int cpuType)
    {
        var rel32 = unchecked((int)(0x1050UL - (0x1040UL + 5)));
        var rel32Bytes = BitConverter.GetBytes(rel32);
        var text = new byte[0x11];
        text[0] = 0xE8;
        Array.Copy(rel32Bytes, 0, text, 1, rel32Bytes.Length);
        text[^1] = 0xC3;

        var loadCommandsSize = Segment64SizeWithOneSection + SymtabCommandSize + DyldInfoCommandSize;
        const int textOffset = 0x200;
        const int symtabOffset = 0x280;
        const int strtabOffset = 0x280;
        var strtab = new byte[] { 0x00 };
        var exportTrie = BuildSingleExportTrie("_foo", 0x50);
        var exportTrieOffset = strtabOffset + strtab.Length;
        var file = new byte[exportTrieOffset + exportTrie.Length];

        WriteMachHeader(file, cpuType, 3u, loadCommandsSize);
        var commandOffset = HeaderSize;
        WriteSegment64(file, commandOffset, "__TEXT", 0x1000UL, 0x100UL, 0, 0x100UL, [
            new SectionSpec("__text", "__TEXT", 0x1040UL, (ulong)text.Length, textOffset, 0x80000400u, 0, 0),
        ]);

        commandOffset += Segment64SizeWithOneSection;
        WriteSymtabCommand(file, commandOffset, symtabOffset, 0, strtabOffset, strtab.Length);
        commandOffset += SymtabCommandSize;
        WriteDyldInfoCommand(file, commandOffset, exportTrieOffset, exportTrie.Length);

        text.CopyTo(file, textOffset);
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

    private static byte[] BuildDefinedExternalSymbol(uint stringOffset, byte sectionIndex, ulong value)
    {
        var entry = new byte[16];
        WriteLe32(entry, 0, stringOffset);
        entry[4] = 0x0F;
        entry[5] = sectionIndex;
        WriteLe64(entry, 8, value);
        return entry;
    }

    private static void WriteMachHeader(byte[] file, int cpuType, uint ncmds, int sizeofcmds)
    {
        WriteLe32(file, 0, 0xFEEDFACF);
        WriteLe32(file, 4, (uint)cpuType);
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
