using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using DotnetNativeMcp.Core.Errors;

namespace DotnetNativeMcp.Core.Imaging;

public static partial class PeNativeReader
{
    public static NativeResult<IReadOnlyList<ImportedFunction>> ReadImportedFunctions(NativeImage image)
    {
        var result = ReadImports(image);
        return result.IsError
            ? NativeResult.Fail<IReadOnlyList<ImportedFunction>>(result.Error!.Kind, result.Error.Message, result.Error.Detail)
            : NativeResult.Ok(result.Summary, (IReadOnlyList<ImportedFunction>)result.Data!.Functions, result.Hints);
    }

    public static NativeResult<IReadOnlyList<ImportedLibrary>> ReadImportedLibraries(NativeImage image)
    {
        var result = ReadImports(image);
        return result.IsError
            ? NativeResult.Fail<IReadOnlyList<ImportedLibrary>>(result.Error!.Kind, result.Error.Message, result.Error.Detail)
            : NativeResult.Ok(result.Summary, (IReadOnlyList<ImportedLibrary>)result.Data!.Libraries, result.Hints);
    }

    private static NativeResult<PeImports> ReadImports(NativeImage image)
    {
        if (image.Format != BinaryFormat.Pe)
            return NativeResult.Fail<PeImports>(ErrorKinds.InvalidArgument, $"Image '{image.Handle.Value}' is not a PE binary.");

        try
        {
            using var ms = new MemoryStream(image.RawBytes.ToArray());
            using var pe = new PEReader(ms, PEStreamOptions.PrefetchEntireImage);
            var importDirectory = pe.PEHeaders.PEHeader?.ImportTableDirectory;
            if (importDirectory is null || importDirectory.Value.Size == 0)
                return NativeResult.Ok("PE image has no import directory.", new PeImports([], []));

            var bytes = image.RawBytes.Span;
            var descriptorOffset = RvaToOffset(pe, bytes, (uint)importDirectory.Value.RelativeVirtualAddress);
            if (descriptorOffset < 0)
            {
                return NativeResult.Fail<PeImports>(
                    ErrorKinds.InternalError,
                    "Failed to parse PE import table.",
                    $"Import directory RVA 0x{importDirectory.Value.RelativeVirtualAddress:x8} does not map to a file offset.");
            }

            var libraries = new List<ImportedLibrary>();
            var functions = new List<ImportedFunction>();
            var is64 = pe.PEHeaders.PEHeader!.Magic == PEMagic.PE32Plus;
            var thunkSize = is64 ? 8 : 4;
            var ordinalFlag = is64 ? 0x8000000000000000UL : 0x80000000UL;

            for (var descriptorIndex = 0; ; descriptorIndex++)
            {
                var currentOffset = descriptorOffset + (descriptorIndex * 20);
                if (currentOffset + 20 > bytes.Length)
                {
                    return NativeResult.Fail<PeImports>(
                        ErrorKinds.InternalError,
                        "Failed to parse PE import table.",
                        $"Import descriptor #{descriptorIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)} extends past end of file.");
                }

                var descriptor = bytes[currentOffset..];
                var originalFirstThunk = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[0..]);
                var nameRva = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[12..]);
                var firstThunk = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[16..]);
                if (originalFirstThunk == 0 && nameRva == 0 && firstThunk == 0)
                    break;

                var nameOffset = RvaToOffset(pe, bytes, nameRva);
                if (nameOffset < 0)
                {
                    return NativeResult.Fail<PeImports>(
                        ErrorKinds.InternalError,
                        "Failed to parse PE import table.",
                        $"Import descriptor '{descriptorIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}' has an invalid library-name RVA 0x{nameRva:x8}.");
                }

                var library = ReadCString(bytes, nameOffset);
                if (string.IsNullOrEmpty(library))
                {
                    return NativeResult.Fail<PeImports>(
                        ErrorKinds.InternalError,
                        "Failed to parse PE import table.",
                        $"Import descriptor '{descriptorIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}' resolved to an empty library name.");
                }

                libraries.Add(new ImportedLibrary(library));

                var thunkRva = originalFirstThunk != 0 ? originalFirstThunk : firstThunk;
                if (thunkRva == 0)
                    continue;

                var thunkOffset = RvaToOffset(pe, bytes, thunkRva);
                if (thunkOffset < 0)
                {
                    return NativeResult.Fail<PeImports>(
                        ErrorKinds.InternalError,
                        "Failed to parse PE import table.",
                        $"Import descriptor '{library}' has an invalid thunk RVA 0x{thunkRva:x8}.");
                }

                for (var thunkIndex = 0; ; thunkIndex++)
                {
                    var entryOffset = thunkOffset + (thunkIndex * thunkSize);
                    if (entryOffset + thunkSize > bytes.Length)
                    {
                        return NativeResult.Fail<PeImports>(
                            ErrorKinds.InternalError,
                            "Failed to parse PE import table.",
                            $"Thunk #{thunkIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)} for '{library}' extends past end of file.");
                    }

                    var thunkValue = is64
                        ? BinaryPrimitives.ReadUInt64LittleEndian(bytes[entryOffset..])
                        : BinaryPrimitives.ReadUInt32LittleEndian(bytes[entryOffset..]);
                    if (thunkValue == 0)
                        break;

                    if ((thunkValue & ordinalFlag) != 0)
                    {
                        var ordinal = (ushort)(thunkValue & 0xFFFF);
                        functions.Add(new ImportedFunction(library, $"#{ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)}", ordinal));
                        continue;
                    }

                    var hintNameRva = (uint)thunkValue;
                    var hintNameOffset = RvaToOffset(pe, bytes, hintNameRva);
                    if (hintNameOffset < 0 || hintNameOffset + 2 > bytes.Length)
                    {
                        return NativeResult.Fail<PeImports>(
                            ErrorKinds.InternalError,
                            "Failed to parse PE import table.",
                            $"Import-by-name entry for '{library}' has an invalid RVA 0x{hintNameRva:x8}.");
                    }

                    var importName = ReadCString(bytes, hintNameOffset + 2);
                    if (string.IsNullOrEmpty(importName))
                    {
                        return NativeResult.Fail<PeImports>(
                            ErrorKinds.InternalError,
                            "Failed to parse PE import table.",
                            $"Import-by-name entry for '{library}' resolved to an empty function name.");
                    }

                    functions.Add(new ImportedFunction(library, importName, null));
                }
            }

            return NativeResult.Ok(
                $"Read {functions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} imported function(s) and {libraries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} library dependency(ies) from '{image.Handle.Value}'.",
                new PeImports(libraries, functions));
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or ArgumentOutOfRangeException)
        {
            return NativeResult.Fail<PeImports>(ErrorKinds.InternalError, "Failed to parse PE import table.", SanitisedError.From(ex));
        }
    }

    private sealed record PeImports(
        IReadOnlyList<ImportedLibrary> Libraries,
        IReadOnlyList<ImportedFunction> Functions);
}
