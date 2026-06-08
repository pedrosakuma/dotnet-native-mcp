using DotnetNativeMcp.Core.Imaging;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Differential ("oracle") harness for the ELF section-header reader: asserts that every
/// section <see cref="ElfReader"/> emits matches <c>readelf -SW</c> on geometry
/// (virtual address, file offset, size).
///
/// No count assertion is made: <see cref="ElfReader"/> deliberately drops the NULL section
/// and any section with a zero file offset/size or whose data lies outside the file
/// (e.g. <c>SHT_NOBITS</c> like <c>.bss</c>). The strong invariant is that whatever it
/// <em>does</em> surface must be byte-for-byte correct. See docs/differential-testing.md.
/// </summary>
public sealed class ElfSectionDifferentialTests
{
    [Fact]
    public void ElfReader_Sections_MatchReadelf_OnSampleAotFixture() =>
        AssertSectionsMatch(FixturePaths.SampleAot);

    [Fact]
    public void ElfReader_Sections_MatchReadelf_OnSystemBinary() =>
        AssertSectionsMatch(File.Exists("/usr/bin/cat") ? "/usr/bin/cat" : null);

    private static void AssertSectionsMatch(string? path)
    {
        if (path is null) return; // fixture not built / not a Linux host — skip

        var oracle = ReadelfOracle.TryReadSections(path);
        if (oracle is null) return; // readelf unavailable — skip

        var image = ElfReader.Read(File.ReadAllBytes(path), path);
        image.Should().NotBeNull();
        image!.Sections.Should().NotBeEmpty("a real ELF must yield sections");

        var mismatches = new List<string>();
        foreach (var sec in image.Sections)
        {
            if (!oracle.TryGetValue(sec.Name, out var rows) || rows.Count == 0)
            {
                mismatches.Add($"section '{sec.Name}' not reported by readelf");
                continue;
            }

            if (rows.Count == 1)
            {
                // Unique name — emit precise per-field diagnostics.
                mismatches.AddRange(Diff(sec, rows[0]));
            }
            else if (!rows.Any(row => Diff(sec, row).Count == 0))
            {
                // Duplicate section name (legal but rare): require some readelf row of that
                // name to match on every field, since ElfReader's list order need not line up
                // with readelf's once filtered sections are dropped.
                mismatches.Add($"section '{sec.Name}': no readelf row of that name matches geometry " +
                    $"(addr 0x{sec.VirtualAddress:x}, off 0x{sec.FileOffset:x}, size 0x{sec.VirtualSize:x})");
            }
        }

        mismatches.Should().BeEmpty(
            "ElfReader and readelf must agree on every emitted section; first divergences:\n" +
            string.Join("\n", mismatches.Take(25)));
    }

    /// <summary>
    /// Returns the per-field divergences between an ElfReader section and a readelf row, or an
    /// empty sequence when they agree. <c>SHT_NOBITS</c> sections (e.g. <c>.bss</c>) occupy no
    /// file bytes, so their <c>FileSize</c> is not checked against readelf's <c>sh_size</c>
    /// (which is the in-memory size); ElfReader currently reports <c>sh_size</c> there, an
    /// imprecision that is out of scope for this harness.
    /// </summary>
    private static List<string> Diff(NativeSection sec, ReadelfOracle.Section row)
    {
        var diffs = new List<string>();

        if (sec.VirtualAddress != row.Address)
            diffs.Add($"'{sec.Name}': addr 0x{sec.VirtualAddress:x} != readelf 0x{row.Address:x}");
        if (sec.FileOffset != row.Offset)
            diffs.Add($"'{sec.Name}': offset 0x{sec.FileOffset:x} != readelf 0x{row.Offset:x}");
        if (sec.VirtualSize != row.Size)
            diffs.Add($"'{sec.Name}': vsize 0x{sec.VirtualSize:x} != readelf 0x{row.Size:x}");
        if (row.Type != "NOBITS" && sec.FileSize != row.Size)
            diffs.Add($"'{sec.Name}': fsize 0x{sec.FileSize:x} != readelf 0x{row.Size:x}");

        return diffs;
    }
}
