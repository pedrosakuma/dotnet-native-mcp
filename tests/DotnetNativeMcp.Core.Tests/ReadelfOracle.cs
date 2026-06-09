using System.Globalization;
using System.Text.RegularExpressions;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Thin wrapper over GNU <c>readelf</c> used as an independent reference ("oracle") for the
/// differential test harnesses. Each <c>TryRead*</c> method shells out to <c>readelf</c> with
/// the relevant flag and parses its wide (<c>-W</c>) textual output into a comparable shape.
///
/// All methods return <c>null</c> when <c>readelf</c> is unavailable or exits non-zero, so the
/// tests skip cleanly on hosts without binutils. See docs/differential-testing.md.
/// </summary>
internal static partial class ReadelfOracle
{
    /// <summary>One symbol-table row (<c>readelf -sW</c>).</summary>
    public readonly record struct Symbol(int Index, ulong Value, ulong Size, string Type, string Ndx, string Name);

    /// <summary>One section-header row (<c>readelf -SW</c>).</summary>
    public readonly record struct Section(string Name, string Type, ulong Address, ulong Offset, ulong Size);

    // -- regexes ------------------------------------------------------------

    [GeneratedRegex(@"^Symbol table '(?<tab>\S+)' contains")]
    private static partial Regex SymbolHeaderRegex();

    // Num: Value Size Type Bind Vis Ndx Name(optional). Size is DECIMAL in -sW output.
    [GeneratedRegex(@"^\s*(?<num>\d+):\s+(?<val>[0-9A-Fa-f]+)\s+(?<size>\d+)\s+(?<type>\S+)\s+\S+\s+\S+\s+(?<ndx>\S+)(?:\s+(?<name>.*\S))?\s*$")]
    private static partial Regex SymbolRowRegex();

    // [Nr] Name Type Address Off Size ... — Address/Off/Size are HEX in -SW output. Requiring a
    // letter-led Type skips the all-zero NULL section (index 0, blank name) cleanly.
    [GeneratedRegex(@"^\s*\[\s*\d+\]\s+(?<name>\S+)\s+(?<type>[A-Za-z][\w]*)\s+(?<addr>[0-9A-Fa-f]+)\s+(?<off>[0-9A-Fa-f]+)\s+(?<size>[0-9A-Fa-f]+)")]
    private static partial Regex SectionRowRegex();

    [GeneratedRegex(@"\(NEEDED\)\s+Shared library:\s+\[(?<lib>[^\]]+)\]")]
    private static partial Regex NeededRegex();

    // -- public API ---------------------------------------------------------

    /// <summary>
    /// Returns the preferred symbol table (<c>.symtab</c> when present, else <c>.dynsym</c>)
    /// indexed by symbol number — mirroring <c>ElfReader</c>'s table preference.
    /// </summary>
    public static IReadOnlyDictionary<int, Symbol>? TryReadSymbols(string path)
    {
        var tables = ParseSymbolTables(path);
        if (tables is null) return null;
        if (tables.TryGetValue(".symtab", out var symtab)) return symtab;
        if (tables.TryGetValue(".dynsym", out var dynsym)) return dynsym;
        return null;
    }

    /// <summary>
    /// Returns the version-normalized names of every undefined (<c>UND</c>) <c>.dynsym</c> entry —
    /// the reference set for <c>ElfReader.ReadImportedFunctions</c>. <c>null</c> if readelf is
    /// unavailable; an empty list when the binary has no <c>.dynsym</c>.
    /// </summary>
    public static IReadOnlyList<string>? TryReadUndefinedDynamicFunctions(string path)
    {
        var tables = ParseSymbolTables(path);
        if (tables is null) return null;
        if (!tables.TryGetValue(".dynsym", out var dynsym)) return [];

        return dynsym.Values
            .Where(s => s.Ndx == "UND")
            .Select(s => NormalizeName(s.Name))
            .Where(n => n.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Returns the section headers (<c>readelf -SW</c>) grouped by section name. ELF section
    /// names are not guaranteed unique, so each name maps to the list of rows (in header order)
    /// that carry it.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<Section>>? TryReadSections(string path)
    {
        var output = Run(path, "-SW");
        if (output is null) return null;

        var sections = new Dictionary<string, List<Section>>(StringComparer.Ordinal);
        foreach (var rawLine in output.Split('\n'))
        {
            var row = SectionRowRegex().Match(rawLine.TrimEnd('\r'));
            if (!row.Success) continue;

            var name = row.Groups["name"].Value;
            var section = new Section(
                name,
                row.Groups["type"].Value,
                ParseHex(row.Groups["addr"].Value),
                ParseHex(row.Groups["off"].Value),
                ParseHex(row.Groups["size"].Value));

            if (!sections.TryGetValue(name, out var list))
                sections[name] = list = [];
            list.Add(section);
        }

        return sections.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<Section>)kvp.Value, StringComparer.Ordinal);
    }

    /// <summary>Returns the <c>DT_NEEDED</c> shared-library names (<c>readelf -dW</c>).</summary>
    public static IReadOnlyList<string>? TryReadNeededLibraries(string path)
    {
        var output = Run(path, "-dW");
        if (output is null) return null;

        var libs = new List<string>();
        foreach (var rawLine in output.Split('\n'))
        {
            var m = NeededRegex().Match(rawLine);
            if (m.Success) libs.Add(m.Groups["lib"].Value);
        }

        return libs;
    }

    /// <summary>Strips the <c>@VER</c> / <c>@@VER</c> version suffix readelf appends to symbol names.</summary>
    public static string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var at = name.IndexOf('@', StringComparison.Ordinal);
        return (at >= 0 ? name[..at] : name).Trim();
    }

    // -- internals ----------------------------------------------------------

    private static Dictionary<string, Dictionary<int, Symbol>>? ParseSymbolTables(string path)
    {
        var output = Run(path, "-sW");
        if (output is null) return null;

        var tables = new Dictionary<string, Dictionary<int, Symbol>>(StringComparer.Ordinal);
        Dictionary<int, Symbol>? current = null;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            var header = SymbolHeaderRegex().Match(line);
            if (header.Success)
            {
                current = new Dictionary<int, Symbol>();
                tables[header.Groups["tab"].Value] = current;
                continue;
            }

            if (current is null) continue;

            var row = SymbolRowRegex().Match(line);
            if (!row.Success) continue;

            var index = int.Parse(row.Groups["num"].Value, CultureInfo.InvariantCulture);
            var value = ulong.Parse(row.Groups["val"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var size = ulong.Parse(row.Groups["size"].Value, CultureInfo.InvariantCulture);
            var type = row.Groups["type"].Value;
            var ndx = row.Groups["ndx"].Value;
            var name = row.Groups["name"].Success ? row.Groups["name"].Value : string.Empty;

            current[index] = new Symbol(index, value, size, type, ndx, name);
        }

        return tables;
    }

    private static ulong ParseHex(string hex) =>
        ulong.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static string? Run(string path, string flag) =>
        OracleProcess.Run("readelf", flag, path);
}
