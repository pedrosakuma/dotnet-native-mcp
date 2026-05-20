namespace DotnetNativeMcp.Core.Symbols;

/// <summary>Source file and line number resolved from DWARF debug info or a portable PDB sidecar.</summary>
/// <param name="File">Absolute path to the source file as recorded in the debug info.</param>
/// <param name="StartLine">1-based starting line number.</param>
/// <param name="EndLine">1-based ending line number, or <c>null</c> when debug info records only a single line.</param>
/// <param name="SourceLinkUrl">URL computed from SourceLink JSON in the PDB, or <c>null</c> when unavailable. The server never fetches this URL.</param>
public sealed record SourceLocation(
    string File,
    int StartLine,
    int? EndLine,
    string? SourceLinkUrl);
