using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace DotnetNativeMcp.Core.Imaging;

/// <summary>
/// Lazily-built Mach-O metadata used by cross-image xref resolution.
/// </summary>
/// <param name="StubTargets">Maps stub virtual addresses to imported symbol names.</param>
/// <param name="Exports">Maps exported symbol names to defined virtual addresses.</param>
public sealed record MachOCrossImageMetadata(
    IReadOnlyDictionary<ulong, string> StubTargets,
    IReadOnlyDictionary<string, ulong> Exports)
{
    public static MachOCrossImageMetadata Empty { get; } = new(
        new ReadOnlyDictionary<ulong, string>(new Dictionary<ulong, string>()),
        new ReadOnlyDictionary<string, ulong>(new Dictionary<string, ulong>(StringComparer.Ordinal)));
}

public static partial class MachOReader
{
    private const uint LcDysymtab = 0xB;
    private const uint LcDyldInfo = 0x22;
    private const uint LcDyldInfoOnly = 0x80000022;
    private const uint SectionTypeMask = 0xFF;
    private const uint SectionTypeSymbolStubs = 0x8;
    private const uint IndirectSymbolLocal = 0x80000000;
    private const uint IndirectSymbolAbsolute = 0x40000000;
    private const byte NExt = 0x01;
    private const ulong ExportSymbolFlagsReexport = 0x08;
    private const ulong ExportSymbolFlagsStubAndResolver = 0x10;

    public static IReadOnlyDictionary<ulong, string> ResolveStubEntries(NativeImage image) =>
        BuildCrossImageMetadata(image).StubTargets;

    public static IReadOnlyDictionary<string, ulong> ReadExports(NativeImage image) =>
        BuildCrossImageMetadata(image).Exports;

    internal static MachOCrossImageMetadata BuildCrossImageMetadata(NativeImage image)
    {
        if (image.Format != BinaryFormat.MachO)
            return MachOCrossImageMetadata.Empty;

        try
        {
            var bytes = image.RawBytes.Span;
            if (bytes.Length < 32 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != MachOMagic64Le)
                return MachOCrossImageMetadata.Empty;

            var ncmds = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]);
            const int headerSize = 32;

            SymtabCommand? symtab = null;
            DysymtabCommand? dysymtab = null;
            DyldInfoCommand? dyldInfo = null;
            ulong? preferredLoadAddress = null;
            var sections = new List<Section64Info>();

