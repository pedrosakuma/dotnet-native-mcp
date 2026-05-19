using System.Buffers.Binary;
using System.Text;

namespace DotnetNativeMcp.Core;

public static class NativeStringExtractor
{
    private static readonly string[] DefaultSections = [".rodata", ".rdata"];
    private const int MaxResultsLimit = 2000;

    public static ExtractStringsResult Extract(
        string binaryPath,
        string[]? sectionFilter = null,
        int minLength = 6,
        int maxResults = 200,
        int pageOffset = 0)
    {
        if (string.IsNullOrWhiteSpace(binaryPath))
        {
            throw new ArgumentException("Binary path must be provided.", nameof(binaryPath));
        }

        if (minLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minLength), "minLength must be at least 1.");
        }

        if (maxResults < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), "maxResults must be at least 1.");
        }

        if (pageOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageOffset), "pageOffset must be non-negative.");
        }

        var imageBytes = File.ReadAllBytes(binaryPath);
        var sections = ParseSections(imageBytes);
        var effectiveFilter = (sectionFilter is { Length: > 0 } ? sectionFilter : DefaultSections)
            .Select(s => s.Trim())
            .Where(static s => s.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var filteredSections = sections.Where(s => effectiveFilter.Contains(s.Name)).ToList();
        var found = new List<ExtractedString>(capacity: Math.Min(MaxResultsLimit, 512));

        foreach (var section in filteredSections)
        {
            ScanSectionForAscii(section, imageBytes, minLength, found);
            ScanSectionForUtf16Le(section, imageBytes, minLength, found);

            if (found.Count >= MaxResultsLimit)
            {
                break;
            }
        }

        found.Sort(static (a, b) =>
        {
            var sectionCompare = string.CompareOrdinal(a.Section, b.Section);
            return sectionCompare != 0 ? sectionCompare : a.Offset.CompareTo(b.Offset);
        });

        if (found.Count > MaxResultsLimit)
        {
            found.RemoveRange(MaxResultsLimit, found.Count - MaxResultsLimit);
        }

        var pageSize = Math.Min(maxResults, MaxResultsLimit);
        if (pageOffset >= found.Count)
        {
            return new([], null);
        }

        var page = found.Skip(pageOffset).Take(pageSize).ToList();
        int? nextOffset = pageOffset + page.Count < found.Count ? pageOffset + page.Count : null;
        return new(page, nextOffset);
    }

    private static List<ParsedBinarySection> ParseSections(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x7F && bytes[1] == (byte)'E' && bytes[2] == (byte)'L' && bytes[3] == (byte)'F')
        {
            return ParseElfSections(bytes);
        }

        if (bytes.Length >= 2 && bytes[0] == (byte)'M' && bytes[1] == (byte)'Z')
        {
            return ParsePeSections(bytes);
        }

        throw new InvalidDataException("Unsupported binary format. Expected ELF or PE.");
    }

    private static List<ParsedBinarySection> ParsePeSections(byte[] bytes)
    {
        if (bytes.Length < 0x40)
        {
            throw new InvalidDataException("Invalid PE file.");
        }

        var peOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x3C, 4));
        if (peOffset < 0 || peOffset + 24 > bytes.Length || bytes[peOffset] != (byte)'P' || bytes[peOffset + 1] != (byte)'E')
        {
            throw new InvalidDataException("Invalid PE signature.");
        }

        var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(peOffset + 6, 2));
        var optionalHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(peOffset + 20, 2));
        var sectionTableOffset = peOffset + 24 + optionalHeaderSize;
        var sectionSize = 40;

        var sections = new List<ParsedBinarySection>(sectionCount);
        for (var i = 0; i < sectionCount; i++)
        {
            var offset = sectionTableOffset + (i * sectionSize);
            if (offset + sectionSize > bytes.Length)
            {
                break;
            }

            var rawName = bytes.AsSpan(offset, 8);
            var nameLength = rawName.IndexOf((byte)0);
            if (nameLength < 0)
            {
                nameLength = rawName.Length;
            }

            var name = Encoding.ASCII.GetString(rawName[..nameLength]);
            var sizeOfRawData = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 16, 4));
            var pointerToRawData = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 20, 4));
            if (sizeOfRawData <= 0 || pointerToRawData < 0 || pointerToRawData + sizeOfRawData > bytes.Length)
            {
                continue;
            }

            sections.Add(new(name, pointerToRawData, sizeOfRawData));
        }

        return sections;
    }

    private static List<ParsedBinarySection> ParseElfSections(byte[] bytes)
    {
        if (bytes.Length < 64)
        {
            throw new InvalidDataException("Invalid ELF file.");
        }

        var elfClass = bytes[4];
        var elfData = bytes[5];
        var littleEndian = elfData switch
        {
            1 => true,
            2 => false,
            _ => throw new InvalidDataException("Unsupported ELF endianness."),
        };

        if (elfClass != 2)
        {
            throw new InvalidDataException("Only ELF64 is currently supported.");
        }

        var sectionHeaderOffset = (long)ReadUInt64(bytes, 0x28, littleEndian);
        var sectionHeaderEntrySize = ReadUInt16(bytes, 0x3A, littleEndian);
        var sectionHeaderCount = ReadUInt16(bytes, 0x3C, littleEndian);
        var stringSectionIndex = ReadUInt16(bytes, 0x3E, littleEndian);

        if (sectionHeaderOffset <= 0
            || sectionHeaderEntrySize <= 0
            || sectionHeaderCount <= 0
            || sectionHeaderOffset + (sectionHeaderEntrySize * sectionHeaderCount) > bytes.Length)
        {
            throw new InvalidDataException("Invalid ELF section headers.");
        }

        var shstrHeaderOffset = checked((int)(sectionHeaderOffset + (stringSectionIndex * sectionHeaderEntrySize)));
        if (shstrHeaderOffset + sectionHeaderEntrySize > bytes.Length)
        {
            throw new InvalidDataException("Invalid ELF section name table index.");
        }

        var shstrOffset = checked((int)ReadUInt64(bytes, shstrHeaderOffset + 0x18, littleEndian));
        var shstrSize = checked((int)ReadUInt64(bytes, shstrHeaderOffset + 0x20, littleEndian));
        if (shstrOffset < 0 || shstrSize <= 0 || shstrOffset + shstrSize > bytes.Length)
        {
            throw new InvalidDataException("Invalid ELF section name table.");
        }

        var shstr = bytes.AsSpan(shstrOffset, shstrSize);
        var sections = new List<ParsedBinarySection>(sectionHeaderCount);
        for (var i = 0; i < sectionHeaderCount; i++)
        {
            var headerOffset = checked((int)(sectionHeaderOffset + (i * sectionHeaderEntrySize)));
            if (headerOffset + sectionHeaderEntrySize > bytes.Length)
            {
                break;
            }

            var nameOffset = checked((int)ReadUInt32(bytes, headerOffset, littleEndian));
            var sectionOffset = checked((int)ReadUInt64(bytes, headerOffset + 0x18, littleEndian));
            var sectionSize = checked((int)ReadUInt64(bytes, headerOffset + 0x20, littleEndian));
            if (sectionOffset < 0 || sectionSize <= 0 || sectionOffset + sectionSize > bytes.Length)
            {
                continue;
            }

            var name = ReadElfString(shstr, nameOffset);
            sections.Add(new(name, sectionOffset, sectionSize));
        }

        return sections;
    }

    private static uint ReadUInt32(byte[] bytes, int offset, bool littleEndian)
    {
        var value = bytes.AsSpan(offset, 4);
        return littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(value)
            : BinaryPrimitives.ReadUInt32BigEndian(value);
    }

    private static ushort ReadUInt16(byte[] bytes, int offset, bool littleEndian)
    {
        var value = bytes.AsSpan(offset, 2);
        return littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(value)
            : BinaryPrimitives.ReadUInt16BigEndian(value);
    }

    private static ulong ReadUInt64(byte[] bytes, int offset, bool littleEndian)
    {
        var value = bytes.AsSpan(offset, 8);
        return littleEndian
            ? BinaryPrimitives.ReadUInt64LittleEndian(value)
            : BinaryPrimitives.ReadUInt64BigEndian(value);
    }

    private static string ReadElfString(ReadOnlySpan<byte> stringTable, int offset)
    {
        if (offset < 0 || offset >= stringTable.Length)
        {
            return string.Empty;
        }

        var end = offset;
        while (end < stringTable.Length && stringTable[end] != 0)
        {
            end++;
        }

        return Encoding.ASCII.GetString(stringTable[offset..end]);
    }

    private static void ScanSectionForAscii(ParsedBinarySection section, byte[] imageBytes, int minLength, List<ExtractedString> target)
    {
        var bytes = imageBytes.AsSpan(section.Offset, section.Length);
        var sectionRelativeOffset = -1;
        for (var i = 0; i < bytes.Length; i++)
        {
            var printable = bytes[i] is >= 0x20 and <= 0x7E;
            if (printable)
            {
                sectionRelativeOffset = sectionRelativeOffset < 0 ? i : sectionRelativeOffset;
                continue;
            }

            if (sectionRelativeOffset >= 0 && i - sectionRelativeOffset >= minLength)
            {
                target.Add(new(section.Name, sectionRelativeOffset, "ascii", Encoding.ASCII.GetString(bytes.Slice(sectionRelativeOffset, i - sectionRelativeOffset))));
            }

            if (target.Count >= MaxResultsLimit)
            {
                return;
            }

            sectionRelativeOffset = -1;
        }

        if (sectionRelativeOffset >= 0 && bytes.Length - sectionRelativeOffset >= minLength && target.Count < MaxResultsLimit)
        {
            target.Add(new(section.Name, sectionRelativeOffset, "ascii", Encoding.ASCII.GetString(bytes[sectionRelativeOffset..])));
        }
    }

    private static void ScanSectionForUtf16Le(ParsedBinarySection section, byte[] imageBytes, int minLength, List<ExtractedString> target)
    {
        var bytes = imageBytes.AsSpan(section.Offset, section.Length);
        var i = 0;
        while (i + 1 < bytes.Length)
        {
            var startsOnUtf16Boundary = i % 2 == 0;
            if (!startsOnUtf16Boundary || !(bytes[i] is >= 0x20 and <= 0x7E && bytes[i + 1] == 0))
            {
                i++;
                continue;
            }

            var sectionRelativeOffset = i;
            var chars = 0;
            while (i + 1 < bytes.Length && bytes[i] is >= 0x20 and <= 0x7E && bytes[i + 1] == 0)
            {
                chars++;
                i += 2;
            }

            if (chars >= minLength)
            {
                target.Add(new(section.Name, sectionRelativeOffset, "utf-16le", Encoding.Unicode.GetString(bytes.Slice(sectionRelativeOffset, chars * 2))));
            }

            if (target.Count >= MaxResultsLimit)
            {
                return;
            }
        }
    }

    private sealed record ParsedBinarySection(string Name, int Offset, int Length);
}

public sealed record ExtractedString(string Section, int Offset, string Encoding, string Value);

public sealed record ExtractStringsResult(IReadOnlyList<ExtractedString> Items, int? NextOffset);
