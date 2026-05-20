namespace DotnetNativeMcp.Core.Xref;

/// <summary>
/// A call-site found in one image that targets a symbol exported by a different image.
/// </summary>
/// <param name="SourceAddressHex">Absolute virtual address of the calling instruction in the caller image, lowercase hex.</param>
/// <param name="CallerSymbol">Raw mangled name of the enclosing function in the caller image, or <c>null</c>.</param>
/// <param name="CallerDemangled">Best-effort demangled name of the enclosing function, or <c>null</c>.</param>
/// <param name="Mnemonic">Lowercase mnemonic of the transfer-of-control instruction.</param>
/// <param name="Operands">Formatted operand text.</param>
/// <param name="RawBytes">Hex-encoded raw bytes of the instruction.</param>
/// <param name="CallerImageBuildId">Build-id (lowercase hex) of the image that contains this call site.</param>
/// <param name="CallerImagePath">Absolute path of the image that contains this call site.</param>
public sealed record CrossImageCallSite(
    string SourceAddressHex,
    string? CallerSymbol,
    string? CallerDemangled,
    string Mnemonic,
    string Operands,
    string RawBytes,
    string CallerImageBuildId,
    string CallerImagePath);
