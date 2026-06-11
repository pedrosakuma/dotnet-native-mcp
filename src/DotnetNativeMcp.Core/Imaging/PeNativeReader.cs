using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Symbols;

namespace DotnetNativeMcp.Core.Imaging;

/// <summary>
/// PE-native binary reader using <see cref="PEReader"/>.
/// Supports NativeAOT PE (export-table heuristic) and ReadyToRun detection.
/// </summary>
public static partial class PeNativeReader
{
    private static readonly string[] NativeAotExports =
    [
        "RhpNewFast", "RhpAssignRef", "RhpNewArray",
        "RhEHEnum", "RhpAssignRefAVLocation",
    ];

    /// <summary>
    /// Parses a PE binary from raw bytes and returns a <see cref="NativeImage"/>.
    /// Returns <c>null</c> if the bytes are not a valid PE.
    /// </summary>
    public static NativeImage? Read(ReadOnlyMemory<byte> rawBytes, string filePath)
    {
        if (rawBytes.Length < 2) return null;
        var span = rawBytes.Span;
        if (span[0] != 0x4D || span[1] != 0x5A) return null; // MZ

        PEReader pe;
        try
        {
            var ms = new System.IO.MemoryStream(rawBytes.ToArray());
            pe = new PEReader(ms, PEStreamOptions.PrefetchEntireImage);
        }
        catch
        {
            return null;
        }

        using (pe)
        {
            try
            {
                if (!pe.HasMetadata && !HasExportDirectory(pe))
                    return null;

                var arch = pe.PEHeaders.CoffHeader.Machine switch
                {
                    Machine.Amd64 => Architecture.X64,
                    Machine.I386 => Architecture.X86,
                    Machine.Arm64 => Architecture.Arm64,
                    _ => Architecture.Unknown,
                };

                var sections = ReadSections(pe);
                var symbols = ReadExports(pe, rawBytes.Span);
                var imageBase = (ulong)(pe.PEHeaders.PEHeader?.ImageBase ?? 0);

                var buildIdHex = Identity.BuildId.Extract(rawBytes.Span, filePath);
                var handle = ImageHandle.From(buildIdHex, System.IO.Path.GetFileName(filePath));

                return new NativeImage(
                    handle, filePath, BinaryFormat.Pe, arch,
                    sections, symbols, rawBytes, imageBase);
            }
            catch (Exception ex) when (ex is BadImageFormatException or System.IO.IOException)
            {
                // Malformed or truncated PE — lazy header parsing surfaces here. Treat as "not a PE we can read".
                return null;
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the PE looks like a managed-native build (NativeAOT or ReadyToRun).
    /// </summary>
    public static bool LooksLikeManagedNativeBuild(NativeImage image, ReadOnlySpan<byte> bytes)
    {
        // ReadyToRun: managed PE with non-empty ManagedNativeHeaderDirectory
        try
        {
            using var ms = new System.IO.MemoryStream(bytes.ToArray());
            using var pe = new PEReader(ms, PEStreamOptions.Default);
            if (pe.HasMetadata)
            {
                var corHeader = pe.PEHeaders.CorHeader;
                if (corHeader != null &&
                    corHeader.ManagedNativeHeaderDirectory.Size > 0)
                {
                    return true;
                }
            }
        }
        catch { /* ignore */ }

        // NativeAOT: characteristic export symbols or mangled names
        foreach (var sym in image.Symbols)
        {
            if (Array.IndexOf(NativeAotExports, sym.Name) >= 0) return true;
            if (sym.Name.StartsWith("S_P_", StringComparison.Ordinal)) return true;
        }

        // Check section names for managed markers
        foreach (var sec in image.Sections)
        {
            if (sec.Name is ".managed" or "hydrated") return true;
        }

        return false;
    }

    private static bool HasExportDirectory(PEReader pe)
    {
        try
        {
            return pe.PEHeaders.PEHeader?.ExportTableDirectory.Size > 0;
        }
        catch
        {
            return false;
        }
    }

    private static List<NativeSection> ReadSections(PEReader pe)
    {
        var result = new List<NativeSection>();
        foreach (var sec in pe.PEHeaders.SectionHeaders)
        {
            var name = sec.Name;
            result.Add(new NativeSection(
                name,
                (ulong)sec.VirtualAddress,
                (ulong)sec.VirtualSize,
                (ulong)sec.PointerToRawData,
                (ulong)sec.SizeOfRawData));
        }
        return result;
    }

    private static List<NativeSymbol> ReadExports(PEReader pe, ReadOnlySpan<byte> bytes)
    {
        var result = new List<NativeSymbol>();
        try
        {
            var exportDir = pe.PEHeaders.PEHeader?.ExportTableDirectory;
            if (exportDir is null || exportDir.Value.Size == 0) return result;

            // Export Directory: IMAGE_EXPORT_DIRECTORY
            // NumberOfFunctions(4), NumberOfNames(4),
            // AddressOfFunctions(4), AddressOfNames(4), AddressOfNameOrdinals(4)
            var exportRva = (uint)exportDir.Value.RelativeVirtualAddress;
            var edOffset = RvaToOffset(pe, bytes, exportRva);
            if (edOffset < 0 || edOffset + 40 > bytes.Length) return result;

            var ed = bytes[edOffset..];
            var numberOfFunctions = BinaryPrimitives.ReadUInt32LittleEndian(ed[16..]);
            var numberOfNames = BinaryPrimitives.ReadUInt32LittleEndian(ed[20..]);
            var addrOfFunctions = BinaryPrimitives.ReadUInt32LittleEndian(ed[28..]);
            var addrOfNames = BinaryPrimitives.ReadUInt32LittleEndian(ed[32..]);
            var addrOfNameOrdinals = BinaryPrimitives.ReadUInt32LittleEndian(ed[36..]);

            var funcsOffset = RvaToOffset(pe, bytes, addrOfFunctions);
            var namesOffset = RvaToOffset(pe, bytes, addrOfNames);
            var ordinalsOffset = RvaToOffset(pe, bytes, addrOfNameOrdinals);

            if (funcsOffset < 0 || namesOffset < 0 || ordinalsOffset < 0) return result;

            for (var i = 0; i < (int)numberOfNames; i++)
            {
                var nameRvaOffset = namesOffset + i * 4;
                var ordinalOffset = ordinalsOffset + i * 2;
                if (nameRvaOffset + 4 > bytes.Length || ordinalOffset + 2 > bytes.Length) break;

                var nameRva = BinaryPrimitives.ReadUInt32LittleEndian(bytes[nameRvaOffset..]);
                var ordinal = BinaryPrimitives.ReadUInt16LittleEndian(bytes[ordinalOffset..]);

                var nameOffset = RvaToOffset(pe, bytes, nameRva);
                if (nameOffset < 0) continue;
                var symName = ReadCString(bytes, nameOffset);
                if (string.IsNullOrEmpty(symName)) continue;

                ulong funcRva = 0;
                var funcRvaOffset = funcsOffset + ordinal * 4;
                if (funcRvaOffset + 4 <= bytes.Length)
                    funcRva = BinaryPrimitives.ReadUInt32LittleEndian(bytes[funcRvaOffset..]);

                var demangled = NativeAotSymbolDemangler.Demangle(symName);
                result.Add(new NativeSymbol(i, symName, demangled, funcRva, 0, null, true));
            }
        }
        catch { /* best-effort */ }
        return result;
    }

    private static int RvaToOffset(PEReader pe, ReadOnlySpan<byte> bytes, uint rva)
    {
        foreach (var sec in pe.PEHeaders.SectionHeaders)
        {
            var start = (uint)sec.VirtualAddress;
            var end = start + (uint)sec.VirtualSize;
            if (rva >= start && rva < end)
            {
                var offset = (int)sec.PointerToRawData + (int)(rva - start);
                return offset < bytes.Length ? offset : -1;
            }
        }
        return -1;
    }

    private static string ReadCString(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset < 0 || offset >= bytes.Length) return string.Empty;
        var end = offset;
        while (end < bytes.Length && bytes[end] != 0) end++;
        return Encoding.ASCII.GetString(bytes[offset..end]);
    }
}
