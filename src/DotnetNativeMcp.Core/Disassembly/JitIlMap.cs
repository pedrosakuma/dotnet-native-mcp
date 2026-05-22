using System.Globalization;
using System.Text;
using DotnetNativeMcp.Core.Errors;

namespace DotnetNativeMcp.Core.Disassembly;

/// <summary>
/// IL-to-native offset map for a raw JIT code blob captured from a live process.
/// </summary>
public sealed class JitIlMap
{
    private readonly IReadOnlyList<Entry> _entries;

    private JitIlMap(IReadOnlyList<Entry> entries) => _entries = entries;

    internal IReadOnlyList<Entry> Entries => _entries;

    /// <summary>
    /// Loads a UTF-8 <c>.ilmap</c> file from disk.
    /// </summary>
    public static NativeResult<JitIlMap> Load(string path) =>
        Load(path, ResourceLimits.MaxIlMapBytes, ResourceLimits.MaxIlMapEntries);

    internal static NativeResult<JitIlMap> Load(string path, long maxBytes, int maxEntries)
    {
        var displayName = Path.GetFileName(path);

        if (!File.Exists(path))
            return NativeResult.Fail<JitIlMap>(
                ErrorKinds.BinaryNotFound,
                $"File not found: '{displayName}'.");

        try
        {
            var info = new FileInfo(path);
            if (info.Length > maxBytes)
            {
                return NativeResult.Fail<JitIlMap>(
                    ErrorKinds.FileTooLarge,
                    $"IL map '{displayName}' is {info.Length} bytes, which exceeds the limit of {maxBytes} bytes.");
            }

            using var stream = File.OpenRead(path);
            if (stream.CanSeek && stream.Length > maxBytes)
            {
                return NativeResult.Fail<JitIlMap>(
                    ErrorKinds.FileTooLarge,
                    $"IL map '{displayName}' is {stream.Length} bytes, which exceeds the limit of {maxBytes} bytes.");
            }

            return ParseLines(EnumerateLinesBounded(stream, maxBytes, displayName), displayName, maxEntries);
        }
        catch (Exception ex)
        {
            return NativeResult.Fail<JitIlMap>(
                ErrorKinds.InvalidArgument,
                $"Failed to read IL map '{displayName}'.",
                SanitisedError.From(ex, path));
        }
    }

    private static IEnumerable<string> EnumerateLinesBounded(Stream stream, long maxBytes, string sourceName)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (stream.CanSeek && stream.Position > maxBytes)
            {
                throw new InvalidDataException(
                    $"IL map '{sourceName}' grew beyond {maxBytes} bytes while being read.");
            }
            yield return line;
        }
    }

    internal static NativeResult<JitIlMap> Parse(string text, string sourceName) =>
        Parse(text, sourceName, ResourceLimits.MaxIlMapEntries);

    internal static NativeResult<JitIlMap> Parse(string text, string sourceName, int maxEntries) =>
        ParseLines(text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'), sourceName, maxEntries);

    private static NativeResult<JitIlMap> ParseLines(IEnumerable<string> lines, string sourceName, int maxEntries)
    {
        var displayName = Path.GetFileName(sourceName);
        if (string.IsNullOrEmpty(displayName))
            displayName = sourceName;

        var entries = new List<Entry>();
        var headerAllowed = true;
        var lineNumber = 0;

        foreach (var line in lines)
        {
            lineNumber++;
            var trimmedLine = line.Trim();
            if (trimmedLine.Length == 0)
                continue;

            if (headerAllowed)
            {
                headerAllowed = false;

                if (TryParseHeaderVersion(trimmedLine, out var version))
                {
                    if (version != 1)
                        return NativeResult.Fail<JitIlMap>(
                            ErrorKinds.InvalidArgument,
                            $"Unsupported ilmap version {version} in '{displayName}'. Supported: 1.");

                    continue;
                }
            }

            if (trimmedLine.StartsWith('#'))
                continue;

            var firstTab = trimmedLine.IndexOf('\t');
            if (firstTab <= 0 || firstTab != trimmedLine.LastIndexOf('\t') || firstTab == trimmedLine.Length - 1)
                return NativeResult.Fail<JitIlMap>(
                    ErrorKinds.InvalidArgument,
                    $"Malformed IL map line {lineNumber} in '{displayName}'. Expected '<nativeOffsetHex>\\t<ilOffsetHex|prolog|epilog|noinfo>'.");

            var nativeOffsetToken = trimmedLine[..firstTab].Trim();
            var ilOffsetToken = trimmedLine[(firstTab + 1)..].Trim();

            if (!ulong.TryParse(nativeOffsetToken, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var nativeOffset))
                return NativeResult.Fail<JitIlMap>(
                    ErrorKinds.InvalidArgument,
                    $"Malformed native offset '{nativeOffsetToken}' on IL map line {lineNumber} in '{displayName}'. Expected lowercase hex without 0x.");

            if (!TryNormalizeIlOffset(ilOffsetToken, out var normalizedIlOffset))
                return NativeResult.Fail<JitIlMap>(
                    ErrorKinds.InvalidArgument,
                    $"Malformed IL offset '{ilOffsetToken}' on IL map line {lineNumber} in '{displayName}'. Expected lowercase hex without 0x, or one of: prolog, epilog, noinfo.");

            if (entries.Count >= maxEntries)
            {
                return NativeResult.Fail<JitIlMap>(
                    ErrorKinds.FileTooLarge,
                    $"IL map '{displayName}' exceeds the maximum of {maxEntries} entries.");
            }

            entries.Add(new Entry(nativeOffset, normalizedIlOffset));
        }

        entries.Sort((left, right) => left.NativeOffset.CompareTo(right.NativeOffset));

        for (var i = 1; i < entries.Count; i++)
        {
            if (entries[i - 1].NativeOffset == entries[i].NativeOffset)
                return NativeResult.Fail<JitIlMap>(
                    ErrorKinds.InvalidArgument,
                    $"Duplicate native offset 0x{entries[i].NativeOffset:x} in '{displayName}'. Each IL map entry must start a unique range.");
        }

        return NativeResult.Ok(
            $"Loaded {entries.Count} IL map entr{(entries.Count == 1 ? "y" : "ies")} from '{displayName}'.",
            new JitIlMap(entries));
    }

    /// <summary>
    /// Resolves the IL offset annotation for the supplied native offset.
    /// </summary>
    public string? FindIlOffset(ulong nativeOffset)
    {
        if (_entries.Count == 0 || nativeOffset < _entries[0].NativeOffset)
            return null;

        var low = 0;
        var high = _entries.Count - 1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var candidate = _entries[mid].NativeOffset;

            if (candidate <= nativeOffset)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return high >= 0 ? _entries[high].IlOffset : null;
    }

    internal sealed record Entry(ulong NativeOffset, string IlOffset);

    private static bool TryNormalizeIlOffset(string token, out string normalized)
    {
        switch (token)
        {
            case "prolog":
            case "epilog":
            case "noinfo":
                normalized = token;
                return true;
        }

        if (ulong.TryParse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var ilOffset))
        {
            normalized = ilOffset.ToString("x", CultureInfo.InvariantCulture);
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    private static bool TryParseHeaderVersion(string line, out int version)
    {
        const string prefix = "# ilmap v";

        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            version = 0;
            return false;
        }

        var versionToken = line[prefix.Length..].Trim();
        if (!int.TryParse(versionToken, NumberStyles.None, CultureInfo.InvariantCulture, out version))
        {
            version = 0;
            return false;
        }

        return true;
    }
}