            var cmdOffset = headerSize;
            for (var i = 0u; i < ncmds; i++)
            {
                if (cmdOffset + 8 > bytes.Length)
                    break;

                var cmd = BinaryPrimitives.ReadUInt32LittleEndian(bytes[cmdOffset..]);
                var cmdSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 4)..]);
                if (cmdSize < 8 || cmdOffset + cmdSize > (uint)bytes.Length)
                    break;

                switch (cmd)
                {
                    case LcSegment64:
                        preferredLoadAddress = ParseSectionInfos(bytes, cmdOffset, sections, preferredLoadAddress);
                        break;
                    case LcSymtab when cmdOffset + 24 <= bytes.Length:
                        symtab = new SymtabCommand(
                            BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 8)..]),
                            BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 12)..]),
                            BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 16)..]),
                            BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 20)..]));
                        break;
                    case LcDysymtab when cmdOffset + 80 <= bytes.Length:
                        dysymtab = new DysymtabCommand(
                            BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 56)..]),
                            BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 60)..]));
                        break;
                    case LcDyldInfo:
                    case LcDyldInfoOnly when cmdOffset + 48 <= bytes.Length:
                        dyldInfo = new DyldInfoCommand(
                            BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 40)..]),
                            BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 44)..]));
                        break;
                }

                cmdOffset += (int)cmdSize;
            }

            if (symtab is null)
                return MachOCrossImageMetadata.Empty;

            var symbols = ReadSymtabEntries(bytes, symtab.Value);
            var stubTargets = ResolveStubTargets(bytes, sections, symbols, dysymtab);
            var exports = ReadExports(bytes, symbols, dyldInfo, preferredLoadAddress ?? 0UL);

            return new MachOCrossImageMetadata(
                new ReadOnlyDictionary<ulong, string>(stubTargets),
                new ReadOnlyDictionary<string, ulong>(exports));
        }
        catch
        {
            return MachOCrossImageMetadata.Empty;
        }
    }

    private static Dictionary<ulong, string> ResolveStubTargets(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<Section64Info> sections,
        IReadOnlyList<SymtabEntry> symbols,
        DysymtabCommand? dysymtab)
    {
        var result = new Dictionary<ulong, string>();
        if (dysymtab is null || dysymtab.Value.NIndirectSyms == 0)
            return result;

        var tableByteCount = (ulong)dysymtab.Value.NIndirectSyms * sizeof(uint);
        if ((ulong)dysymtab.Value.IndirectSymOff + tableByteCount > (ulong)bytes.Length)
            return result;

        var indirectSymbols = new uint[dysymtab.Value.NIndirectSyms];
        for (var i = 0; i < indirectSymbols.Length; i++)
        {
            var indirectSymbolOffset = (int)dysymtab.Value.IndirectSymOff + (i * sizeof(uint));
            indirectSymbols[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes[indirectSymbolOffset..]);
        }

        foreach (var section in sections)
        {
            if ((section.Flags & SectionTypeMask) != SectionTypeSymbolStubs || section.Reserved2 == 0)
                continue;

            var slotCount = (int)(section.Size / section.Reserved2);
            for (var slot = 0; slot < slotCount; slot++)
            {
                var indirectIndex = section.Reserved1 + slot;
                if (indirectIndex < 0 || indirectIndex >= indirectSymbols.Length)
                    break;

                var symbolIndex = indirectSymbols[indirectIndex];
                if ((symbolIndex & (IndirectSymbolLocal | IndirectSymbolAbsolute)) != 0 || symbolIndex >= symbols.Count)
                    continue;

                var symbol = symbols[(int)symbolIndex];
                if (!symbol.IsUndefined || string.IsNullOrEmpty(symbol.Name))
                    continue;

                result[section.Address + ((ulong)slot * section.Reserved2)] = symbol.Name;
            }
        }

        return result;
    }

    private static Dictionary<string, ulong> ReadExports(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<SymtabEntry> symbols,
        DyldInfoCommand? dyldInfo,
        ulong preferredLoadAddress)
    {
        if (dyldInfo is { ExportSize: > 0 } &&
            (ulong)dyldInfo.Value.ExportOff + dyldInfo.Value.ExportSize <= (ulong)bytes.Length)
        {
            var exportTrie = bytes.Slice((int)dyldInfo.Value.ExportOff, (int)dyldInfo.Value.ExportSize);
            var trieExports = ParseExportTrie(exportTrie, preferredLoadAddress);
            if (trieExports.Count > 0)
                return trieExports;
        }

        var exports = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (symbol.IsUndefined || !symbol.IsExternal || string.IsNullOrEmpty(symbol.Name))
                continue;

            exports[symbol.Name] = symbol.Value;
        }

        return exports;
    }

    private static List<SymtabEntry> ReadSymtabEntries(ReadOnlySpan<byte> bytes, SymtabCommand symtab)
    {
        const uint entrySize = 16;
        var byteCount = (ulong)symtab.NSyms * entrySize;
        if ((ulong)symtab.SymOff + byteCount > (ulong)bytes.Length ||
            (ulong)symtab.StrOff + symtab.StrSize > (ulong)bytes.Length)
        {
            return [];
        }

        var strtab = bytes.Slice((int)symtab.StrOff, (int)symtab.StrSize);
        var entries = new List<SymtabEntry>((int)symtab.NSyms);
        for (var i = 0u; i < symtab.NSyms; i++)
        {
            var symBase = (int)symtab.SymOff + (int)(i * entrySize);
            var nStrx = BinaryPrimitives.ReadUInt32LittleEndian(bytes[symBase..]);
            var nType = bytes[symBase + 4];
            var nDesc = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(symBase + 6)..]);
            var nValue = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(symBase + 8)..]);

            string? name = null;
            if (nStrx < (uint)strtab.Length)
            {
                var rawName = ReadCString(strtab, (int)nStrx);
                name = NormalizeSymbolName(rawName);
            }

            entries.Add(new SymtabEntry(
                name,
                nType,
                nDesc,
                nValue,
                (nType & NType) == NUndf,
                (nType & NExt) != 0));
        }

        return entries;
    }

    private static Dictionary<string, ulong> ParseExportTrie(ReadOnlySpan<byte> trie, ulong preferredLoadAddress)
    {
        var exports = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var visited = new HashSet<int>();
        WalkExportTrieNode(trie, 0, string.Empty, exports, visited, preferredLoadAddress);
        return exports;
    }

    private static void WalkExportTrieNode(
        ReadOnlySpan<byte> trie,
        int nodeOffset,
        string prefix,
        Dictionary<string, ulong> exports,
        HashSet<int> visited,
        ulong preferredLoadAddress)
    {
        if (nodeOffset < 0 || nodeOffset >= trie.Length || !visited.Add(nodeOffset))
            return;

        var cursor = nodeOffset;
        if (!TryReadUleb128(trie, ref cursor, out var terminalSize))
            return;

        var terminalEnd = cursor + (int)terminalSize;
        if (terminalEnd > trie.Length)
            return;

        if (terminalSize > 0)
        {
            var terminalCursor = cursor;
            if (!TryReadUleb128(trie, ref terminalCursor, out var flags))
                return;

            if ((flags & ExportSymbolFlagsReexport) == 0 &&
                TryReadUleb128(trie, ref terminalCursor, out var address))
            {
                var name = NormalizeSymbolName(prefix);
                if (!string.IsNullOrEmpty(name))
                    exports[name] = preferredLoadAddress + address;

                if ((flags & ExportSymbolFlagsStubAndResolver) != 0)
                    TryReadUleb128(trie, ref terminalCursor, out _);
            }
        }

        cursor = terminalEnd;
        if (cursor >= trie.Length)
            return;

        var childCount = trie[cursor++];
        for (var i = 0; i < childCount; i++)
        {
            var edgeName = ReadCString(trie, cursor);
            cursor += edgeName.Length + 1;
            if (cursor > trie.Length || !TryReadUleb128(trie, ref cursor, out var childOffset))
                return;

            if (childOffset < (ulong)trie.Length)
                WalkExportTrieNode(trie, (int)childOffset, prefix + edgeName, exports, visited, preferredLoadAddress);
        }
    }

    private static ulong? ParseSectionInfos(
        ReadOnlySpan<byte> bytes,
        int cmdOffset,
        List<Section64Info> sections,
        ulong? currentPreferredLoadAddress)
    {
        if (cmdOffset + 72 > bytes.Length)
            return currentPreferredLoadAddress;

        var segmentName = ReadFixedString(bytes, cmdOffset + 8, 16);
        var segmentVmaddr = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(cmdOffset + 24)..]);
        var segmentFileSize = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(cmdOffset + 48)..]);
        if (string.Equals(segmentName, "__TEXT", StringComparison.Ordinal))
        {
            currentPreferredLoadAddress = segmentVmaddr;
        }
        else if (currentPreferredLoadAddress is null && segmentFileSize != 0)
        {
            currentPreferredLoadAddress = segmentVmaddr;
        }

        var nsects = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 64)..]);
        for (var i = 0u; i < nsects; i++)
        {
            var sectionBase = cmdOffset + 72 + (int)(i * 80);
            if (sectionBase + 80 > bytes.Length)
                break;

            sections.Add(new Section64Info(
                ReadFixedString(bytes, sectionBase, 16),
                ReadFixedString(bytes, sectionBase + 16, 16),
                BinaryPrimitives.ReadUInt64LittleEndian(bytes[(sectionBase + 32)..]),
                BinaryPrimitives.ReadUInt64LittleEndian(bytes[(sectionBase + 40)..]),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[(sectionBase + 64)..]),
                (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[(sectionBase + 68)..]),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[(sectionBase + 72)..])));
        }

        return currentPreferredLoadAddress;
    }

    private static bool TryReadUleb128(ReadOnlySpan<byte> data, ref int offset, out ulong value)
    {
        value = 0;
        var shift = 0;
        while (offset < data.Length && shift < 64)
        {
            var b = data[offset++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return true;
            shift += 7;
        }

        value = 0;
        return false;
    }

    private static string? NormalizeSymbolName(string? rawName)
    {
        if (string.IsNullOrEmpty(rawName))
            return null;

        return rawName[0] == '_' ? rawName[1..] : rawName;
    }

    private readonly record struct SymtabCommand(uint SymOff, uint NSyms, uint StrOff, uint StrSize);
    private readonly record struct DysymtabCommand(uint IndirectSymOff, uint NIndirectSyms);
    private readonly record struct DyldInfoCommand(uint ExportOff, uint ExportSize);
    private readonly record struct Section64Info(string SectionName, string SegmentName, ulong Address, ulong Size, uint Flags, int Reserved1, ulong Reserved2);
    private readonly record struct SymtabEntry(string? Name, byte Type, ushort Desc, ulong Value, bool IsUndefined, bool IsExternal);
}
