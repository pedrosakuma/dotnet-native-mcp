using DotnetNativeMcp.Core.Imaging;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Differential ("oracle") harness for the Mach-O symbol reader: parses a committed Mach-O object both
/// with <see cref="MachOReader"/> and with LLVM <c>llvm-readobj --syms</c>, then asserts the two agree
/// on the defined-symbol set (name + value).
///
/// Mirrors <see cref="ElfSymbolDifferentialTests"/> for the Mach-O nlist symbol table, closing the gap
/// where the Mach-O harness previously covered only section geometry. The fixtures are tiny, committed
/// relocatable objects (see <c>tests/fixtures/MachO/README.md</c>) covering both the x86_64 and arm64
/// code paths. Comparison is by name (the macOS leading <c>_</c> stripped on both sides) and value;
/// size and the function flag are not compared because Mach-O nlist entries carry neither a reliable
/// size nor a reliable function discriminator, so <see cref="MachOReader"/> reports size 0 and
/// <c>IsFunction = true</c> uniformly. The test no-ops when the fixture or <c>llvm-readobj</c> is
/// unavailable, so it stays green on hosts without LLVM. See docs/differential-testing.md.
/// </summary>
public sealed class MachOSymbolDifferentialTests
{
    [Fact]
    public void Read_X64MachOObject_SymbolsMatchLlvmReadobj()
    {
        var path = FixturePaths.MachOX64Object;
        if (path is null) return;

        AssertMachOSymbolsMatch(path);
    }

    [Fact]
    public void Read_Arm64MachOObject_SymbolsMatchLlvmReadobj()
    {
        var path = FixturePaths.MachOArm64Object;
        if (path is null) return;

        AssertMachOSymbolsMatch(path);
    }

    [Fact]
    public void Read_Arm64RichMachOObject_SymbolsMatchLlvmReadobj()
    {
        var path = FixturePaths.MachOArm64RichObject;
        if (path is null) return;

        AssertMachOSymbolsMatch(path);
    }

    private static void AssertMachOSymbolsMatch(string path)
    {
        var image = MachOReader.Read(new ReadOnlyMemory<byte>(File.ReadAllBytes(path)), path);
        image.Should().NotBeNull($"MachOReader should parse '{Path.GetFileName(path)}'");

        var oracle = LlvmReadobjOracle.TryReadMachOSymbols(path);
        if (oracle is null) return; // llvm-readobj unavailable → skip
        oracle.Should().NotBeEmpty($"llvm-readobj should report defined symbols for '{Path.GetFileName(path)}'");

        // Compare the defined-symbol set as a multiset of (name, value) tuples so a wrong count or a
        // single mismatched value can't be masked. MachOReader emits a non-contiguous nlist index
        // (it skips STAB/undefined entries), so there is no shared index to key on — name+value is the
        // stable identity both readers agree on.
        var ours = image!.Symbols
            .Select(s => (s.Name, s.Rva))
            .ToList();
        var expected = oracle
            .Select(s => (s.Name, Rva: s.Value))
            .ToList();

        ours.Should().BeEquivalentTo(expected,
            $"MachOReader and llvm-readobj must agree on the defined-symbol set of '{Path.GetFileName(path)}'");
    }
}
