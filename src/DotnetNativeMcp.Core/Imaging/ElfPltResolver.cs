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
            return ResolvePltEntriesCore(image.RawBytes.Span, image.Architecture);
        }
        catch
        {
            return new Dictionary<ulong, string>();
        }
    }

    private static Dictionary<ulong, string> ResolvePltEntriesCore(ReadOnlySpan<byte> bytes, Architecture arch)
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

        // Parse .rela.plt entries: each is 24 bytes (Elf64_Rela) or 12 bytes (Elf32_Rel).
        var (relaPltOff, relaPltSize) = ReadSectionHeader(bytes, is64, shOff, shEntSize, (ushort)relaPltIdx);
        if (relaPltOff == 0 || relaPltOff + relaPltSize > (ulong)bytes.Length)
            return new Dictionary<ulong, string>();

        var relaData = bytes[(int)relaPltOff..(int)(relaPltOff + relaPltSize)];
        var relaEntrySize = is64 ? 24 : 12;
        var entryCount = relaData.Length / relaEntrySize;

        // Determine PLT base VA and per-entry stride.
        // .plt.sec (IBT-enabled, x86 only) entries start at index 0; each is 16 bytes.
        // .plt without .plt.sec: a resolver stub (PLT0) precedes the function entries. Its size
        // is architecture-specific — x86-64 emits a 16-byte PLT0, AArch64 a 32-byte PLT0.
        // PLTn function entries are 16 bytes on x86-64 and on default AArch64, but AArch64 BTI/PAC
        // hardening widens them to 24 bytes. We detect that by inspecting the first function
        // entry's opcode (a `BTI` landing pad), which is immune to TLSDESC/IRELATIVE relocation
        // accounting; everything else uses the canonical 16-byte stride.
        ulong pltBaseVa;
        const int pltEntrySize = 16;
        var pltHeaderSize = arch == Architecture.Arm64 ? 32 : pltEntrySize;
        var pltStride = pltEntrySize;
        if (pltSecIdx >= 0)
        {
            pltBaseVa = ReadSectionVAddr(bytes, is64, shOff, shEntSize, (ushort)pltSecIdx);
        }
        else if (pltIdx >= 0)
        {
            var pltSectionVa = ReadSectionVAddr(bytes, is64, shOff, shEntSize, (ushort)pltIdx);
            // Skip the resolver stub (PLT0 header).
            pltBaseVa = pltSectionVa + (ulong)pltHeaderSize;

            if (arch == Architecture.Arm64)
            {
                var (pltOff, pltSize) = ReadSectionHeader(bytes, is64, shOff, shEntSize, (ushort)pltIdx);
                pltStride = DeriveArm64PltStride(bytes, (long)pltOff, (long)pltSize, pltHeaderSize);
            }
        }
        else
        {
            return new Dictionary<ulong, string>();
        }

        if (pltBaseVa == 0)
            return new Dictionary<ulong, string>();

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

            var pltEntryVa = pltBaseVa + (ulong)(i * pltStride);
            result[pltEntryVa] = symName;
        }

        return result;
    }

    /// <summary>
    /// Picks the AArch64 PLTn function-entry stride by inspecting the first function entry's
    /// opcode. BTI/PAC-hardened entries are 24 bytes and begin with a <c>BTI</c> landing pad
    /// (a HINT instruction in the <c>0xD503241F</c> family); the default 16-byte entry begins
    /// with <c>adrp x16, …</c>. Reading the opcode is immune to TLSDESC / IRELATIVE relocation
    /// accounting (those append stubs to <c>.plt</c> / <c>.iplt</c> that would otherwise skew a
    /// size-based derivation). Falls back to 16 when the first entry can't be read.
    /// </summary>
    private static int DeriveArm64PltStride(ReadOnlySpan<byte> bytes, long pltOffset, long pltSize, int headerSize)
    {
        const int defaultStride = 16;
        const int hardenedStride = 24;

        // Validate before any addition/cast so a malformed (huge or negative) sh_offset cannot
        // wrap past the bounds check. pltOffset/pltSize are long-casts of ulong section fields;
        // a value with the top bit set casts negative and is rejected here.
        if (pltOffset < 0 || headerSize < 0 || pltSize < (long)headerSize + 4
            || pltOffset > bytes.Length - headerSize - 4)
            return defaultStride;

        var firstEntryOffset = (int)(pltOffset + headerSize);
        var firstOpcode = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(firstEntryOffset, 4));

        // BTI is a HINT-space instruction. Masking off the CRm/op2 target field (bti / bti c /
        // bti j / bti jc all collapse to the same base) leaves 0xD503241F.
        const uint btiBase = 0xD503241F;
        const uint btiMask = 0xFFFFFF1F;
        return (firstOpcode & btiMask) == btiBase ? hardenedStride : defaultStride;
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
