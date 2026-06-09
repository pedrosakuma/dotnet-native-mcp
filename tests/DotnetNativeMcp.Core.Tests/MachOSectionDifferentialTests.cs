using DotnetNativeMcp.Core.Imaging;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Differential ("oracle") harness for the Mach-O reader: parses a committed Mach-O object both with
/// <see cref="MachOReader"/> and with LLVM <c>llvm-readobj --sections</c>, then asserts the two agree
/// on every section's geometry (virtual address, virtual size, file offset).
///
/// The fixtures are tiny, committed relocatable objects (see <c>tests/fixtures/MachO/README.md</c>) so
/// the harness covers both the x86_64 and arm64 code paths of <see cref="MachOReader"/> without a
/// macOS cross-toolchain. The test no-ops when the fixture or <c>llvm-readobj</c> is unavailable, so
/// it stays green on hosts without LLVM. See docs/differential-testing.md.
/// </summary>
public class MachOSectionDifferentialTests
{
    [Fact]
    public void Read_X64MachOObject_SectionsMatchLlvmReadobj()
    {
        var path = FixturePaths.MachOX64Object;
        if (path is null) return;

        AssertMachOSectionsMatch(path);
    }

    [Fact]
    public void Read_Arm64MachOObject_SectionsMatchLlvmReadobj()
    {
        var path = FixturePaths.MachOArm64Object;
        if (path is null) return;

        AssertMachOSectionsMatch(path);
    }

    private static void AssertMachOSectionsMatch(string path)
    {
        var image = MachOReader.Read(new ReadOnlyMemory<byte>(File.ReadAllBytes(path)), path);
        image.Should().NotBeNull($"MachOReader should parse '{Path.GetFileName(path)}'");

        var oracle = LlvmReadobjOracle.TryReadMachOSections(path);
        if (oracle is null) return; // llvm-readobj unavailable → skip
        oracle.Should().NotBeEmpty($"llvm-readobj should report sections for '{Path.GetFileName(path)}'");

        var ours = image!.Sections
            .GroupBy(s => s.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        ours.Keys.Should().BeEquivalentTo(oracle.Keys,
            $"MachOReader and llvm-readobj must surface the same section names in '{Path.GetFileName(path)}'");

        foreach (var (sectionName, references) in oracle)
        {
            ours.TryGetValue(sectionName, out var ourSections).Should().BeTrue(
                $"MachOReader should surface section '{sectionName}'");
            ourSections!.Count.Should().Be(references.Count,
                $"section '{sectionName}' should appear the same number of times");

            if (references.Count == 1)
            {
                var actual = ourSections[0];
                var reference = references[0];
                actual.VirtualAddress.Should().Be(reference.VirtualAddress, $"virtual address of '{sectionName}'");
                actual.VirtualSize.Should().Be(reference.VirtualSize, $"virtual size of '{sectionName}'");
                actual.FileOffset.Should().Be(reference.FileOffset, $"file offset of '{sectionName}'");
                // MachOReader models a section's file size as its virtual size (section_64.size).
                actual.FileSize.Should().Be(reference.VirtualSize, $"file size of '{sectionName}'");
            }
            else
            {
                // Duplicate section names: compare as a multiset of full geometry tuples so a wrong
                // count or a single mismatched FileSize can't be masked by reusing one actual row.
                var actualTuples = ourSections
                    .Select(s => (s.VirtualAddress, s.VirtualSize, s.FileOffset, s.FileSize));
                var expectedTuples = references
                    .Select(r => (r.VirtualAddress, r.VirtualSize, r.FileOffset, FileSize: r.VirtualSize));
                actualTuples.Should().BeEquivalentTo(expectedTuples,
                    $"every section named '{sectionName}' should match llvm-readobj's geometry");
            }
        }
    }
}
