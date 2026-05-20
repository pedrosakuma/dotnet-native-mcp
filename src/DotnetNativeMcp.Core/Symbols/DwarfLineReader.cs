using DotnetNativeMcp.Core.Imaging;

namespace DotnetNativeMcp.Core.Symbols;

internal static class DwarfLineReader
{
    public readonly record struct LineRow(ulong Address, string File, int Line);

    public static IReadOnlyList<LineRow> Read(NativeImage image)
    {
        var section = image.Sections.FirstOrDefault(s => s.Name == ".debug_line");
        if (section is null || section.FileSize == 0) return [];
        var data = image.GetSectionBytes(section).ToArray();
        var rows = new List<LineRow>();
        ParseSection(data, rows);
        rows.Sort((a, b) => a.Address.CompareTo(b.Address));
        return rows;
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
        if (offset + 4 > data.Length) return false;
        uint unitLength = ReadU32(data, offset); offset += 4;
        if (unitLength == 0xFFFFFFFF) { offset += 8; return false; }
        int unitEnd = offset + (int)unitLength;
        if (unitEnd > data.Length) return false;
        if (offset + 2 > unitEnd) { offset = unitEnd; return true; }
        ushort version = ReadU16(data, offset); offset += 2;
        if (version < 2 || version > 4) { offset = unitEnd; return true; }
        if (offset + 4 > unitEnd) { offset = unitEnd; return true; }
        int headerLength = (int)ReadU32(data, offset); offset += 4;
        int programStart = offset + headerLength;
        if (programStart > unitEnd || programStart > data.Length) { offset = unitEnd; return true; }
        byte minimumInstructionLength = data[offset++];
        if (version >= 4) offset++;
        bool defaultIsStmt = data[offset++] != 0;
        int lineBase = (sbyte)data[offset++];
        byte lineRange = data[offset++];
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
