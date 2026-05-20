using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace DotnetNativeMcp.Core.Identity;

/// <summary>
/// Extracts a stable build identity from ELF (NT_GNU_BUILD_ID), PE (CodeView GUID+Age),
/// or Mach-O (LC_UUID) binaries.
/// Falls back to a SHA-256 prefix of the raw file bytes when neither header is present.
/// </summary>
public static class BuildId
{
    private const int ElfMagic0 = 0x7F;
    private const byte ElfMagic1 = (byte)'E';
    private const byte ElfMagic2 = (byte)'L';
    private const byte ElfMagic3 = (byte)'F';

    // Mach-O magic numbers (little-endian read)
    private const uint MachOMagic64Le = 0xFEEDFACF;
    private const uint MachOMagic32Le = 0xFEEDFACE;
    private const uint LcUuid = 0x1B;

    /// <summary>
    /// Extracts the build id from the given file bytes.
    /// Returns a lowercase hex string.
    /// </summary>
    public static string Extract(ReadOnlySpan<byte> bytes, string fileName)
    {
        if (bytes.Length >= 4 &&
            bytes[0] == ElfMagic0 && bytes[1] == ElfMagic1 &&
            bytes[2] == ElfMagic2 && bytes[3] == ElfMagic3)
        {
            var elfId = TryExtractElfBuildId(bytes);
            if (elfId is not null) return elfId;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0x4D && bytes[1] == 0x5A) // MZ
        {
            var peId = TryExtractPeBuildId(bytes);
            if (peId is not null) return peId;
        }
        else if (bytes.Length >= 4)
        {
            var machMagic = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            if (machMagic == MachOMagic64Le || machMagic == MachOMagic32Le)
            {
                var machoId = TryExtractMachOBuildId(bytes, machMagic == MachOMagic64Le);
                if (machoId is not null) return machoId;
            }
        }

        // SHA-256 prefix fallback
        return ComputeSha256Prefix(bytes);
    }

