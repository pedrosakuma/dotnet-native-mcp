using System.Globalization;
using DotnetNativeMcp.Core.Disassembly;
using DotnetNativeMcp.Core.Imaging;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Differential ("oracle") harness for the x86/x64 disassembler: decodes the same machine code
/// two ways — once through <see cref="IcedDisassembler"/> (via <see cref="RawDisassembler"/>) and
/// once through GNU <c>objdump -d -M intel</c> — and asserts they agree.
///
/// The hard oracle is <b>instruction-boundary + raw-byte</b> agreement: two independent decoders
/// walking the same bytes must segment them identically. A mismatch means one of them mis-sized an
/// instruction — the most dangerous class of decoder bug, which the existing fuzz harness (which
/// only proves "never throws") cannot catch. Mnemonics are compared as a softer signal.
///
/// The test no-ops when the NativeAOT fixture, its symbols, or <c>objdump</c> are unavailable, so
/// it stays green on hosts without binutils. See docs/differential-testing.md.
/// </summary>
public class ElfDisassemblyDifferentialTests
{
    private const int MaxFunctions = 40;
    private const ulong MinFunctionSize = 0x10;
    private const ulong MaxFunctionSize = 0x800;

    [Fact]
    public void Disassemble_SampleAotFunctions_MatchObjdumpBoundariesAndBytes()
    {
        var path = FixturePaths.SampleAot;
        if (path is null) return;

        var image = ElfReader.Read(new ReadOnlyMemory<byte>(File.ReadAllBytes(path)), path);
        if (image is null) return;
        if (image.Architecture != Architecture.X64) return; // objdump intel oracle targets x86/x64

        var functions = SelectFunctions(image);
        if (functions.Count == 0) return;

        var comparedFunctions = 0;
        var comparedInstructions = 0;
        var mnemonicMismatches = new List<string>();

        foreach (var fn in functions)
        {
            var startVa = image.ImageBase + fn.Rva;
            var stopVa = startVa + fn.Size;

            var oracle = ObjdumpOracle.TryDisassemble(path, startVa, stopVa);
            if (oracle is null) return; // objdump unavailable → skip the whole test
            if (oracle.Count == 0) continue;

            var ours = RawDisassembler.Disassemble(
                path, (int)fn.Rva, (int)fn.Size,
                arch: null, baseAddress: null,
                maxInstructions: IcedDisassembler.MaxInstructionsCap);
            ours.IsError.Should().BeFalse(ours.Error?.Message ?? string.Empty);

            comparedFunctions++;

            // Boundary oracle: both decoders must segment the same bytes into the same set of
            // instruction start addresses. Iterating only ours→objdump would silently pass if
            // Iced stopped decoding early, so assert the in-range address SETS are equal first.
            var ourAddresses = ours.Data!
                .Select(i => ulong.Parse(i.AddressHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
                .Where(a => a < stopVa)
                .ToHashSet();
            ourAddresses.Should().BeEquivalentTo(oracle.Keys,
                $"Iced and objdump must agree on instruction boundaries within {fn.Name} " +
                $"(0x{startVa:x}..0x{stopVa:x})");

            foreach (var insn in ours.Data!)
            {
                var addr = ulong.Parse(insn.AddressHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (addr >= stopVa) break;

                oracle.TryGetValue(addr, out var reference).Should().BeTrue(
                    $"objdump should decode an instruction at 0x{addr:x} in {fn.Name}");
                insn.Bytes.Should().Be(reference.Bytes,
                    $"raw bytes at 0x{addr:x} in {fn.Name} must match objdump");

                comparedInstructions++;

                if (!MnemonicsAgree(insn.Mnemonic, reference.Mnemonic))
                    mnemonicMismatches.Add(
                        $"0x{addr:x} {fn.Name}: ours='{insn.Mnemonic}' objdump='{reference.Mnemonic}' bytes={insn.Bytes}");
            }
        }

        comparedFunctions.Should().BeGreaterThan(0,
            "at least one function should have been cross-checked against objdump");
        comparedInstructions.Should().BeGreaterThan(50,
            "the harness should compare a meaningful number of instructions");
        mnemonicMismatches.Should().BeEmpty(
            "Iced and objdump should agree on mnemonics:\n" + string.Join("\n", mnemonicMismatches));
    }

    private static List<NativeSymbol> SelectFunctions(NativeImage image)
    {
        // ElfReader does not stamp NativeSymbol.Section, so bound functions to the .text
        // section's address range instead (objdump's intel decoder targets executable code).
        var text = image.Sections.FirstOrDefault(s => string.Equals(s.Name, ".text", StringComparison.Ordinal));
        if (text is null) return [];

        var textStart = text.VirtualAddress;
        var textEnd = text.VirtualAddress + text.VirtualSize;

        return image.Symbols
            .Where(s => s.IsFunction
                && s.Size >= MinFunctionSize
                && s.Size <= MaxFunctionSize
                && s.Rva >= textStart
                && s.Rva + s.Size <= textEnd)
            .GroupBy(s => s.Rva)
            .Select(g => g.First())
            .OrderBy(s => s.Rva)
            .Take(MaxFunctions)
            .ToList();
    }

    private static readonly HashSet<string> NopFamily = new(StringComparer.Ordinal) { "nop", "xchg" };

    private static bool MnemonicsAgree(string ours, string theirs)
    {
        if (string.Equals(ours, theirs, StringComparison.Ordinal))
            return true;

        // 0x66-prefixed and multi-byte NOP encodings: objdump prints them as `xchg ax,ax`,
        // `nop`, or `cs nop ...`; Iced canonicalizes the whole family to `nop`.
        return NopFamily.Contains(ours) && NopFamily.Contains(theirs);
    }
}
