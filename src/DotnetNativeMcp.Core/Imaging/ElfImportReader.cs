using System.Buffers.Binary;
using DotnetNativeMcp.Core.Errors;

namespace DotnetNativeMcp.Core.Imaging;

public static partial class ElfReader
{
    private const uint SHT_DYNAMIC = 6;
    private const uint SHT_DYNSYM = 11;
    private const ushort SHN_UNDEF = 0;
    private const long DT_NULL = 0;
    private const long DT_NEEDED = 1;

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

    private static NativeResult<ElfImports> ReadImports(NativeImage image)
    {
        if (image.Format != BinaryFormat.Elf)
            return NativeResult.Fail<ElfImports>(ErrorKinds.InvalidArgument, $"Image '{image.Handle.Value}' is not an ELF binary.");

        try
        {
            var bytes = image.RawBytes.Span;
            if (!TryReadElfHeader(bytes, out var is64, out var shOff, out var shEntSize, out var shNum, out _))
                return NativeResult.Fail<ElfImports>(ErrorKinds.InternalError, "Failed to parse ELF imports.", "ELF section table is missing or malformed.");

            int dynamicIndex = -1;
            int dynsymIndex = -1;
            for (ushort sectionIndex = 0; sectionIndex < shNum; sectionIndex++)
            {
                var sectionType = ReadSectionType(bytes, is64, shOff, shEntSize, sectionIndex);
                if (sectionType == SHT_DYNAMIC)
                {
                    dynamicIndex = sectionIndex;
                    continue;
                }

                if (sectionType == SHT_DYNSYM)
                {
                    dynsymIndex = sectionIndex;
                }
            }

            var libraries = dynamicIndex >= 0
                ? ReadNeededLibraries(bytes, is64, shOff, shEntSize, shNum, (ushort)dynamicIndex)
                : [];
            var functions = dynsymIndex >= 0
                ? ReadUndefinedDynSymbols(bytes, is64, shOff, shEntSize, shNum, (ushort)dynsymIndex)
                : [];

            return NativeResult.Ok(
                $"Read {functions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} imported function(s) and {libraries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} library dependency(ies) from '{image.Handle.Value}'.",
                new ElfImports(libraries, functions));
        }
        catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException)
        {
            return NativeResult.Fail<ElfImports>(ErrorKinds.InternalError, "Failed to parse ELF imports.", ex.Message);
        }
    }

    private static List<ImportedLibrary> ReadNeededLibraries(
        ReadOnlySpan<byte> bytes,
        bool is64,
        ulong shOff,
        ushort shEntSize,
        ushort shNum,
        ushort dynamicSectionIndex)
    {
        var result = new List<ImportedLibrary>();
        var (dynamicOffset, dynamicSize) = ReadSectionHeader(bytes, is64, shOff, shEntSize, dynamicSectionIndex);
        var dynamicLink = ReadSectionLink(bytes, is64, shOff, shEntSize, dynamicSectionIndex);
        if (dynamicOffset == 0 || dynamicSize == 0 || dynamicOffset + dynamicSize > (ulong)bytes.Length || dynamicLink >= shNum)
            return result;

        var (stringOffset, stringSize) = ReadSectionHeader(bytes, is64, shOff, shEntSize, (ushort)dynamicLink);
        if (stringOffset == 0 || stringSize == 0 || stringOffset + stringSize > (ulong)bytes.Length)
            return result;

        var dynamicData = bytes[(int)dynamicOffset..(int)(dynamicOffset + dynamicSize)];
        var stringTable = bytes[(int)stringOffset..(int)(stringOffset + stringSize)];
        var entrySize = is64 ? 16 : 8;
        var entryCount = dynamicData.Length / entrySize;

        for (var entryIndex = 0; entryIndex < entryCount; entryIndex++)
        {
            var entry = dynamicData.Slice(entryIndex * entrySize, entrySize);
            var tag = is64
                ? BinaryPrimitives.ReadInt64LittleEndian(entry)
                : BinaryPrimitives.ReadInt32LittleEndian(entry);
            if (tag == DT_NULL)
                break;
            if (tag != DT_NEEDED)
                continue;

            var nameOffset = is64
                ? BinaryPrimitives.ReadUInt64LittleEndian(entry[8..])
                : BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            var name = ReadCString(stringTable, checked((int)nameOffset));
            if (!string.IsNullOrEmpty(name))
                result.Add(new ImportedLibrary(name));
        }

        return result;
    }

    private static List<ImportedFunction> ReadUndefinedDynSymbols(
        ReadOnlySpan<byte> bytes,
        bool is64,
        ulong shOff,
        ushort shEntSize,
        ushort shNum,
        ushort dynsymSectionIndex)
    {
        var result = new List<ImportedFunction>();
        var (symbolOffset, symbolSize) = ReadSectionHeader(bytes, is64, shOff, shEntSize, dynsymSectionIndex);
        var stringTableIndex = ReadSectionLink(bytes, is64, shOff, shEntSize, dynsymSectionIndex);
        if (symbolOffset == 0 || symbolSize == 0 || symbolOffset + symbolSize > (ulong)bytes.Length || stringTableIndex >= shNum)
            return result;

        var (stringOffset, stringSize) = ReadSectionHeader(bytes, is64, shOff, shEntSize, (ushort)stringTableIndex);
        if (stringOffset == 0 || stringSize == 0 || stringOffset + stringSize > (ulong)bytes.Length)
            return result;

        var symbolData = bytes[(int)symbolOffset..(int)(symbolOffset + symbolSize)];
        var stringTable = bytes[(int)stringOffset..(int)(stringOffset + stringSize)];
        var entrySize = is64 ? 24 : 16;
        var entryCount = symbolData.Length / entrySize;

        for (var entryIndex = 0; entryIndex < entryCount; entryIndex++)
        {
            var entry = symbolData.Slice(entryIndex * entrySize, entrySize);
            uint nameIndex;
            ushort sectionIndex;
            if (is64)
            {
                nameIndex = BinaryPrimitives.ReadUInt32LittleEndian(entry);
                sectionIndex = BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]);
            }
            else
            {
                nameIndex = BinaryPrimitives.ReadUInt32LittleEndian(entry);
                sectionIndex = BinaryPrimitives.ReadUInt16LittleEndian(entry[14..]);
            }

            if (sectionIndex != SHN_UNDEF)
                continue;

            var name = ReadCString(stringTable, checked((int)nameIndex));
            if (!string.IsNullOrEmpty(name))
                result.Add(new ImportedFunction(null, name, null));
        }

        return result;
    }

    private static bool TryReadElfHeader(
        ReadOnlySpan<byte> bytes,
        out bool is64,
        out ulong shOff,
        out ushort shEntSize,
        out ushort shNum,
        out ushort shStrNdx)
    {
        is64 = false;
        shOff = 0;
        shEntSize = 0;
        shNum = 0;
        shStrNdx = 0;

        if (bytes.Length < 16 || bytes[0] != 0x7F || bytes[1] != (byte)'E' || bytes[2] != (byte)'L' || bytes[3] != (byte)'F')
            return false;
        if (bytes[5] != 1)
            return false;

        is64 = bytes[4] == 2;
        if (is64)
        {
            if (bytes.Length < 64)
                return false;

            shOff = BinaryPrimitives.ReadUInt64LittleEndian(bytes[40..]);
            shEntSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes[58..]);
            shNum = BinaryPrimitives.ReadUInt16LittleEndian(bytes[60..]);
            shStrNdx = BinaryPrimitives.ReadUInt16LittleEndian(bytes[62..]);
        }
        else
        {
            if (bytes.Length < 52)
                return false;

            shOff = BinaryPrimitives.ReadUInt32LittleEndian(bytes[32..]);
            shEntSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes[46..]);
            shNum = BinaryPrimitives.ReadUInt16LittleEndian(bytes[48..]);
            shStrNdx = BinaryPrimitives.ReadUInt16LittleEndian(bytes[50..]);
        }

        return shOff != 0 && shEntSize != 0 && shNum != 0;
    }

    private sealed record ElfImports(
        IReadOnlyList<ImportedLibrary> Libraries,
        IReadOnlyList<ImportedFunction> Functions);
}
