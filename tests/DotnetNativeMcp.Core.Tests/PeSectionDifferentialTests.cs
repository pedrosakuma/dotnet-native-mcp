using DotnetNativeMcp.Core.Imaging;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Differential ("oracle") harness for the PE reader: parses a real PE both with
/// <see cref="PeNativeReader"/> and with LLVM <c>llvm-readobj --sections</c>, then asserts the two
/// agree on every section's geometry (virtual address, virtual size, file offset, file size).
///
/// The primary target is this test run's own <c>DotnetNativeMcp.Core.dll</c> — a managed PE that is
/// always present next to the test assembly, so the comparison runs everywhere rather than skipping
/// on a missing fixture. A second test additionally exercises the published ReadyToRun
/// <c>System.Private.CoreLib.dll</c> (the real asm-mcp → native-mcp handoff target) when present.
///
/// The test no-ops when <c>llvm-readobj</c> is unavailable, so it stays green on hosts without LLVM.
/// See docs/differential-testing.md.
/// </summary>
public class PeSectionDifferentialTests
{
    [Fact]
    public void Read_CoreLibraryManagedPe_SectionsMatchLlvmReadobj()
    {
        // The Core assembly is a real managed PE that is always on disk beside the test binary.
        var path = typeof(NativeImage).Assembly.Location;
        if (!File.Exists(path)) return;

        AssertPeSectionsMatch(path);
    }

    [Fact]
    public void Read_ReadyToRunSystemPrivateCoreLib_SectionsMatchLlvmReadobj()
    {
        var path = FixturePaths.SystemPrivateCoreLib;
        if (path is null) return;

        AssertPeSectionsMatch(path);
    }

    private static void AssertPeSectionsMatch(string path)
    {
        var image = PeNativeReader.Read(new ReadOnlyMemory<byte>(File.ReadAllBytes(path)), path);
        image.Should().NotBeNull($"PeNativeReader should parse '{Path.GetFileName(path)}'");

        var oracle = LlvmReadobjOracle.TryReadPeSections(path);
        if (oracle is null) return; // llvm-readobj unavailable → skip
        oracle.Should().NotBeEmpty($"llvm-readobj should report sections for '{Path.GetFileName(path)}'");

        var ours = image!.Sections
            .GroupBy(s => s.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        ours.Keys.Should().BeEquivalentTo(oracle.Keys,
            $"PeNativeReader and llvm-readobj must surface the same section names in '{Path.GetFileName(path)}'");

        foreach (var (sectionName, references) in oracle)
        {
            ours.TryGetValue(sectionName, out var ourSections).Should().BeTrue(
                $"PeNativeReader should surface section '{sectionName}'");
            ourSections!.Count.Should().Be(references.Count,
                $"section '{sectionName}' should appear the same number of times");

            if (references.Count == 1)
            {
                var actual = ourSections[0];
                var reference = references[0];
                actual.VirtualAddress.Should().Be(reference.VirtualAddress, $"virtual address of '{sectionName}'");
                actual.VirtualSize.Should().Be(reference.VirtualSize, $"virtual size of '{sectionName}'");
                actual.FileOffset.Should().Be(reference.FileOffset, $"file offset of '{sectionName}'");
                actual.FileSize.Should().Be(reference.FileSize, $"file size of '{sectionName}'");
            }
            else
            {
                // Duplicate section names: fall back to a geometry-existence match (robust to ordering).
                foreach (var reference in references)
                    ourSections.Should().Contain(
                        actual => actual.VirtualAddress == reference.VirtualAddress
                            && actual.VirtualSize == reference.VirtualSize
                            && actual.FileOffset == reference.FileOffset
                            && actual.FileSize == reference.FileSize,
                        $"a section named '{sectionName}' with llvm-readobj's geometry should exist");
            }
        }
    }
}
