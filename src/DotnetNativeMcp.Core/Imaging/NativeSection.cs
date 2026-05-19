namespace DotnetNativeMcp.Core.Imaging;

/// <summary>A named section in a native binary (ELF section or PE section).</summary>
/// <param name="Name">Section name (e.g. <c>.text</c>, <c>.data</c>).</param>
/// <param name="VirtualAddress">Section RVA (relative to image base for PE, virtual address for ELF).</param>
/// <param name="VirtualSize">Size of the section in memory.</param>
/// <param name="FileOffset">Offset from the start of the file where raw data begins.</param>
/// <param name="FileSize">Size of raw data in the file.</param>
public sealed record NativeSection(
    string Name,
    ulong VirtualAddress,
    ulong VirtualSize,
    ulong FileOffset,
    ulong FileSize);
