namespace DotnetNativeMcp.Core.Disassembly;

/// <summary>A cross-reference hint for a CALL or JMP instruction.</summary>
/// <param name="TargetAddressHex">Absolute target address in lowercase hex (16 chars for x64).</param>
/// <param name="ResolvedSymbol">Raw mangled symbol name at the target, if resolved.</param>
/// <param name="ResolvedDemangled">Demangled symbol name at the target, if resolved.</param>
public sealed record CrossRefHint(
    string TargetAddressHex,
    string? ResolvedSymbol,
    string? ResolvedDemangled);
