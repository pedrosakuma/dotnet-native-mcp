using System.Globalization;
using DotnetNativeMcp.Core.Symbols;

namespace DotnetNativeMcp.Core.Imaging;

/// <summary>
/// Parser for ILC .map sidecar files (produced with <c>IlcMapFileType=Normal</c>).
/// The format is a simple text file with lines like:
/// <code>
/// 00000000004012a0 S_P_CoreLib_System_String__Equals
/// </code>
/// (hex address, whitespace, mangled symbol name).
/// When a .map file is present it provides the richest symbol table (every managed method);
/// if absent we fall back to ELF/PE symbol tables.
/// </summary>
public static class MapFileReader
{
    /// <summary>
    /// Attempts to locate a .map sidecar adjacent to the given binary path.
    /// Checks <c>&lt;binaryPath&gt;.map</c> and <c>&lt;binaryPathWithoutExtension&gt;.map</c>.
    /// Returns <c>null</c> when no file is found.
    /// </summary>
    public static string? FindSidecar(string binaryPath)
    {
        var direct = binaryPath + ".map";
        if (File.Exists(direct)) return direct;
        var withoutExt = Path.ChangeExtension(binaryPath, ".map");
        if (File.Exists(withoutExt)) return withoutExt;
        return null;
    }

    /// <summary>
    /// Parses a .map sidecar file and returns a list of <see cref="NativeSymbol"/> instances.
    /// Symbols are merged (by RVA) into <paramref name="existing"/> — .map wins over ELF/PE for any overlapping address.
    /// Returns <c>null</c> if <paramref name="mapPath"/> is <c>null</c> or cannot be read.
    /// </summary>
    public static IReadOnlyList<NativeSymbol>? TryMerge(
        string? mapPath,
        IReadOnlyList<NativeSymbol> existing)
    {
        if (mapPath is null) return null;

        List<NativeSymbol> parsed;
        try
        {
            parsed = Parse(mapPath);
        }
        catch
        {
            return null;
        }

        if (parsed.Count == 0) return null;

        // Build lookup by RVA from existing symbols (for size/section info preservation).
        var byRva = new Dictionary<ulong, NativeSymbol>();
        foreach (var s in existing) byRva[s.Rva] = s;

        // .map wins: overwrite existing by RVA.
        foreach (var s in parsed) byRva[s.Rva] = s;

        var merged = byRva.Values.OrderBy(s => s.Rva).ToList();
        // Re-index
        for (var i = 0; i < merged.Count; i++)
        {
            var orig = merged[i];
            merged[i] = orig with { Index = i };
        }
        return merged;
    }

    private static List<NativeSymbol> Parse(string mapPath)
    {
        var result = new List<NativeSymbol>();
        var index = 0;
        foreach (var line in File.ReadLines(mapPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var trimmed = line.TrimStart();
            var spaceIdx = trimmed.IndexOfAny([' ', '\t']);
            if (spaceIdx <= 0) continue;

            var addrStr = trimmed[..spaceIdx];
            var nameStr = trimmed[(spaceIdx + 1)..].Trim();
            if (string.IsNullOrEmpty(nameStr)) continue;

            if (!ulong.TryParse(addrStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rva))
                continue;

            var demangled = NativeAotSymbolDemangler.Demangle(nameStr);
            result.Add(new NativeSymbol(index++, nameStr, demangled, rva, 0, null, true));
        }
        return result;
    }
}
