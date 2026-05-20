using System.IO.Compression;
using DotnetNativeMcp.Core.Imaging;

namespace DotnetNativeMcp.Core.Symbols;

internal static class DwarfLineReader
{
    public readonly record struct LineRow(ulong Address, string File, int Line);

    public static IReadOnlyList<LineRow> Read(NativeImage image)
    {
        var section = image.Sections.FirstOrDefault(s => s.Name == ".debug_line");
        if (section is null || section.FileSize == 0) return [];
        var raw = image.GetSectionBytes(section).ToArray();
        // ELF SHF_COMPRESSED: Elf64_Chdr starts with ch_type==1 (ELFCOMPRESS_ZLIB).
        var data = TryDecompressElf(raw) ?? raw;
        var rows = new List<LineRow>();
        ParseSection(data, rows);
        rows.Sort((a, b) => a.Address.CompareTo(b.Address));
        return rows;
    }

    /// <summary>
    /// Detects and decompresses an SHF_COMPRESSED ELF section (ELFCOMPRESS_ZLIB only).
    /// Returns <c>null</c> if the data is not in that format (caller uses raw data).
    /// </summary>
    private static byte[]? TryDecompressElf(byte[] data)
    {
        // Elf64_Chdr: ch_type(4) + ch_reserved(4) + ch_size(8) + ch_addralign(8) = 24 bytes.
        if (data.Length < 24) return null;
        if (ReadU32(data, 0) != 1) return null; // ch_type must be 1 (ELFCOMPRESS_ZLIB)
        if (ReadU32(data, 4) != 0) return null; // ch_reserved must be 0
        var uncompressedSize = (int)ReadU64(data, 8);
        if (uncompressedSize <= 0 || uncompressedSize > 256 * 1024 * 1024) return null;
        try
        {
            using var compressed = new MemoryStream(data, 24, data.Length - 24);
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            var output = new byte[uncompressedSize];
            int total = 0;
            while (total < uncompressedSize)
            {
                int read = zlib.Read(output, total, uncompressedSize - total);
                if (read == 0) return null; // truncated: declared ch_size > actual stream
                total += read;
            }
            // Reject streams that inflate past the declared ch_size (malformed / hostile input).
            if (zlib.ReadByte() != -1) return null;
            return output;
        }
        catch { return null; }
    }

    public static LineRow? FindRow(IReadOnlyList<LineRow> rows, ulong address)
    {
        if (rows.Count == 0) return null;
        int lo = 0, hi = rows.Count - 1;
        while (lo < hi)
        {
            int mid = lo + (hi - lo + 1) / 2;
            if (rows[mid].Address <= address) lo = mid;
            else hi = mid - 1;
        }
        if (rows[lo].Address > address) return null;
        return rows[lo];
    }

    private static void ParseSection(byte[] data, List<LineRow> rows)
    {
        int offset = 0;
        while (offset < data.Length)
            if (!TryParseCompilationUnit(data, ref offset, rows)) return;
    }

