using System.Buffers.Binary;
using System.Text;
using DotnetNativeMcp.Core.Identity;

namespace DotnetNativeMcp.Core.Imaging;

/// <summary>
/// Minimal Mach-O parser for 32-bit and 64-bit little-endian Mach-O binaries (macOS NativeAOT).
/// Fat (universal) binaries are rejected at load time — the caller must slice the desired
/// architecture slice before calling <see cref="Read"/>. Big-endian Mach-O is not supported.
/// </summary>
public static partial class MachOReader
{
    // Mach-O magic numbers (read as LE uint32)
    internal const uint MachOMagic64Le = 0xFEEDFACF;
    internal const uint MachOMagic32Le = 0xFEEDFACE;
    internal const uint FatMagicBe = 0xBEBAFECA; // CA FE BA BE on disk, read LE = 0xBEBAFECA

    // Load command types
    private const uint LcSegment = 0x1;
    private const uint LcSegment64 = 0x19;
    private const uint LcSymtab = 0x2;

    // CPU types
    private const int CpuTypeX86_64 = 0x01000007;
    private const int CpuTypeArm64 = 0x0100000C;

    // nlist n_type masks
    private const byte NStab = 0xE0;
    private const byte NType = 0x0E;
    private const byte NUndf = 0x00;

    // Known NativeAOT marker symbols (without leading '_')
    private static readonly string[] NativeAotMarkers =
    [
        "RhpNewFast", "RhpAssignRef", "RhpNewArray",
        "RhEHEnum",
    ];

