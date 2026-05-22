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
        bool utf16) =>
        Extract(data, baseRva, sectionName, minLength, ascii, utf16, out _);

    /// <summary>Scans a section's raw bytes for printable ASCII and/or UTF-16LE strings.</summary>
    public static IReadOnlyList<ExtractedString> Extract(
        ReadOnlySpan<byte> data,
        ulong baseRva,
        string sectionName,
        int minLength,
        bool ascii,
        bool utf16,
        out bool truncated,
        int maxMatches = ResourceLimits.MaxStringMatches)
    {
        List<(int Offset, ExtractedString Value)> matches = [];
        truncated = false;

        if (maxMatches <= 0)
        {
            truncated = true;
            return [];
        }

        var matchCapReached = ascii && ScanAscii(data, baseRva, sectionName, minLength, matches, maxMatches, ref truncated);
        if (!matchCapReached && utf16 && ScanUtf16(data, baseRva, sectionName, minLength, matches, maxMatches, ref truncated))
            matchCapReached = true;

        if (matchCapReached)
            truncated = true;

        matches.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));
        return matches.Select(static match => match.Value).ToList();
    }

    private static bool ScanAscii(
        ReadOnlySpan<byte> data,
        ulong baseRva,
        string sectionName,
        int minLength,
        List<(int Offset, ExtractedString Value)> matches,
        int maxMatches,
        ref bool truncated)
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

            if (matches.Count >= maxMatches)
                return true;

            matches.Add((
                start,
                new ExtractedString(
                    sectionName,
                    FormatRva(baseRva + (ulong)start),
                    "ascii",
                    length,
                    BuildAsciiValue(data, start, length, ref truncated))));
        }

        return false;
    }

    private static bool ScanUtf16(
        ReadOnlySpan<byte> data,
        ulong baseRva,
        string sectionName,
        int minLength,
        List<(int Offset, ExtractedString Value)> matches,
        int maxMatches,
        ref bool truncated)
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

            if (matches.Count >= maxMatches)
                return true;

            matches.Add((
                start,
                new ExtractedString(
                    sectionName,
                    FormatRva(baseRva + (ulong)start),
                    "utf16le",
                    charCount,
                    BuildUtf16Value(data, start, charCount, ref truncated))));
        }

        return false;
    }

    private static bool IsPrintableUtf16Pair(ReadOnlySpan<byte> data, int index) =>
        data[index + 1] == 0x00 && IsPrintableAscii(data[index]);

    private static bool IsPrintableAscii(byte value) =>
        value == 0x09 || (value >= 0x20 && value <= 0x7E);

    private static string BuildAsciiValue(ReadOnlySpan<byte> data, int start, int length, ref bool truncated)
    {
        if (length <= ResourceLimits.MaxExtractedStringChars)
            return Encoding.ASCII.GetString(data.Slice(start, length));

        truncated = true;
        var visibleLength = Math.Max(1, ResourceLimits.MaxExtractedStringChars - 1);
        return Encoding.ASCII.GetString(data.Slice(start, visibleLength)) + "…";
    }

    private static string BuildUtf16Value(ReadOnlySpan<byte> data, int start, int charCount, ref bool truncated)
    {
        if (charCount <= ResourceLimits.MaxExtractedStringChars)
        {
            var chars = new char[charCount];
            for (var i = 0; i < charCount; i++)
                chars[i] = (char)data[start + (i * 2)];

            return new string(chars);
        }

        truncated = true;
        var visibleLength = Math.Max(1, ResourceLimits.MaxExtractedStringChars - 1);
        var visibleChars = new char[visibleLength + 1];
        for (var i = 0; i < visibleLength; i++)
            visibleChars[i] = (char)data[start + (i * 2)];

        visibleChars[^1] = '…';
        return new string(visibleChars);
    }

    private static string FormatRva(ulong value) => value.ToString("x16", CultureInfo.InvariantCulture);
}

/// <summary>One extracted string with its source location and encoding.</summary>
public sealed record ExtractedString(
    string SectionName,
    string RvaHex,
    string Encoding,
    int Length,
    string Value);
