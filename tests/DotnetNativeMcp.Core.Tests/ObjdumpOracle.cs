using System.Globalization;
using System.Text.RegularExpressions;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Thin wrapper over GNU <c>objdump -d -M intel</c> used as an independent reference ("oracle")
/// for the disassembly differential harness. It decodes a virtual-address range and parses each
/// instruction line into an address → (raw bytes, mnemonic) shape comparable against
/// <c>IcedDisassembler</c> output.
///
/// Returns <c>null</c> when <c>objdump</c> is unavailable or exits non-zero, so the test skips
/// cleanly on hosts without binutils. See docs/differential-testing.md.
/// </summary>
internal static partial class ObjdumpOracle
{
    /// <summary>One decoded instruction: absolute address, concatenated lowercase-hex bytes, and the bare mnemonic.</summary>
    public readonly record struct Insn(ulong Address, string Bytes, string Mnemonic);

    // Instruction line, e.g. "    58c4:\te8 67 f8 ff ff       \tcall   5130 <abort@plt>".
    // The address is followed by ':' + a tab, the space-separated byte column, then a tab + disassembly.
    [GeneratedRegex(@"^\s*(?<addr>[0-9a-f]+):\t(?<bytes>[0-9a-f]{2}(?: [0-9a-f]{2})*)\s*\t(?<rest>.*\S)\s*$")]
    private static partial Regex InsnRegex();

    // objdump renders segment overrides, rep/lock and REX bytes as leading tokens before the
    // mnemonic; strip them so the bare mnemonic aligns with Iced's.
    private static readonly HashSet<string> Prefixes = new(StringComparer.Ordinal)
    {
        "lock", "rep", "repz", "repe", "repnz", "repne",
        "bnd", "notrack", "data16", "data32", "addr32",
        "cs", "ds", "es", "fs", "gs", "ss",
    };

    // objdump → Iced mnemonic aliases for instructions the two decoders name differently.
    private static readonly Dictionary<string, string> MnemonicAliases = new(StringComparer.Ordinal)
    {
        ["movabs"] = "mov",
    };

    /// <summary>
    /// Disassembles the half-open virtual-address range [<paramref name="startVa"/>,
    /// <paramref name="stopVa"/>) and returns the instructions indexed by address, or <c>null</c>
    /// when objdump is unavailable.
    /// </summary>
    public static IReadOnlyDictionary<ulong, Insn>? TryDisassemble(string path, ulong startVa, ulong stopVa)
    {
        var output = OracleProcess.Run(
            "objdump", "-d", "-M", "intel",
            "--insn-width=15", // x86 max instruction length: keep every instruction's bytes on one line (no column wrap)
            $"--start-address=0x{startVa:x}",
            $"--stop-address=0x{stopVa:x}",
            path);
        if (output is null) return null;

        var insns = new Dictionary<ulong, Insn>();
        foreach (var rawLine in output.Split('\n'))
        {
            var m = InsnRegex().Match(rawLine.TrimEnd('\r'));
            if (!m.Success) continue;

            var addr = ulong.Parse(m.Groups["addr"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var bytes = m.Groups["bytes"].Value.Replace(" ", string.Empty, StringComparison.Ordinal);
            var mnemonic = NormalizeMnemonic(m.Groups["rest"].Value);
            insns[addr] = new Insn(addr, bytes, mnemonic);
        }

        return insns;
    }

    /// <summary>
    /// Strips leading prefix tokens, takes the bare mnemonic, lowercases it, and maps known
    /// objdump→Iced aliases so the result is directly comparable to <c>InstructionView.Mnemonic</c>.
    /// </summary>
    public static string NormalizeMnemonic(string disassembly)
    {
        var tokens = disassembly.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            var lower = token.ToLowerInvariant();
            if (Prefixes.Contains(lower) || lower.StartsWith("rex", StringComparison.Ordinal))
                continue;

            return MnemonicAliases.TryGetValue(lower, out var alias) ? alias : lower;
        }

        return string.Empty;
    }
}
