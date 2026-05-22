using DotnetNativeMcp.Core.Identity;

namespace DotnetNativeMcp.Core.Imaging;

/// <summary>Binary format of the loaded native image.</summary>
public enum BinaryFormat
{
    /// <summary>ELF (Linux NativeAOT).</summary>
    Elf,

    /// <summary>PE (Windows NativeAOT or ReadyToRun).</summary>
    Pe,

    /// <summary>Mach-O (macOS NativeAOT).</summary>
    MachO,
}

/// <summary>
/// Represents a loaded native binary with its parsed metadata.
/// All heavy data is kept in memory so the file handle can be released.
/// </summary>
public sealed class NativeImage
{
    /// <summary>Stable opaque handle for this image.</summary>
    public ImageHandle Handle { get; }

    /// <summary>Absolute path to the binary on disk (for diagnostics only; do not trust across containers).</summary>
    public string FilePath { get; }

    /// <summary>Parsed binary format.</summary>
    public BinaryFormat Format { get; }

    /// <summary>CPU architecture.</summary>
    public Architecture Architecture { get; }

    /// <summary>Sections in this image.</summary>
    public IReadOnlyList<NativeSection> Sections { get; }

    /// <summary>Symbols resolved from .map sidecar, ELF symtab/dynsym, or PE export table.</summary>
    public IReadOnlyList<NativeSymbol> Symbols { get; }

    /// <summary>Raw bytes of the entire file (needed for disassembly).</summary>
    public ReadOnlyMemory<byte> RawBytes { get; }

    /// <summary>Image load base (ELF: first PT_LOAD p_vaddr; PE: ImageBase from Optional header). Used to translate RVAs to virtual addresses.</summary>
    public ulong ImageBase { get; }

    /// <summary>
    /// Initialises a fully-loaded <see cref="NativeImage"/>.
    /// </summary>
    public NativeImage(
        ImageHandle handle,
        string filePath,
        BinaryFormat format,
        Architecture architecture,
        IReadOnlyList<NativeSection> sections,
        IReadOnlyList<NativeSymbol> symbols,
        ReadOnlyMemory<byte> rawBytes,
        ulong imageBase)
    {
        Handle = handle;
        FilePath = filePath;
        Format = format;
        Architecture = architecture;
        Sections = sections;
        Symbols = symbols;
        RawBytes = rawBytes;
        ImageBase = imageBase;
    }

    /// <summary>Finds the section that contains the given RVA, or <c>null</c>.</summary>
    public NativeSection? FindSection(ulong rva)
    {
        foreach (var s in Sections)
        {
            if (rva >= s.VirtualAddress && rva < s.VirtualAddress + s.VirtualSize)
                return s;
        }
        return null;
    }

    /// <summary>Gets a slice of the raw file bytes that corresponds to the given section.</summary>
    public ReadOnlyMemory<byte> GetSectionBytes(NativeSection section)
    {
        // Defensive bounds checks: attacker-controlled section headers can carry
        // file offsets / sizes that don't fit in `int` or that exceed the buffer.
        // Return empty instead of throwing so the rest of the tool surface stays
        // responsive.
        if (section.FileOffset > (ulong)RawBytes.Length || section.FileOffset > int.MaxValue)
            return ReadOnlyMemory<byte>.Empty;

        var start = (int)section.FileOffset;
        var available = (ulong)(RawBytes.Length - start);
        var len = (int)Math.Min(section.FileSize, available);
        return RawBytes.Slice(start, len);
    }

    /// <summary>Gets the file offset for the given RVA, or <c>null</c> if no section contains it.</summary>
    public int? RvaToFileOffset(ulong rva)
    {
        var section = FindSection(rva);
        if (section is null) return null;

        // Reject the (rva - VA) + FileOffset computation pre-emptively if it
        // would wrap in `ulong` — a crafted section with VirtualSize=ulong.MaxValue
        // could otherwise let a far-out RVA land on a bogus low file offset.
        var delta = rva - section.VirtualAddress;
        if (delta > ulong.MaxValue - section.FileOffset)
            return null;

        var offset = section.FileOffset + delta;
        if (offset > (ulong)RawBytes.Length || offset > int.MaxValue)
            return null;

        return (int)offset;
    }
}
