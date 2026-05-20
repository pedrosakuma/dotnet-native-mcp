using System.Buffers.Binary;
using DotnetNativeMcp.Core.Errors;

namespace DotnetNativeMcp.Core.Imaging;

public static partial class MachOReader
{
    private const uint LcLoadDylib = 0xC;

    /// <summary>Returns a list of functions imported by this Mach-O binary (undefined symbols).</summary>
    public static NativeResult<IReadOnlyList<ImportedFunction>> ReadImportedFunctions(NativeImage image)
    {
        var result = ReadImports(image);
        return result.IsError
            ? NativeResult.Fail<IReadOnlyList<ImportedFunction>>(result.Error!.Kind, result.Error.Message, result.Error.Detail)
            : NativeResult.Ok(result.Summary, (IReadOnlyList<ImportedFunction>)result.Data!.Functions, result.Hints);
    }

    /// <summary>Returns the list of dylibs declared in LC_LOAD_DYLIB commands.</summary>
    public static NativeResult<IReadOnlyList<ImportedLibrary>> ReadImportedLibraries(NativeImage image)
    {
        var result = ReadImports(image);
        return result.IsError
            ? NativeResult.Fail<IReadOnlyList<ImportedLibrary>>(result.Error!.Kind, result.Error.Message, result.Error.Detail)
            : NativeResult.Ok(result.Summary, (IReadOnlyList<ImportedLibrary>)result.Data!.Libraries, result.Hints);
    }

    private static NativeResult<MachOImports> ReadImports(NativeImage image)
    {
        if (image.Format != BinaryFormat.MachO)
            return NativeResult.Fail<MachOImports>(ErrorKinds.InvalidArgument,
                $"Image '{image.Handle.Value}' is not a Mach-O binary.");

        try
        {
            var bytes = image.RawBytes.Span;
            var magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            var is64 = magic == MachOMagic64Le;
            var headerSize = is64 ? 32 : 28;
            if (bytes.Length < headerSize)
                return NativeResult.Fail<MachOImports>(ErrorKinds.InternalError, "Mach-O header truncated.");

            var ncmds = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]);

            // Pass 1: collect LC_LOAD_DYLIB entries (1-based ordinals for two-level namespace)
            var dylibs = new List<string>();
            var cmdOffset = headerSize;
            for (var i = 0u; i < ncmds; i++)
            {
                if (cmdOffset + 8 > bytes.Length) break;
                var cmd = BinaryPrimitives.ReadUInt32LittleEndian(bytes[cmdOffset..]);
                var cmdsize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 4)..]);
                if (cmdsize < 8 || cmdOffset + cmdsize > (uint)bytes.Length) break;

                if (cmd == LcLoadDylib && cmdOffset + 12 <= bytes.Length)
                {
                    // dylib_command: cmd(4)+cmdsize(4)+name.offset(4 from cmd start)+...
                    var nameOff = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 8)..]);
                    var nameStart = cmdOffset + (int)nameOff;
                    if (nameStart < cmdOffset + (int)cmdsize && nameStart < bytes.Length)
                    {
                        var end = Math.Min((int)(cmdOffset + cmdsize), bytes.Length);
                        dylibs.Add(ReadCString(bytes[nameStart..end], 0));
                    }
                }

                cmdOffset += (int)cmdsize;
            }

            // Pass 2: collect undefined symbols (imports) from LC_SYMTAB
            var functions = ReadUndefinedSymbols(bytes, is64, headerSize, ncmds, dylibs);
            var libraries = dylibs.ConvertAll(d => new ImportedLibrary(d));

            var summary = $"{functions.Count} imported function(s) from {libraries.Count} dylib(s).";
            return NativeResult.Ok(summary, new MachOImports(functions, libraries), []);
        }
        catch (Exception ex)
        {
            return NativeResult.Fail<MachOImports>(ErrorKinds.InternalError,
                "Failed to parse Mach-O imports.", ex.ToString());
        }
    }

    private static List<ImportedFunction> ReadUndefinedSymbols(
        ReadOnlySpan<byte> bytes, bool is64, int headerSize, uint ncmds, List<string> dylibs)
    {
        var functions = new List<ImportedFunction>();
        var cmdOffset = headerSize;
        for (var i = 0u; i < ncmds; i++)
        {
            if (cmdOffset + 8 > bytes.Length) break;
            var cmd = BinaryPrimitives.ReadUInt32LittleEndian(bytes[cmdOffset..]);
            var cmdsize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 4)..]);
            if (cmdsize < 8 || cmdOffset + cmdsize > (uint)bytes.Length) break;

            if (cmd == 0x2) // LC_SYMTAB
            {
                if (cmdOffset + 24 > bytes.Length) break;
                var symoff = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 8)..]);
                var nsyms = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 12)..]);
                var stroff = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 16)..]);
                var strsize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cmdOffset + 20)..]);

                if (stroff + strsize > (uint)bytes.Length) break;
                var strtab = bytes[(int)stroff..(int)(stroff + strsize)];
                var entrySize = is64 ? 16u : 12u;

                for (var j = 0u; j < nsyms; j++)
                {
                    var symBase = (int)symoff + (int)(j * entrySize);
                    if (symBase + (int)entrySize > bytes.Length) break;

                    var nStrx = BinaryPrimitives.ReadUInt32LittleEndian(bytes[symBase..]);
                    var nType = bytes[symBase + 4];
                    var nDesc = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(symBase + 6)..]);

                    // Skip STAB entries
                    if ((nType & 0xE0) != 0) continue;
                    // Only undefined (imported) symbols
                    if ((nType & 0x0E) != 0) continue;

                    if (nStrx >= (uint)strtab.Length) continue;
                    var rawName = ReadCString(strtab, (int)nStrx);
                    var name = rawName.StartsWith('_') ? rawName[1..] : rawName;
                    if (name.Length == 0) continue;

                    // Two-level namespace: library ordinal = (n_desc >> 8) & 0xFF (1-based)
                    var libOrdinal = (nDesc >> 8) & 0xFF;
                    var library = (libOrdinal > 0 && libOrdinal <= dylibs.Count)
                        ? dylibs[libOrdinal - 1]
                        : null;

                    functions.Add(new ImportedFunction(library, name, null));
                }
                break; // only one LC_SYMTAB per Mach-O
            }

            cmdOffset += (int)cmdsize;
        }
        return functions;
    }
}

internal sealed record MachOImports(
    IReadOnlyList<ImportedFunction> Functions,
    IReadOnlyList<ImportedLibrary> Libraries);
