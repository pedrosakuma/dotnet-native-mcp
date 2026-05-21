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
    public static NativeResult<JitIlMap> Load(string path)
    {
        if (!File.Exists(path))
            return NativeResult.Fail<JitIlMap>(
                ErrorKinds.BinaryNotFound,
                $"File not found: '{path}'.");

        string text;
        try
        {
            text = File.ReadAllText(path, new UTF8Encoding(false, true));
        }
        catch (Exception ex)
        {
            return NativeResult.Fail<JitIlMap>(
                ErrorKinds.InvalidArgument,
                $"Failed to read IL map '{path}': {ex.Message}");
        }

        return Parse(text, path);
    }

    internal static NativeResult<JitIlMap> Parse(string text, string sourceName)
    {
        var entries = new List<Entry>();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var headerAllowed = true;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmedLine = lines[i].Trim();
            if (trimmedLine.Length == 0)
                continue;

            if (trimmedLine.StartsWith('#'))
            {
                if (headerAllowed && TryParseHeaderVersion(trimmedLine, out var version))
                {
                    if (version != 1)
                        return NativeResult.Fail<JitIlMap>(
                            ErrorKinds.InvalidArgument,
                            $"Unsupported ilmap version {version} in '{sourceName}'. Supported: 1.");

                    headerAllowed = false;
                }

                continue;
            }

            headerAllowed = false;

            var firstTab = trimmedLine.IndexOf('\t');
            if (firstTab <= 0 || firstTab != trimmedLine.LastIndexOf('\t') || firstTab == trimmedLine.Length - 1)
                return NativeResult.Fail<JitIlMap>(
                    ErrorKinds.InvalidArgument,
                    $"Malformed IL map line {i + 1} in '{sourceName}'. Expected '<nativeOffsetHex>\\t<ilOffsetHex|prolog|epilog|noinfo>'.");

            var nativeOffsetToken = trimmedLine[..firstTab].Trim();
            var ilOffsetToken = trimmedLine[(firstTab + 1)..].Trim();

            if (!ulong.TryParse(nativeOffsetToken, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var nativeOffset))
                return NativeResult.Fail<JitIlMap>(
                    ErrorKinds.InvalidArgument,
                    $"Malformed native offset '{nativeOffsetToken}' on IL map line {i + 1} in '{sourceName}'. Expected lowercase hex without 0x.");

            if (!TryNormalizeIlOffset(ilOffsetToken, out var normalizedIlOffset))
                return NativeResult.Fail<JitIlMap>(
                    ErrorKinds.InvalidArgument,
                    $"Malformed IL offset '{ilOffsetToken}' on IL map line {i + 1} in '{sourceName}'. Expected lowercase hex without 0x, or one of: prolog, epilog, noinfo.");

            entries.Add(new Entry(nativeOffset, normalizedIlOffset));
        }

        entries.Sort((left, right) => left.NativeOffset.CompareTo(right.NativeOffset));

        for (var i = 1; i < entries.Count; i++)
        {
            if (entries[i - 1].NativeOffset == entries[i].NativeOffset)
                return NativeResult.Fail<JitIlMap>(
                    ErrorKinds.InvalidArgument,
                    $"Duplicate native offset 0x{entries[i].NativeOffset:x} in '{sourceName}'. Each IL map entry must start a unique range.");
        }

        return NativeResult.Ok(
            $"Loaded {entries.Count} IL map entr{(entries.Count == 1 ? "y" : "ies")} from '{Path.GetFileName(sourceName)}'.",
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
