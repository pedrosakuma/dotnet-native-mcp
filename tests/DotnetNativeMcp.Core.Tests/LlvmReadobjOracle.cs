using System.Globalization;
using System.Text.RegularExpressions;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Thin wrapper over LLVM <c>llvm-readobj</c> used as an independent reference ("oracle") for the
/// PE differential harness. It parses <c>llvm-readobj --sections</c> output into a comparable
/// section shape for <see cref="DotnetNativeMcp.Core.Imaging.PeNativeReader"/>.
///
/// Returns <c>null</c> when <c>llvm-readobj</c> is unavailable or exits non-zero, so the test skips
/// cleanly on hosts without LLVM. See docs/differential-testing.md.
/// </summary>
internal static partial class LlvmReadobjOracle
{
    /// <summary>One COFF/PE section header (<c>llvm-readobj --sections</c>).</summary>
    public readonly record struct PeSection(string Name, ulong VirtualAddress, ulong VirtualSize, ulong FileOffset, ulong FileSize);

    [GeneratedRegex(@"^\s*Name:\s+(?<name>\S+)")]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"^\s*VirtualSize:\s+(?<v>\S+)")]
    private static partial Regex VirtualSizeRegex();

    [GeneratedRegex(@"^\s*VirtualAddress:\s+(?<v>\S+)")]
    private static partial Regex VirtualAddressRegex();

    [GeneratedRegex(@"^\s*RawDataSize:\s+(?<v>\S+)")]
    private static partial Regex RawDataSizeRegex();

    [GeneratedRegex(@"^\s*PointerToRawData:\s+(?<v>\S+)")]
    private static partial Regex PointerToRawDataRegex();

    /// <summary>
    /// Returns the PE section headers grouped by section name. PE section names are not guaranteed
    /// unique, so each name maps to the list of rows (in header order) that carry it.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<PeSection>>? TryReadPeSections(string path)
    {
        var output = OracleProcess.Run("llvm-readobj", "--sections", path);
        if (output is null) return null;

        var sections = new Dictionary<string, List<PeSection>>(StringComparer.Ordinal);

        string? name = null;
        ulong? vaddr = null, vsize = null, rawSize = null, rawPtr = null;

        void Flush()
        {
            if (name is null || vaddr is null || vsize is null || rawSize is null || rawPtr is null)
                return;

            var section = new PeSection(name, vaddr.Value, vsize.Value, rawPtr.Value, rawSize.Value);
            if (!sections.TryGetValue(name, out var list))
                sections[name] = list = [];
            list.Add(section);
        }

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            var nameMatch = NameRegex().Match(line);
            if (nameMatch.Success)
            {
                Flush(); // emit the previous block before starting a new one
                name = nameMatch.Groups["name"].Value;
                vaddr = vsize = rawSize = rawPtr = null;
                continue;
            }

            if (VirtualSizeRegex().Match(line) is { Success: true } vs) vsize = ParseNum(vs.Groups["v"].Value);
            else if (VirtualAddressRegex().Match(line) is { Success: true } va) vaddr = ParseNum(va.Groups["v"].Value);
            else if (RawDataSizeRegex().Match(line) is { Success: true } rs) rawSize = ParseNum(rs.Groups["v"].Value);
            else if (PointerToRawDataRegex().Match(line) is { Success: true } rp) rawPtr = ParseNum(rp.Groups["v"].Value);
        }

        Flush(); // last block

        return sections.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<PeSection>)kvp.Value, StringComparer.Ordinal);
    }

    /// <summary>One Mach-O section (<c>llvm-readobj --sections</c>).</summary>
    public readonly record struct MachOSection(string Name, ulong VirtualAddress, ulong VirtualSize, ulong FileOffset);

    [GeneratedRegex(@"^\s*Segment:\s+(?<seg>\S+)")]
    private static partial Regex SegmentRegex();

    [GeneratedRegex(@"^\s*Address:\s+(?<v>\S+)")]
    private static partial Regex AddressRegex();

    [GeneratedRegex(@"^\s*Size:\s+(?<v>\S+)")]
    private static partial Regex SizeRegex();

    [GeneratedRegex(@"^\s*Offset:\s+(?<v>\S+)")]
    private static partial Regex OffsetRegex();

    /// <summary>
    /// Returns the Mach-O section headers grouped by the <c>Segment,Name</c> composite key that
    /// <see cref="DotnetNativeMcp.Core.Imaging.MachOReader"/> uses as its section display name. Each
    /// composite name maps to the list of rows that carry it (Mach-O section names are not unique).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<MachOSection>>? TryReadMachOSections(string path)
    {
        var output = OracleProcess.Run("llvm-readobj", "--sections", path);
        if (output is null) return null;

        var sections = new Dictionary<string, List<MachOSection>>(StringComparer.Ordinal);

        string? name = null, segment = null;
        ulong? addr = null, size = null, offset = null;

        void Flush()
        {
            if (name is null || segment is null || addr is null || size is null || offset is null)
                return;

            // MachOReader composes the display name as "{segName},{sectName}" (empty segment falls back to name).
            var displayName = segment.Length == 0 ? name : $"{segment},{name}";
            var section = new MachOSection(displayName, addr.Value, size.Value, offset.Value);
            if (!sections.TryGetValue(displayName, out var list))
                sections[displayName] = list = [];
            list.Add(section);
        }

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            // llvm-readobj prints "Name: __text (5F 5F ...)"; strip the trailing hex-byte gloss.
            var nameMatch = NameRegex().Match(line);
            if (nameMatch.Success)
            {
                Flush(); // emit the previous block before starting a new one
                name = nameMatch.Groups["name"].Value;
                segment = null;
                addr = size = offset = null;
                continue;
            }

            if (SegmentRegex().Match(line) is { Success: true } sg) segment = sg.Groups["seg"].Value;
            else if (AddressRegex().Match(line) is { Success: true } ad) addr = ParseNum(ad.Groups["v"].Value);
            else if (SizeRegex().Match(line) is { Success: true } sz) size = ParseNum(sz.Groups["v"].Value);
            else if (OffsetRegex().Match(line) is { Success: true } of) offset = ParseNum(of.Groups["v"].Value);
        }

        Flush(); // last block

        return sections.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<MachOSection>)kvp.Value, StringComparer.Ordinal);
    }

    // llvm-readobj prints addresses/offsets in hex (0x-prefixed) but sizes in decimal.
    private static ulong ParseNum(string value) =>
        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : ulong.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
}