    /// <summary>Returns <c>true</c> if the bytes start with a 64-bit or 32-bit LE Mach-O magic.</summary>
    public static bool IsMachO(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4 &&
        BinaryPrimitives.ReadUInt32LittleEndian(bytes) is var m &&
        (m == MachOMagic64Le || m == MachOMagic32Le);

    /// <summary>Returns <c>true</c> if the bytes start with a fat (universal) binary magic.</summary>
    public static bool IsFatBinary(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4 &&
        BinaryPrimitives.ReadUInt32LittleEndian(bytes) == FatMagicBe;

    /// <summary>
    /// Parses a Mach-O binary from raw bytes and returns a <see cref="NativeImage"/>.
    /// Returns <c>null</c> if the bytes are not a supported Mach-O file.
    /// </summary>
    public static NativeImage? Read(ReadOnlyMemory<byte> rawBytes, string filePath)
    {
        var bytes = rawBytes.Span;
        if (!IsMachO(bytes)) return null;

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        var is64 = magic == MachOMagic64Le;

        // mach_header: magic(4)+cputype(4)+cpusubtype(4)+filetype(4)+ncmds(4)+sizeofcmds(4)+flags(4) = 28 bytes
        // mach_header_64: same + reserved(4) = 32 bytes
        var headerSize = is64 ? 32 : 28;
        if (bytes.Length < headerSize) return null;

        var cpuType = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        var ncmds = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]);

        var arch = cpuType switch
        {
            CpuTypeX86_64 => Architecture.X64,
            CpuTypeArm64 => Architecture.Arm64,
            _ => Architecture.Unknown,
        };

        var sections = new List<NativeSection>();

        // Pass 1: collect sections from segment load commands
        var cmdOffset = headerSize;
        for (var i = 0u; i < ncmds; i++)
        {
            if (cmdOffset + 8 > bytes.Length) break;
            var cmd = BinaryPrimitives.ReadUInt32LittleEndian(bytes[cmdOffset..]);
            var cmdsize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 4)..]);
            if (cmdsize < 8 || cmdOffset + cmdsize > (uint)bytes.Length) break;

            if (cmd == LcSegment64 && is64)
                ParseSegment64(bytes, cmdOffset, sections);
            else if (cmd == LcSegment && !is64)
                ParseSegment32(bytes, cmdOffset, sections);

            cmdOffset += (int)cmdsize;
        }

        // Pass 2: collect symbols (uses resolved sections for name lookup)
        var symbols = new List<NativeSymbol>();
        cmdOffset = headerSize;
        for (var i = 0u; i < ncmds; i++)
        {
            if (cmdOffset + 8 > bytes.Length) break;
            var cmd = BinaryPrimitives.ReadUInt32LittleEndian(bytes[cmdOffset..]);
            var cmdsize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 4)..]);
            if (cmdsize < 8 || cmdOffset + cmdsize > (uint)bytes.Length) break;

            if (cmd == LcSymtab)
                ReadSymtab(bytes, cmdOffset, is64, sections, symbols);

            cmdOffset += (int)cmdsize;
        }

        var buildIdHex = Identity.BuildId.Extract(bytes, filePath);
        var handle = ImageHandle.From(buildIdHex, filePath);
        return new NativeImage(handle, filePath, BinaryFormat.MachO, arch, sections, symbols, rawBytes, 0);
    }

    /// <summary>
    /// Returns <c>true</c> when the Mach-O binary appears to be a NativeAOT managed-native build.
    /// </summary>
    public static bool LooksLikeManagedNativeBuild(NativeImage image)
    {
        foreach (var sym in image.Symbols)
            foreach (var marker in NativeAotMarkers)
                if (sym.Name.Equals(marker, StringComparison.Ordinal))
                    return true;
        return false;
    }

    // segment_command_64:
    //   cmd(4)+cmdsize(4)+segname(16)+vmaddr(8)+vmsize(8)+fileoff(8)+filesize(8)+maxprot(4)+initprot(4)+nsects(4)+flags(4) = 72 bytes
    // section_64 (each 80 bytes):
    //   sectname(16)+segname(16)+addr(8)+size(8)+offset(4)+align(4)+reloff(4)+nreloc(4)+flags(4)+reserved1(4)+reserved2(4)+reserved3(4)
    private static void ParseSegment64(ReadOnlySpan<byte> bytes, int cmdOffset, List<NativeSection> sections)
    {
        if (cmdOffset + 72 > bytes.Length) return;
        var nsects = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 64)..]);
        for (var s = 0u; s < nsects; s++)
        {
            var sectBase = cmdOffset + 72 + (int)(s * 80);
            if (sectBase + 80 > bytes.Length) break;
            var sectName = ReadFixedString(bytes, sectBase, 16);
            var segName = ReadFixedString(bytes, sectBase + 16, 16);
            var addr = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(sectBase + 32)..]);
            var size = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(sectBase + 40)..]);
            var fileOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(sectBase + 48)..]);
            var displayName = string.IsNullOrEmpty(segName) ? sectName : $"{segName},{sectName}";
            sections.Add(new NativeSection(displayName, addr, size, fileOffset, size));
        }
    }

    // segment_command:
    //   cmd(4)+cmdsize(4)+segname(16)+vmaddr(4)+vmsize(4)+fileoff(4)+filesize(4)+maxprot(4)+initprot(4)+nsects(4)+flags(4) = 56 bytes
    // section (each 68 bytes):
    //   sectname(16)+segname(16)+addr(4)+size(4)+offset(4)+align(4)+reloff(4)+nreloc(4)+flags(4)+reserved1(4)+reserved2(4)
    private static void ParseSegment32(ReadOnlySpan<byte> bytes, int cmdOffset, List<NativeSection> sections)
    {
        if (cmdOffset + 56 > bytes.Length) return;
        var nsects = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 48)..]);
        for (var s = 0u; s < nsects; s++)
        {
            var sectBase = cmdOffset + 56 + (int)(s * 68);
            if (sectBase + 68 > bytes.Length) break;
            var sectName = ReadFixedString(bytes, sectBase, 16);
            var segName = ReadFixedString(bytes, sectBase + 16, 16);
            var addr = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(sectBase + 32)..]);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(sectBase + 36)..]);
            var fileOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(sectBase + 40)..]);
            var displayName = string.IsNullOrEmpty(segName) ? sectName : $"{segName},{sectName}";
            sections.Add(new NativeSection(displayName, addr, size, fileOffset, size));
        }
    }

    // symtab_command: cmd(4)+cmdsize(4)+symoff(4)+nsyms(4)+stroff(4)+strsize(4) = 24 bytes
    // nlist_64 (each 16 bytes): n_strx(4)+n_type(1)+n_sect(1)+n_desc(2)+n_value(8)
    // nlist    (each 12 bytes): n_strx(4)+n_type(1)+n_sect(1)+n_desc(2)+n_value(4)
    private static void ReadSymtab(
        ReadOnlySpan<byte> bytes, int cmdOffset, bool is64,
        List<NativeSection> sections, List<NativeSymbol> symbols)
    {
        if (cmdOffset + 24 > bytes.Length) return;
        var symoff = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 8)..]);
        var nsyms = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 12)..]);
        var stroff = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 16)..]);
        var strsize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 20)..]);

        var entrySize = is64 ? 16u : 12u;
        if (symoff + nsyms * entrySize > (uint)bytes.Length) return;
        if (stroff + strsize > (uint)bytes.Length) return;

        var strtab = bytes[(int)stroff..(int)(stroff + strsize)];

        for (var i = 0u; i < nsyms; i++)
        {
            var symBase = (int)symoff + (int)(i * entrySize);
            if (symBase + (int)entrySize > bytes.Length) break;

            var nStrx = BinaryPrimitives.ReadUInt32LittleEndian(bytes[symBase..]);
            var nType = bytes[symBase + 4];
            var nSect = bytes[symBase + 5];
            ulong nValue = is64
                ? BinaryPrimitives.ReadUInt64LittleEndian(bytes[(symBase + 8)..])
                : BinaryPrimitives.ReadUInt32LittleEndian(bytes[(symBase + 8)..]);

            // Skip STAB (debug) entries
            if ((nType & NStab) != 0) continue;
            // Skip undefined (imported) symbols — only emit defined
            if ((nType & NType) == NUndf) continue;

            if (nStrx >= (uint)strtab.Length) continue;
            var rawName = ReadCString(strtab, (int)nStrx);
            // macOS symbols carry a leading '_' — strip it
            var name = rawName.StartsWith('_') ? rawName[1..] : rawName;
            if (name.Length == 0) continue;

            string? sectionName = null;
            if (nSect > 0 && nSect <= sections.Count)
                sectionName = sections[nSect - 1].Name;

            symbols.Add(new NativeSymbol((int)i, name, name, nValue, 0, sectionName, true));
        }
    }

    private static string ReadFixedString(ReadOnlySpan<byte> bytes, int offset, int maxLen)
    {
        var end = offset;
        var limit = Math.Min(offset + maxLen, bytes.Length);
        while (end < limit && bytes[end] != 0) end++;
        return Encoding.ASCII.GetString(bytes[offset..end]);
    }

    private static string ReadCString(ReadOnlySpan<byte> data, int offset)
    {
        var end = offset;
        while (end < data.Length && data[end] != 0) end++;
        return Encoding.ASCII.GetString(data[offset..end]);
    }
}