    private static bool TryParseCompilationUnit(byte[] data, ref int offset, List<LineRow> rows)
    {
        try
        {
        if (offset + 4 > data.Length) return false;
        uint unitLength = ReadU32(data, offset); offset += 4;
        if (unitLength == 0xFFFFFFFF) { offset += 8; return false; }
        // Use long arithmetic to prevent signed-integer overflow when unitLength is large.
        long unitEndL = (long)offset + unitLength;
        if (unitEndL > data.Length) return false;
        int unitEnd = (int)unitEndL;
        if (offset + 2 > unitEnd) { offset = unitEnd; return true; }
        ushort version = ReadU16(data, offset); offset += 2;
        if (version < 2 || version > 4) { offset = unitEnd; return true; }
        if (offset + 4 > unitEnd) { offset = unitEnd; return true; }
        uint headerLengthU = ReadU32(data, offset); offset += 4;
        // Use long arithmetic to prevent overflow when headerLength is large.
        long programStartL = (long)offset + headerLengthU;
        if (programStartL > unitEnd || programStartL > data.Length) { offset = unitEnd; return true; }
        int programStart = (int)programStartL;
        byte minimumInstructionLength = data[offset++];
        if (version >= 4) offset++;
        bool defaultIsStmt = data[offset++] != 0;
        int lineBase = (sbyte)data[offset++];
        byte lineRange = data[offset++];
        // DWARF spec says line_range must not be zero (it's a divisor for special opcodes).
        if (lineRange == 0) { offset = unitEnd; return true; }
        byte opcodeBase = data[offset++];
        if (opcodeBase == 0) { offset = unitEnd; return true; }
        int[] stdOpcodeLengths = new int[opcodeBase];
        for (int i = 1; i < opcodeBase && offset < programStart; i++)
            stdOpcodeLengths[i] = (int)ReadULEB128(data, ref offset);
        var directories = new List<string> { string.Empty };
        while (offset < programStart && data[offset] != 0) directories.Add(ReadNTS(data, ref offset));
        if (offset < programStart) offset++;
        var fileNames = new List<(string Name, int DirIdx)> { (string.Empty, 0) };
        while (offset < programStart && data[offset] != 0)
        {
            string name = ReadNTS(data, ref offset);
            int dirIdx = (int)ReadULEB128(data, ref offset);
            ReadULEB128(data, ref offset);
            ReadULEB128(data, ref offset);
            fileNames.Add((name, dirIdx));
        }
        if (offset < programStart) offset++;
        offset = programStart;
        ulong address = 0; int fileIdx = 1, line = 1; bool isStmt = defaultIsStmt;
        while (offset < unitEnd)
        {
            byte opcode = data[offset++];
            if (opcode == 0)
            {
                int extLen = (int)ReadULEB128(data, ref offset);
                int extEnd = offset + extLen;
                if (extEnd > unitEnd || extEnd > data.Length) break;
                byte extOpcode = data[offset++];
                switch (extOpcode)
                {
                    case 1:
                        EmitRow(rows, fileNames, directories, address, fileIdx, line);
                        address = 0; fileIdx = 1; line = 1; isStmt = defaultIsStmt;
                        break;
                    case 2:
                        address = extLen == 9 ? ReadU64(data, offset) : ReadU32(data, offset);
                        break;
                }
                offset = extEnd;
            }
            else if (opcode < opcodeBase)
            {
                switch (opcode)
                {
                    case 1: EmitRow(rows, fileNames, directories, address, fileIdx, line); break;
                    case 2: address += ReadULEB128(data, ref offset) * minimumInstructionLength; break;
                    case 3: line += (int)ReadSLEB128(data, ref offset); break;
                    case 4: fileIdx = (int)ReadULEB128(data, ref offset); break;
                    case 5: ReadULEB128(data, ref offset); break;
                    case 6: isStmt = !isStmt; break;
                    case 7: break;
                    case 8: address += (ulong)((255 - opcodeBase) / lineRange) * minimumInstructionLength; break;
                    case 9: address += ReadU16(data, offset); offset += 2; break;
                    default: for (int a = 0; a < stdOpcodeLengths[opcode]; a++) ReadULEB128(data, ref offset); break;
                }
            }
            else
            {
                int adjusted = opcode - opcodeBase;
                address += (ulong)(adjusted / lineRange) * minimumInstructionLength;
                line += lineBase + (adjusted % lineRange);
                EmitRow(rows, fileNames, directories, address, fileIdx, line);
            }
        }
        offset = unitEnd;
        return true;
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or OverflowException or ArgumentOutOfRangeException or DivideByZeroException)
        {
            // Malformed DWARF data; skip this compilation unit and stop parsing.
            return false;
        }
    }

    private static void EmitRow(List<LineRow> rows, List<(string Name, int DirIdx)> fileNames, List<string> directories, ulong address, int fileIdx, int line)
    {
        if (address == 0 || line <= 0 || fileIdx <= 0 || fileIdx >= fileNames.Count) return;
        var (name, dirIdx) = fileNames[fileIdx];
        string dir = dirIdx >= 0 && dirIdx < directories.Count ? directories[dirIdx] : string.Empty;
        rows.Add(new LineRow(address, string.IsNullOrEmpty(dir) ? name : $"{dir}/{name}", line));
    }

    private static uint ReadU32(byte[] d, int o) => (uint)(d[o] | (d[o+1] << 8) | (d[o+2] << 16) | (d[o+3] << 24));
    private static ushort ReadU16(byte[] d, int o) => (ushort)(d[o] | (d[o+1] << 8));
    private static ulong ReadU64(byte[] d, int o) { uint lo = ReadU32(d, o); uint hi = ReadU32(d, o+4); return lo | ((ulong)hi << 32); }
    private static ulong ReadULEB128(byte[] d, ref int o) { ulong r = 0; int s = 0; while (o < d.Length) { byte b = d[o++]; r |= (ulong)(b & 0x7F) << s; if ((b & 0x80) == 0) break; s += 7; } return r; }
    private static long ReadSLEB128(byte[] d, ref int o) { long r = 0; int s = 0; byte b = 0; while (o < d.Length) { b = d[o++]; r |= (long)(b & 0x7F) << s; s += 7; if ((b & 0x80) == 0) break; } if (s < 64 && (b & 0x40) != 0) r |= -(1L << s); return r; }
    private static string ReadNTS(byte[] d, ref int o) { int start = o; while (o < d.Length && d[o] != 0) o++; var s = System.Text.Encoding.UTF8.GetString(d, start, o - start); if (o < d.Length) o++; return s; }
}
