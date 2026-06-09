using System.Globalization;
using System.Text.RegularExpressions;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Thin wrapper over LLVM <c>llvm-objdump -d</c> (ARM64 triple) used as an independent reference
/// ("oracle") for the ARM64 disassembly differential harness. It decodes a Mach-O object and parses
/// each instruction line into an address → (raw bytes, mnemonic) shape comparable against
/// <see cref="DotnetNativeMcp.Core.Disassembly.Arm64Disassembler"/> output.
///
/// <para>
/// ARM64 instructions are a fixed 4 bytes, so the x86 harness's "did we segment the bytes
/// identically" signal is trivially satisfied here; the value of this oracle is instead
/// <b>decode correctness</b> — that AsmArm64 names the same mnemonic for each 32-bit word.
/// </para>
///
/// Returns <c>null</c> when <c>llvm-objdump</c> is unavailable or exits non-zero, so the test skips
/// cleanly on hosts without LLVM. See docs/differential-testing.md.
/// </summary>
internal static partial class LlvmObjdumpArm64Oracle
{
    /// <summary>One decoded instruction: absolute address, file-order (little-endian) lowercase-hex bytes, and the bare mnemonic.</summary>
    public readonly record struct Insn(ulong Address, string Bytes, string Mnemonic);

    // Instruction line, e.g. "       0: 8b020020     \tadd\tx0, x1, x2".
    // The address is followed by ':' then the 8-hex-digit instruction word, then the mnemonic.
    [GeneratedRegex(@"^\s*(?<addr>[0-9a-f]+):\s+(?<word>[0-9a-f]{8})\s+(?<mnem>\S+)")]
    private static partial Regex InsnRegex();

    /// <summary>
    /// Disassembles the whole binary at <paramref name="path"/> with the given Apple ARM64 triple and
    /// returns the instructions indexed by address, or <c>null</c> when llvm-objdump is unavailable.
    /// </summary>
    public static IReadOnlyDictionary<ulong, Insn>? TryDisassemble(string path)
    {
        var output = OracleProcess.Run(
            "llvm-objdump", "-d", "--triple=arm64-apple-darwin", path);
        if (output is null) return null;

        var insns = new Dictionary<ulong, Insn>();
        foreach (var rawLine in output.Split('\n'))
        {
            var m = InsnRegex().Match(rawLine.TrimEnd('\r'));
            if (!m.Success) continue;

            var addr = ulong.Parse(m.Groups["addr"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            // llvm-objdump prints the instruction as a big-endian 32-bit word; the reader's
            // InstructionView.Bytes is the little-endian file-byte order, so reverse to match.
            var bytes = WordToFileOrderHex(m.Groups["word"].Value);
            var mnemonic = m.Groups["mnem"].Value.ToLowerInvariant();
            insns[addr] = new Insn(addr, bytes, mnemonic);
        }

        return insns;
    }

    // "8b020020" (word) → bytes [8b,02,00,20] → little-endian file order [20,00,02,8b] → "2000028b".
    private static string WordToFileOrderHex(string wordHex)
    {
        Span<char> result = stackalloc char[8];
        for (var i = 0; i < 4; i++)
        {
            var srcByte = 3 - i; // reverse byte order
            result[i * 2] = wordHex[srcByte * 2];
            result[(i * 2) + 1] = wordHex[(srcByte * 2) + 1];
        }

        return new string(result);
    }
}
