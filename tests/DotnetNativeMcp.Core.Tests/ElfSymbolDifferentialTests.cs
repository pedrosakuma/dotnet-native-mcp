using DotnetNativeMcp.Core.Imaging;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Differential ("oracle") harness for the ELF symbol reader: parses a binary with
/// <see cref="ElfReader"/> and with GNU <c>readelf -sW</c>, then asserts the two agree
/// on the symbol set (index, name, value, size, function flag).
///
/// The fuzz harness (<see cref="Fuzz.ParserFuzzTests"/>) proves the parser never throws;
/// this harness proves it produces the <em>correct</em> answer against an independent,
/// battle-tested reference implementation. See docs/differential-testing.md.
///
/// Tests no-op (pass) when <c>readelf</c> is not on PATH or the fixture is unbuilt, so the
/// suite stays green on hosts without binutils.
/// </summary>
public sealed class ElfSymbolDifferentialTests
{
    /// <summary>
    /// NativeAOT fixture: a clean ELF with a full <c>.symtab</c> (no SECTION/FILE symbols and
    /// no version suffixes on defined symbols), so an exact 1:1 comparison is meaningful.
    /// </summary>
    [Fact]
    public void ElfReader_Symbols_MatchReadelf_OnSampleAotFixture()
    {
        var path = FixturePaths.SampleAot;
        if (path is null) return; // fixture not built — skip

        var oracle = ReadelfOracle.TryReadSymbols(path);
        if (oracle is null) return; // readelf unavailable — skip

        var image = ElfReader.Read(File.ReadAllBytes(path), path);
        image.Should().NotBeNull();

        AssertSymbolsMatch(image!.Symbols, oracle);
    }

    /// <summary>
    /// A stock dynamically-linked system binary. Exercises the <c>.dynsym</c> fallback path
    /// (system binaries are usually stripped of <c>.symtab</c>) and the <c>@GLIBC_x.y</c>
    /// version-suffix normalization on imported symbols.
    /// </summary>
    [Fact]
    public void ElfReader_Symbols_MatchReadelf_OnSystemBinary()
    {
        const string path = "/usr/bin/cat";
        if (!File.Exists(path)) return; // not a Linux host — skip

        var oracle = ReadelfOracle.TryReadSymbols(path);
        if (oracle is null) return; // readelf unavailable — skip

        var image = ElfReader.Read(File.ReadAllBytes(path), path);
        image.Should().NotBeNull();

        AssertSymbolsMatch(image!.Symbols, oracle);
    }

    private static void AssertSymbolsMatch(
        IReadOnlyList<NativeSymbol> symbols,
        IReadOnlyDictionary<int, ReadelfOracle.Symbol> oracle)
    {
        var mismatches = new List<string>();

        foreach (var sym in symbols)
        {
            if (!oracle.TryGetValue(sym.Index, out var row))
            {
                mismatches.Add($"index {sym.Index} '{sym.Name}': no matching readelf row");
                continue;
            }

            // Compare version-independent base names. Symbol versioning is represented
            // inconsistently across tables: in a linked .symtab the '@VER' suffix is baked
            // into st_name (ElfReader returns it verbatim), whereas for .dynsym readelf
            // synthesizes '@VER (N)' from .gnu.version that is absent from st_name. Stripping
            // the version on both sides makes the comparison table-agnostic.
            // SECTION symbols carry a readelf-synthesized name (st_name is 0), which ElfReader
            // never emits, so skip the name check for them. FILE symbols, by contrast, have a
            // real st_name and ARE emitted by ElfReader, so they are compared normally.
            if (row.Type is not "SECTION")
            {
                var expectedName = ReadelfOracle.NormalizeName(row.Name);
                var actualName = ReadelfOracle.NormalizeName(sym.Name);
                if (!string.Equals(actualName, expectedName, StringComparison.Ordinal))
                    mismatches.Add($"index {sym.Index}: name '{actualName}' != readelf '{expectedName}'");
            }

            if (sym.Rva != row.Value)
                mismatches.Add($"index {sym.Index} '{sym.Name}': rva 0x{sym.Rva:x} != readelf 0x{row.Value:x}");

            if (sym.Size != row.Size)
                mismatches.Add($"index {sym.Index} '{sym.Name}': size {sym.Size} != readelf {row.Size}");

            var oracleIsFunc = row.Type == "FUNC";
            if (sym.IsFunction != oracleIsFunc)
                mismatches.Add($"index {sym.Index} '{sym.Name}': IsFunction {sym.IsFunction} != readelf FUNC={oracleIsFunc}");
        }

        // Every named symbol readelf reports (excluding the null symbol and synthesized
        // SECTION names) must be present in the ElfReader output.
        var expectedCount = oracle.Values.Count(r =>
            r.Type is not "SECTION" &&
            ReadelfOracle.NormalizeName(r.Name).Length > 0);

        symbols.Count.Should().Be(expectedCount,
            "ElfReader must emit exactly the named symbols readelf reports");

        mismatches.Should().BeEmpty(
            "ElfReader and readelf must agree on every symbol; first divergences:\n" +
            string.Join("\n", mismatches.Take(25)));
    }
}
