namespace DotnetNativeMcp.Core.Imaging;

/// <summary>A symbol from an ELF symbol table, PE export table, or .map sidecar.</summary>
/// <param name="Index">Zero-based index in the symbol table.</param>
/// <param name="Name">Raw symbol name (possibly mangled).</param>
/// <param name="DemangledName">Best-effort human-readable demangled name; equals <see cref="Name"/> when demangling was not applicable.</param>
/// <param name="Rva">Relative virtual address (offset from image load base).</param>
/// <param name="Size">Size of the symbol in bytes (0 if unknown).</param>
/// <param name="Section">Name of the containing section, if resolved.</param>
/// <param name="IsFunction">True when the symbol is known to be a function.</param>
public sealed record NativeSymbol(
    int Index,
    string Name,
    string DemangledName,
    ulong Rva,
    ulong Size,
    string? Section,
    bool IsFunction);
