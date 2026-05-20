namespace DotnetNativeMcp.Core.Xref;

/// <summary>
/// A single call-site that targets a specific native symbol or address.
/// </summary>
/// <param name="SourceAddressHex">Absolute virtual address of the calling instruction, as lowercase hex.</param>
/// <param name="CallerSymbol">Raw mangled name of the enclosing function, or <c>null</c> if the address cannot be attributed to a symbol.</param>
/// <param name="CallerDemangled">Best-effort demangled name of the enclosing function, or <c>null</c>.</param>
/// <param name="Mnemonic">Lowercase mnemonic of the transfer-of-control instruction (e.g. <c>call</c>, <c>jmp</c>, <c>je</c>).</param>
/// <param name="Operands">Formatted operand text (e.g. <c>0x401234</c>).</param>
/// <param name="RawBytes">Hex-encoded raw bytes of the instruction (e.g. <c>e8deadbeef</c>).</param>
public sealed record CallSite(
    string SourceAddressHex,
    string? CallerSymbol,
    string? CallerDemangled,
    string Mnemonic,
    string Operands,
    string RawBytes);
