using DotnetNativeMcp.Core.Imaging;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Differential ("oracle") harness for the ELF import readers: asserts that
/// <see cref="ElfReader.ReadImportedLibraries"/> matches the <c>DT_NEEDED</c> entries from
/// <c>readelf -dW</c>, and that <see cref="ElfReader.ReadImportedFunctions"/> matches the
/// undefined (<c>UND</c>) <c>.dynsym</c> entries from <c>readelf -sW</c>.
///
/// Symbol version suffixes (<c>@GLIBC_x.y</c>) that readelf synthesizes for <c>.dynsym</c>
/// are normalized away on both sides. Comparisons are multiset-equivalent (order-independent,
/// duplicate-aware). See docs/differential-testing.md.
/// </summary>
public sealed class ElfImportDifferentialTests
{
    [Fact]
    public void ElfReader_Imports_MatchReadelf_OnSampleAotFixture() =>
        AssertImportsMatch(FixturePaths.SampleAot);

    [Fact]
    public void ElfReader_Imports_MatchReadelf_OnSystemBinary() =>
        AssertImportsMatch(File.Exists("/usr/bin/cat") ? "/usr/bin/cat" : null);

    private static void AssertImportsMatch(string? path)
    {
        if (path is null) return; // fixture not built / not a Linux host — skip

        var oracleLibraries = ReadelfOracle.TryReadNeededLibraries(path);
        var oracleFunctions = ReadelfOracle.TryReadUndefinedDynamicFunctions(path);
        if (oracleLibraries is null || oracleFunctions is null) return; // readelf unavailable — skip

        var image = ElfReader.Read(File.ReadAllBytes(path), path);
        image.Should().NotBeNull();

        var libraries = ElfReader.ReadImportedLibraries(image!);
        var functions = ElfReader.ReadImportedFunctions(image!);
        libraries.IsError.Should().BeFalse();
        functions.IsError.Should().BeFalse();

        libraries.Data!.Select(lib => lib.Name)
            .Should().BeEquivalentTo(oracleLibraries,
                "ElfReader must report exactly the DT_NEEDED libraries readelf lists");

        functions.Data!.Select(fn => ReadelfOracle.NormalizeName(fn.Name))
            .Should().BeEquivalentTo(oracleFunctions,
                "ElfReader must report exactly the undefined .dynsym symbols readelf lists");
    }
}
