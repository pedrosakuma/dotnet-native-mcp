using System.Buffers.Binary;
using System.Text;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Symbols;

namespace DotnetNativeMcp.Core.Imaging;

/// <summary>
/// Minimal ELF parser for 32-bit and 64-bit little-endian ELF binaries.
/// Reads the section header table, .symtab/.dynsym symbol tables,
/// and .note.gnu.build-id. Big-endian ELF is not supported.
/// </summary>
public static class ElfReader
{
    // e_machine values
    private const ushort EM_386 = 0x03;
    private const ushort EM_X86_64 = 0x3E;
    private const ushort EM_AARCH64 = 0xB7;

    // ELF symbol type bits (st_info & 0xF)
    private const byte STT_FUNC = 2;

    // Known NativeAOT marker exports / symbols
    private static readonly string[] NativeAotExports =
    [
        "RhpNewFast", "RhpAssignRef", "RhpNewArray",
        "RhEHEnum", "__managedcode",
    ];

    /// <summary>
    /// Parses an ELF binary from raw bytes and returns a <see cref="NativeImage"/>.
    /// Returns <c>null</c> if the bytes are not a supported ELF file.
    /// </summary>
    public static NativeImage? Read(ReadOnlyMemory<byte> rawBytes, string filePath)
    {
        var bytes = rawBytes.Span;
        if (bytes.Length < 16) return null;
        if (bytes[0] != 0x7F || bytes[1] != (byte)'E' || bytes[2] != (byte)'L' || bytes[3] != (byte)'F')
            return null;
        // EI_CLASS
        var is64 = bytes[4] == 2;
        // EI_DATA: only LE
        if (bytes[5] != 1) return null;

        ushort eMachine;
        ulong shOff;
        ushort shEntSize, shNum, shStrNdx;
        ulong phOff;
        ushort phEntSize, phNum;

        if (is64)
        {
            if (bytes.Length < 64) return null;
            eMachine = BinaryPrimitives.ReadUInt16LittleEndian(bytes[18..]);
            phOff = BinaryPrimitives.ReadUInt64LittleEndian(bytes[32..]);
            shOff = BinaryPrimitives.ReadUInt64LittleEndian(bytes[40..]);
            phEntSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes[54..]);
            phNum = BinaryPrimitives.ReadUInt16LittleEndian(bytes[56..]);
            shEntSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes[58..]);
            shNum = BinaryPrimitives.ReadUInt16LittleEndian(bytes[60..]);
            shStrNdx = BinaryPrimitives.ReadUInt16LittleEndian(bytes[62..]);
        }
        else
        {
            if (bytes.Length < 52) return null;
            eMachine = BinaryPrimitives.ReadUInt16LittleEndian(bytes[18..]);
            phOff = BinaryPrimitives.ReadUInt32LittleEndian(bytes[28..]);
            shOff = BinaryPrimitives.ReadUInt32LittleEndian(bytes[32..]);
            phEntSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes[42..]);
            phNum = BinaryPrimitives.ReadUInt16LittleEndian(bytes[44..]);
            shEntSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes[46..]);
            shNum = BinaryPrimitives.ReadUInt16LittleEndian(bytes[48..]);
            shStrNdx = BinaryPrimitives.ReadUInt16LittleEndian(bytes[50..]);
        }

        var arch = eMachine switch
        {
            EM_X86_64 => Architecture.X64,
            EM_386 => Architecture.X86,
            EM_AARCH64 => Architecture.Arm64,
            _ => Architecture.Unknown,
        };

        if (shOff == 0 || shEntSize == 0 || shNum == 0) return null;

        // Read section name string table
        ReadOnlySpan<byte> shStrtab = default;
        if (shStrNdx != 0 && shStrNdx < shNum)
        {
            var (nameTabOff, nameTabSize) = ReadSectionHeader(bytes, is64, shOff, shEntSize, shStrNdx);
            if (nameTabOff > 0 && nameTabOff + nameTabSize <= (ulong)bytes.Length)
                shStrtab = bytes[(int)nameTabOff..(int)(nameTabOff + nameTabSize)];
        }

        // Enumerate sections
        var sections = new List<NativeSection>(shNum);
        int symtabIdx = -1, dynsymIdx = -1;

        for (var i = 0; i < shNum; i++)
        {
            var (off, size) = ReadSectionHeader(bytes, is64, shOff, shEntSize, (ushort)i);
            var nameIdx = ReadSectionNameIndex(bytes, is64, shOff, shEntSize, (ushort)i);
            var shType = ReadSectionType(bytes, is64, shOff, shEntSize, (ushort)i);
            var vaddr = ReadSectionVAddr(bytes, is64, shOff, shEntSize, (ushort)i);
            var name = shStrtab.IsEmpty ? string.Empty : ReadCString(shStrtab, (int)nameIdx);

            if (size > 0 && off > 0)
                sections.Add(new NativeSection(name, vaddr, size, off, size));

            // SHT_SYMTAB = 2, SHT_DYNSYM = 11
            if (shType == 2 && name == ".symtab") symtabIdx = i;
            else if (shType == 11) dynsymIdx = i;
        }

        // Read symbols from .symtab (preferred) or .dynsym
        var symbols = new List<NativeSymbol>();
        var symIdx = symtabIdx >= 0 ? symtabIdx : dynsymIdx;
        if (symIdx >= 0)
        {
            ReadSymtab(bytes, is64, shOff, shEntSize, (ushort)symIdx, shNum, symbols);
        }

        // Compute image base from first PT_LOAD segment
        var imageBase = ComputeImageBase(bytes, is64, phOff, phEntSize, phNum);

        var buildIdHex = Identity.BuildId.Extract(bytes, filePath);
        var handle = ImageHandle.From(buildIdHex, System.IO.Path.GetFileName(filePath));

        return new NativeImage(
            handle, filePath, BinaryFormat.Elf, arch,
            sections, symbols, rawBytes, imageBase);
    }

    /// <summary>
    /// Returns <c>true</c> when the ELF binary appears to be a NativeAOT or R2R managed-native build.
    /// Heuristic — false positives are acceptable; the goal is to reject arbitrary system libraries.
    /// </summary>
    public static bool LooksLikeManagedNativeBuild(NativeImage image)
    {
        foreach (var sym in image.Symbols)
        {
            if (LooksLikeNativeAotMangled(sym.Name)) return true;
            if (Array.IndexOf(NativeAotExports, sym.Name) >= 0) return true;
        }

        // Check for marker strings in section names
        foreach (var sec in image.Sections)
        {
            if (sec.Name is "__managedcode" or ".managed" or "hydrated") return true;
        }

        // Check raw bytes for known marker strings
        var markerBytes = "RhEHEnum"u8;
        if (image.RawBytes.Span.IndexOf(markerBytes) >= 0) return true;

        return false;
    }

    private static bool LooksLikeNativeAotMangled(string name) =>
        name.StartsWith("S_P_", StringComparison.Ordinal) ||
        (name.Contains("__", StringComparison.Ordinal) &&
         (name.StartsWith("Microsoft_", StringComparison.Ordinal) ||
          name.StartsWith("System_", StringComparison.Ordinal) ||
          name.StartsWith("Rhp", StringComparison.Ordinal) ||
          name.StartsWith("RhEH", StringComparison.Ordinal)));

    private static void ReadSymtab(
        ReadOnlySpan<byte> bytes,
        bool is64,
        ulong shOff,
        ushort shEntSize,
        ushort symSectionIdx,
        ushort shNum,
        List<NativeSymbol> symbols)
    {
        var (symOff, symSize) = ReadSectionHeader(bytes, is64, shOff, shEntSize, symSectionIdx);
        var symLink = ReadSectionLink(bytes, is64, shOff, shEntSize, symSectionIdx);

        ReadOnlySpan<byte> strTab = default;
        if (symLink < shNum)
        {
            var (strOff, strSize) = ReadSectionHeader(bytes, is64, shOff, shEntSize, (ushort)symLink);
            if (strOff + strSize <= (ulong)bytes.Length && strSize > 0)
                strTab = bytes[(int)strOff..(int)(strOff + strSize)];
        }

        var entrySize = is64 ? 24 : 16;
        var count = (int)(symSize / (ulong)entrySize);
        if (symOff + symSize > (ulong)bytes.Length) return;
        var symData = bytes[(int)symOff..(int)(symOff + symSize)];

        for (var i = 0; i < count; i++)
        {
            var entry = symData[(i * entrySize)..];
            string symName;
            ulong symValue;
            ulong symSize2;
            byte stInfo;

            if (is64)
            {
                var nameIdx = BinaryPrimitives.ReadUInt32LittleEndian(entry[0..]);
                stInfo = entry[4];
                symValue = BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]);
                symSize2 = BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]);
                symName = strTab.IsEmpty ? string.Empty : ReadCString(strTab, (int)nameIdx);
            }
            else
            {
                var nameIdx = BinaryPrimitives.ReadUInt32LittleEndian(entry[0..]);
                symValue = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
                symSize2 = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
                stInfo = entry[12];
                symName = strTab.IsEmpty ? string.Empty : ReadCString(strTab, (int)nameIdx);
            }

            if (string.IsNullOrEmpty(symName)) continue;
            var isFunc = (stInfo & 0xF) == STT_FUNC;
            var demangled = NativeAotSymbolDemangler.Demangle(symName);
            symbols.Add(new NativeSymbol(i, symName, demangled, symValue, symSize2, null, isFunc));
        }
    }

    private static ulong ComputeImageBase(ReadOnlySpan<byte> bytes, bool is64, ulong phOff, ushort phEntSize, ushort phNum)
    {
        // PT_LOAD = 1
        if (phOff == 0 || phEntSize == 0 || phNum == 0) return 0;
        for (var i = 0; i < phNum; i++)
        {
            var hdrStart = phOff + (ulong)(i * phEntSize);
            if (is64)
            {
                if (hdrStart + 56 > (ulong)bytes.Length) break;
                var ph = bytes[(int)hdrStart..];
                var pType = BinaryPrimitives.ReadUInt32LittleEndian(ph[0..]);
                if (pType == 1) // PT_LOAD
                    return BinaryPrimitives.ReadUInt64LittleEndian(ph[16..]); // p_vaddr
            }
            else
            {
                if (hdrStart + 32 > (ulong)bytes.Length) break;
                var ph = bytes[(int)hdrStart..];
                var pType = BinaryPrimitives.ReadUInt32LittleEndian(ph[0..]);
                if (pType == 1)
                    return BinaryPrimitives.ReadUInt32LittleEndian(ph[8..]); // p_vaddr
            }
        }
        return 0;
    }

    private static (ulong offset, ulong size) ReadSectionHeader(
        ReadOnlySpan<byte> bytes, bool is64, ulong shOff, ushort shEntSize, ushort index)
    {
        var start = shOff + (ulong)(index * shEntSize);
        if (is64)
        {
            if (start + 64 > (ulong)bytes.Length) return (0, 0);
            var sh = bytes[(int)start..];
            return (BinaryPrimitives.ReadUInt64LittleEndian(sh[24..]),
                    BinaryPrimitives.ReadUInt64LittleEndian(sh[32..]));
        }
        else
        {
            if (start + 40 > (ulong)bytes.Length) return (0, 0);
            var sh = bytes[(int)start..];
            return (BinaryPrimitives.ReadUInt32LittleEndian(sh[16..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(sh[20..]));
        }
    }

    private static uint ReadSectionNameIndex(
        ReadOnlySpan<byte> bytes, bool is64, ulong shOff, ushort shEntSize, ushort index)
    {
        var start = shOff + (ulong)(index * shEntSize);
        if (start + 4 > (ulong)bytes.Length) return 0;
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes[(int)start..]);
    }

    private static uint ReadSectionType(
        ReadOnlySpan<byte> bytes, bool is64, ulong shOff, ushort shEntSize, ushort index)
    {
        var start = shOff + (ulong)(index * shEntSize);
        if (start + 8 > (ulong)bytes.Length) return 0;
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes[((int)start + 4)..]);
    }

    private static ulong ReadSectionVAddr(
        ReadOnlySpan<byte> bytes, bool is64, ulong shOff, ushort shEntSize, ushort index)
    {
        var start = shOff + (ulong)(index * shEntSize);
        if (is64)
        {
            if (start + 24 > (ulong)bytes.Length) return 0;
            return BinaryPrimitives.ReadUInt64LittleEndian(bytes[((int)start + 16)..]);
        }
        else
        {
            if (start + 16 > (ulong)bytes.Length) return 0;
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes[((int)start + 12)..]);
        }
    }

    private static uint ReadSectionLink(
        ReadOnlySpan<byte> bytes, bool is64, ulong shOff, ushort shEntSize, ushort index)
    {
        var start = shOff + (ulong)(index * shEntSize);
        if (is64)
        {
            if (start + 44 > (ulong)bytes.Length) return 0;
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes[((int)start + 40)..]);
        }
        else
        {
            if (start + 28 > (ulong)bytes.Length) return 0;
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes[((int)start + 24)..]);
        }
    }

    private static string ReadCString(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset >= data.Length) return string.Empty;
        var end = offset;
        while (end < data.Length && data[end] != 0) end++;
        return Encoding.UTF8.GetString(data[offset..end]);
    }
}