    private static string? TryExtractMachOBuildId(ReadOnlySpan<byte> bytes, bool is64)
    {
        var headerSize = is64 ? 32 : 28;
        if (bytes.Length < headerSize) return null;
        var ncmds = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]);
        var cmdOffset = headerSize;
        for (var i = 0u; i < ncmds; i++)
        {
            if (cmdOffset + 8 > bytes.Length) break;
            var cmd = BinaryPrimitives.ReadUInt32LittleEndian(bytes[cmdOffset..]);
            var cmdsize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 4)..]);
            if (cmdsize < 8 || cmdOffset + cmdsize > (uint)bytes.Length) break;
            if (cmd == LcUuid)
            {
                // LC_UUID: cmd(4)+cmdsize(4)+uuid(16) — total 24 bytes
                if (cmdOffset + 24 > bytes.Length) break;
                var uuid = bytes[(cmdOffset + 8)..(cmdOffset + 24)];
                var sb = new StringBuilder(32);
                foreach (var b in uuid)
                    sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return sb.ToString();
            }
            cmdOffset += (int)cmdsize;
        }
        return null;
    }

    private static string? TryExtractElfBuildId(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return ElfBuildIdExtractor.Extract(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryExtractPeBuildId(ReadOnlySpan<byte> bytes)
    {
        try
        {
            using var ms = new System.IO.MemoryStream(bytes.ToArray());
            using var pe = new PEReader(ms, PEStreamOptions.Default);
            var debugEntries = pe.ReadDebugDirectory();
            foreach (var entry in debugEntries)
            {
                if (entry.Type == DebugDirectoryEntryType.CodeView)
                {
                    var cv = pe.ReadCodeViewDebugDirectoryData(entry);
                    return cv.Guid.ToString("N") + cv.Age.ToString("x", System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }
        catch
        {
            // fall through
        }
        return null;
    }

    private static string ComputeSha256Prefix(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        var sb = new StringBuilder(24);
        for (var i = 0; i < 12; i++)
            sb.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}

/// <summary>Internal ELF build-id extractor (avoids duplicating ELF logic in BuildId).</summary>
internal static class ElfBuildIdExtractor
{
    internal static string? Extract(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 16) return null;
        var is64 = bytes[4] == 2; // EI_CLASS: 1=32bit, 2=64bit
        // EI_DATA: 1=LE, 2=BE; only LE supported
        if (bytes[5] != 1) return null;

        ulong shOff;
        ushort shEntSize;
        ushort shNum;
        ushort shStrNdx;

        if (is64)
        {
            if (bytes.Length < 64) return null;
            shOff = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes[40..]);
            shEntSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes[58..]);
            shNum = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes[60..]);
            shStrNdx = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes[62..]);
        }
        else
        {
            if (bytes.Length < 52) return null;
            shOff = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes[32..]);
            shEntSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes[46..]);
            shNum = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes[48..]);
            shStrNdx = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes[50..]);
        }

        if (shOff == 0 || shEntSize == 0 || shNum == 0) return null;

        // Read the string table section to find section names.
        var shStrSection = ReadSectionHeader(bytes, is64, shOff, shEntSize, shStrNdx);
        if (shStrSection is null) return null;
        var (strOff, strSize) = shStrSection.Value;
        if (strOff + strSize > (ulong)bytes.Length) return null;
        var strTab = bytes[(int)strOff..(int)(strOff + strSize)];

        for (var i = 0; i < shNum; i++)
        {
            var hdr = ReadSectionHeader(bytes, is64, shOff, shEntSize, (ushort)i);
            if (hdr is null) continue;
            var (dataOff, dataSize) = hdr.Value;
            var nameIdxVal = ReadSectionNameIndex(bytes, is64, shOff, shEntSize, (ushort)i);
            if (nameIdxVal >= (uint)strTab.Length) continue;
            var name = ReadCString(strTab, (int)nameIdxVal);
            if (name != ".note.gnu.build-id") continue;

            // Parse the note: Elf64_Nhdr = namesz(4) + descsz(4) + type(4); then name (padded) + desc.
            if (dataOff + dataSize > (ulong)bytes.Length || dataSize < 12) continue;
            var note = bytes[(int)dataOff..(int)(dataOff + dataSize)];
            var namesz = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(note[0..]);
            var descsz = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(note[4..]);
            var type = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(note[8..]);
            if (type != 3) continue; // NT_GNU_BUILD_ID = 3
            var namePadded = (namesz + 3) & ~3u;
            var descStart = 12 + (int)namePadded;
            if ((ulong)descStart + descsz > dataSize) continue;
            var desc = note[descStart..(descStart + (int)descsz)];
            var sb = new StringBuilder((int)descsz * 2);
            foreach (var b in desc) sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return sb.ToString();
        }
        return null;
    }

    private static (ulong offset, ulong size)? ReadSectionHeader(
        ReadOnlySpan<byte> bytes, bool is64, ulong shOff, ushort shEntSize, ushort index)
    {
        var hdrStart = shOff + (ulong)(index * shEntSize);
        if (is64)
        {
            if (hdrStart + 64 > (ulong)bytes.Length) return null;
            var sh = bytes[(int)hdrStart..];
            var offset = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(sh[24..]);
            var size = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(sh[32..]);
            return (offset, size);
        }
        else
        {
            if (hdrStart + 40 > (ulong)bytes.Length) return null;
            var sh = bytes[(int)hdrStart..];
            var offset = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(sh[16..]);
            var size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(sh[20..]);
            return (offset, size);
        }
    }

    private static uint ReadSectionNameIndex(
        ReadOnlySpan<byte> bytes, bool is64, ulong shOff, ushort shEntSize, ushort index)
    {
        var hdrStart = shOff + (ulong)(index * shEntSize);
        var sh = bytes[(int)hdrStart..];
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(sh[0..]);
    }

    private static string ReadCString(ReadOnlySpan<byte> data, int offset)
    {
        var end = offset;
        while (end < data.Length && data[end] != 0) end++;
        return System.Text.Encoding.ASCII.GetString(data[offset..end]);
    }
}
