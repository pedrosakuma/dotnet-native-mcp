using System.Globalization;
using System.Text;

namespace DotnetNativeMcp.Core.Strings;

/// <summary>Extracts printable strings from native image sections.</summary>
public static class StringExtractor
{
    /// <summary>Scans a section's raw bytes for printable ASCII and/or UTF-16LE strings.</summary>
    public static IEnumerable<ExtractedString> Extract(
        ReadOnlySpan<byte> data,
        ulong baseRva,
        string sectionName,
        int minLength,
        bool ascii,
        bool utf16)
    {
        List<(int Offset, ExtractedString Value)> matches = [];

        if (ascii)
            ScanAscii(data, baseRva, sectionName, minLength, matches);

        if (utf16)
            ScanUtf16(data, baseRva, sectionName, minLength, matches);

        matches.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));
        return matches.Select(static match => match.Value).ToList();
    }

    private static void ScanAscii(
        ReadOnlySpan<byte> data,
        ulong baseRva,
        string sectionName,
        int minLength,
        List<(int Offset, ExtractedString Value)> matches)
    {
        var index = 0;
        while (index < data.Length)
        {
            if (!IsPrintableAscii(data[index]))
            {
                index++;
                continue;
            }

            var start = index;
            while (index < data.Length && IsPrintableAscii(data[index]))
                index++;

            var length = index - start;
            if (length < minLength)
                continue;

            matches.Add((
                start,
                new ExtractedString(
                    sectionName,
                    FormatRva(baseRva + (ulong)start),
                    "ascii",
                    length,
                    Encoding.ASCII.GetString(data.Slice(start, length)))));
        }
    }

    private static void ScanUtf16(
        ReadOnlySpan<byte> data,
        ulong baseRva,
        string sectionName,
        int minLength,
        List<(int Offset, ExtractedString Value)> matches)
    {
        var index = 0;
        while (index + 1 < data.Length)
        {
            if (!IsPrintableUtf16Pair(data, index))
            {
                index++;
                continue;
            }

            var start = index;
            var charCount = 0;
            while (index + 1 < data.Length && IsPrintableUtf16Pair(data, index))
            {
                charCount++;
                index += 2;
            }

            if (charCount < minLength)
                continue;

            var chars = new char[charCount];
            for (var i = 0; i < charCount; i++)
                chars[i] = (char)data[start + (i * 2)];

            matches.Add((
                start,
                new ExtractedString(
                    sectionName,
                    FormatRva(baseRva + (ulong)start),
                    "utf16le",
                    charCount,
                    new string(chars))));
        }
    }

    private static bool IsPrintableUtf16Pair(ReadOnlySpan<byte> data, int index) =>
        data[index + 1] == 0x00 && IsPrintableAscii(data[index]);

    private static bool IsPrintableAscii(byte value) =>
        value == 0x09 || (value >= 0x20 && value <= 0x7E);

    private static string FormatRva(ulong value) => value.ToString("x16", CultureInfo.InvariantCulture);
}

/// <summary>One extracted string with its source location and encoding.</summary>
public sealed record ExtractedString(
    string SectionName,
    string RvaHex,
    string Encoding,
    int Length,
    string Value);
