using System.Buffers.Binary;

namespace DotnetNativeMcp.Core.Imaging;

/// <summary>
/// Resolves ELF PLT entries to imported symbol names by parsing <c>.rela.plt</c>,
/// <c>.dynsym</c>, and <c>.plt</c> / <c>.plt.sec</c> sections.
/// </summary>
public static partial class ElfReader
{
    private const uint SHT_RELA = 4;

    /// <summary>
    /// Builds a map of PLT entry virtual address → imported symbol name for <paramref name="image"/>.
    /// Returns an empty dictionary for non-ELF images or images with no PLT.
    /// </summary>
    public static IReadOnlyDictionary<ulong, string> ResolvePltEntries(NativeImage image)
    {
        if (image.Format != BinaryFormat.Elf)
            return new Dictionary<ulong, string>();

        try
        {
            return ResolvePltEntriesCore(image.RawBytes.Span);
        }
        catch
        {
            return new Dictionary<ulong, string>();
        }
    }

    private static Dictionary<ulong, string> ResolvePltEntriesCore(ReadOnlySpan<byte> bytes)
    {
        if (!TryReadElfHeader(bytes, out var is64, out var shOff, out var shEntSize, out var shNum, out var shStrNdx))
            return new Dictionary<ulong, string>();

        if (shStrNdx >= shNum)
            return new Dictionary<ulong, string>();

        var (nameTabOff, nameTabSize) = ReadSectionHeader(bytes, is64, shOff, shEntSize, shStrNdx);
        if (nameTabOff == 0 || nameTabOff + nameTabSize > (ulong)bytes.Length)
            return new Dictionary<ulong, string>();

        var shStrtab = bytes[(int)nameTabOff..(int)(nameTabOff + nameTabSize)];

        int relaPltIdx = -1, pltIdx = -1, pltSecIdx = -1, dynsymIdx = -1;
        for (ushort i = 0; i < shNum; i++)
        {
            var shType = ReadSectionType(bytes, is64, shOff, shEntSize, i);
            var nameIdx = ReadSectionNameIndex(bytes, is64, shOff, shEntSize, i);
            var name = ReadCString(shStrtab, (int)nameIdx);

            if (shType == SHT_RELA && name == ".rela.plt")
                relaPltIdx = i;
            else if (name == ".plt")
                pltIdx = i;
            else if (name == ".plt.sec")
                pltSecIdx = i;

            if (shType == SHT_DYNSYM)
                dynsymIdx = i;
        }

        if (relaPltIdx < 0 || dynsymIdx < 0)
            return new Dictionary<ulong, string>();

        // Build symbol index → name from .dynsym
        var symNames = ReadDynsymSymbolNames(bytes, is64, shOff, shEntSize, shNum, (ushort)dynsymIdx);

        // Determine PLT base VA and per-entry size.
        // .plt.sec (IBT-enabled) entries start at index 0; each is 16 bytes.
        // .plt without .plt.sec: entry 0 is the resolver, function stubs start at index 1.
        ulong pltBaseVa;
        const int pltEntrySize = 16;
        if (pltSecIdx >= 0)
        {
            pltBaseVa = ReadSectionVAddr(bytes, is64, shOff, shEntSize, (ushort)pltSecIdx);
        }
        else if (pltIdx >= 0)
        {
            // Skip the resolver stub (entry 0).
            pltBaseVa = ReadSectionVAddr(bytes, is64, shOff, shEntSize, (ushort)pltIdx) + pltEntrySize;
        }
        else
        {
            return new Dictionary<ulong, string>();
        }

        if (pltBaseVa == 0)
            return new Dictionary<ulong, string>();

        // Parse .rela.plt entries: each is 24 bytes (Elf64_Rela) or 12 bytes (Elf32_Rel).
        var (relaPltOff, relaPltSize) = ReadSectionHeader(bytes, is64, shOff, shEntSize, (ushort)relaPltIdx);
        if (relaPltOff == 0 || relaPltOff + relaPltSize > (ulong)bytes.Length)
            return new Dictionary<ulong, string>();

        var relaData = bytes[(int)relaPltOff..(int)(relaPltOff + relaPltSize)];
        var relaEntrySize = is64 ? 24 : 12;
        var entryCount = relaData.Length / relaEntrySize;

        var result = new Dictionary<ulong, string>(entryCount);
        for (var i = 0; i < entryCount; i++)
        {
            var entry = relaData.Slice(i * relaEntrySize, relaEntrySize);
            uint symIdx;
            if (is64)
            {
                var info = BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]);
                symIdx = (uint)(info >> 32);
            }
            else
            {
                var info = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
                symIdx = info >> 8;
            }

            if (!symNames.TryGetValue(symIdx, out var symName) || string.IsNullOrEmpty(symName))
                continue;

            var pltEntryVa = pltBaseVa + (ulong)(i * pltEntrySize);
            result[pltEntryVa] = symName;
        }

        return result;
    }

    private static Dictionary<uint, string> ReadDynsymSymbolNames(
        ReadOnlySpan<byte> bytes,
        bool is64,
        ulong shOff,
        ushort shEntSize,
        ushort shNum,
        ushort dynsymIdx)
    {
        var result = new Dictionary<uint, string>();

        var (symOff, symSize) = ReadSectionHeader(bytes, is64, shOff, shEntSize, dynsymIdx);
        var strLink = ReadSectionLink(bytes, is64, shOff, shEntSize, dynsymIdx);
        if (symOff == 0 || symSize == 0 || symOff + symSize > (ulong)bytes.Length || strLink >= shNum)
            return result;

        var (strOff, strSize) = ReadSectionHeader(bytes, is64, shOff, shEntSize, (ushort)strLink);
        if (strOff == 0 || strOff + strSize > (ulong)bytes.Length)
            return result;

        var strTab = bytes[(int)strOff..(int)(strOff + strSize)];
        var symEntrySize = is64 ? 24 : 16;
        var symData = bytes[(int)symOff..(int)(symOff + symSize)];
        var count = symData.Length / symEntrySize;

        for (var i = 0; i < count; i++)
        {
            var entry = symData.Slice(i * symEntrySize, symEntrySize);
            uint nameIndex = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            var name = ReadCString(strTab, (int)nameIndex);
            if (!string.IsNullOrEmpty(name))
                result[(uint)i] = name;
        }

        return result;
    }
}
