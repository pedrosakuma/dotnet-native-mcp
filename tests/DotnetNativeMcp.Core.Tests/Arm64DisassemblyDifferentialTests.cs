using System.Globalization;
using DotnetNativeMcp.Core.Disassembly;
using DotnetNativeMcp.Core.Imaging;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Differential ("oracle") harness for the ARM64 disassembler: decodes the same machine code two
/// ways — once through <see cref="Arm64Disassembler"/> (via <see cref="RawDisassembler"/>) and once
/// through LLVM <c>llvm-objdump</c> — and asserts they agree on every instruction's raw 32-bit word
/// and mnemonic.
///
/// Unlike the variable-length x86 harness (where instruction-boundary agreement is the headline
/// signal), ARM64 is fixed-width 4 bytes, so boundaries are trivial. The signal here is <b>decode
/// correctness</b>: AsmArm64 and LLVM must name the same mnemonic for each word. Operands are not
/// compared — the two render addresses/immediates differently (e.g. PC-relative offset vs resolved
/// target) — but the mnemonic (including the condition suffix on <c>b.&lt;cond&gt;</c>) must match.
///
/// The fixture is a committed Mach-O object with a diverse instruction mix (see
/// <c>tests/fixtures/MachO/README.md</c>). The test no-ops when the fixture or <c>llvm-objdump</c>
/// is unavailable, so it stays green on hosts without LLVM. See docs/differential-testing.md.
/// </summary>
public class Arm64DisassemblyDifferentialTests
{
    [Fact]
    public void Disassemble_Arm64RichObject_MatchesLlvmObjdumpWordsAndMnemonics()
    {
        var path = FixturePaths.MachOArm64RichObject;
        if (path is null) return;

        var image = MachOReader.Read(new ReadOnlyMemory<byte>(File.ReadAllBytes(path)), path);
        image.Should().NotBeNull($"MachOReader should parse '{Path.GetFileName(path)}'");
        image!.Architecture.Should().Be(Architecture.Arm64);

        var text = image.Sections.FirstOrDefault(s => string.Equals(s.Name, "__TEXT,__text", StringComparison.Ordinal));
        text.Should().NotBeNull("the fixture should carry a __TEXT,__text section");

        var oracle = LlvmObjdumpArm64Oracle.TryDisassemble(path);
        if (oracle is null) return; // llvm-objdump unavailable → skip
        oracle.Should().NotBeEmpty($"llvm-objdump should decode instructions in '{Path.GetFileName(path)}'");

        var ours = RawDisassembler.Disassemble(
            path, (int)text!.VirtualAddress, (int)text.VirtualSize,
            arch: null, baseAddress: null,
            maxInstructions: IcedDisassembler.MaxInstructionsCap);
        ours.IsError.Should().BeFalse(ours.Error?.Message ?? string.Empty);

        // Both decoders must walk the fixed-width stream into the same set of instruction addresses.
        var ourByAddress = ours.Data!.ToDictionary(
            i => ulong.Parse(i.AddressHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        ourByAddress.Keys.Should().BeEquivalentTo(oracle.Keys,
            "AsmArm64 and llvm-objdump must agree on instruction addresses");

        var mnemonicMismatches = new List<string>();
        foreach (var (addr, reference) in oracle)
        {
            var insn = ourByAddress[addr];

            insn.Bytes.Should().Be(reference.Bytes,
                $"raw instruction word at 0x{addr:x} must match llvm-objdump");

            if (!string.Equals(insn.Mnemonic, reference.Mnemonic, StringComparison.Ordinal))
                mnemonicMismatches.Add(
                    $"0x{addr:x}: ours='{insn.Mnemonic}' llvm-objdump='{reference.Mnemonic}' word={reference.Bytes}");
        }

        oracle.Count.Should().BeGreaterThan(30,
            "the diverse fixture should exercise a meaningful number of instructions");
        mnemonicMismatches.Should().BeEmpty(
            "AsmArm64 and llvm-objdump should agree on mnemonics:\n" + string.Join("\n", mnemonicMismatches));
    }
}
