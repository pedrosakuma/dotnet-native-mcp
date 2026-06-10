using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;

namespace DotnetNativeMcp.Core.R2R;

/// <summary>
/// Parses ReadyToRun (R2R) metadata from a managed PE binary.
/// RuntimeFunctions decoding currently supports x64 and ARM64 layouts.
/// </summary>
public static class ReadyToRunReader
{
    /// <summary>R2R magic signature: ASCII "RTR\0" stored as little-endian uint32.</summary>
    private const uint R2RSignature = 0x00525452u;

    /// <summary>Size of a READYTORUN_HEADER (without the sections table).</summary>
    private const int HeaderByteSize = 16; // sig(4) + major(2) + minor(2) + flags(4) + count(4)

    /// <summary>Byte size of one READYTORUN_SECTION entry (type + RVA + Size).</summary>
    private const int SectionEntrySize = 12;

    /// <summary>Byte size of one x64 RUNTIME_FUNCTION entry (begin + end + unwindInfo).</summary>
    private const int RuntimeFunctionSizeX64 = 12;

    /// <summary>Byte size of one ARM64 pdata entry (begin + unwindData/xdata RVA).</summary>
    private const int RuntimeFunctionSizeArm64 = 8;

    private const uint Arm64PackedFlagMask = 0x3u;
    private const int Arm64PackedFunctionLengthShift = 2;
    private const uint Arm64PackedFunctionLengthMask = 0x7FFu;
    private const uint Arm64XdataFunctionLengthMask = 0x3FFFFu;
    private const int Arm64FunctionLengthScale = 4;

    private const ushort ImageFileMachineI386 = 0x014C;
    private const ushort ImageFileMachineAmd64 = 0x8664;
    private const ushort ImageFileMachineArm64 = 0xAA64;

    private static readonly ushort[] ReadyToRunMachineOsOverrides =
    [
        0x7B79, // Linux
        0x4644, // Apple
        0xADC4, // FreeBSD
        0x1993, // NetBSD
        0x1992, // SunOS
    ];

    private enum RuntimeFunctionLayout
    {
        X64,
        Arm64,
    }

    /// <summary>
    /// Reads the <c>READYTORUN_HEADER</c> from the managed-native header of a PE binary.
    /// </summary>
    public static NativeResult<ReadyToRunHeader> ReadHeader(NativeImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.Format != BinaryFormat.Pe)
            return NativeResult.Fail<ReadyToRunHeader>(
                ErrorKinds.R2RNotPresent,
                "ReadyToRun is only supported for PE binaries.");

        var bytes = image.RawBytes.Span;

        // Locate the ManagedNativeHeader directory via the CLR COM header.
        var mnh = FindManagedNativeHeader(bytes);
        if (mnh is null)
            return NativeResult.Fail<ReadyToRunHeader>(
                ErrorKinds.R2RNotPresent,
                "The binary does not have a CLR managed-native header (ManagedNativeHeaderDirectory is empty or absent). " +
                "This is either a pure managed assembly or a NativeAOT binary — not a ReadyToRun image.");

        var (mnh_fileOffset, mnh_size) = mnh.Value;

        if (mnh_fileOffset + HeaderByteSize > bytes.Length)
            return NativeResult.Fail<ReadyToRunHeader>(
                ErrorKinds.R2RNotPresent,
                "ManagedNativeHeader region is truncated.");

        var sig = BinaryPrimitives.ReadUInt32LittleEndian(bytes[mnh_fileOffset..]);
        if (sig != R2RSignature)
            return NativeResult.Fail<ReadyToRunHeader>(
                ErrorKinds.R2RNotPresent,
                $"ManagedNativeHeader does not start with the R2R signature (found 0x{sig:X8}, expected 0x{R2RSignature:X8}).");

        var major = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(mnh_fileOffset + 4)..]);
        var minor = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(mnh_fileOffset + 6)..]);

        // Minimum supported version
        if (major < 1)
            return NativeResult.Fail<ReadyToRunHeader>(
                ErrorKinds.R2RUnsupportedVersion,
                $"R2R version {major}.{minor} is not supported (minimum: 1.0).");

        var flags = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(mnh_fileOffset + 8)..]);
        var numSections = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[(mnh_fileOffset + 12)..]);

        if (numSections < 0 || numSections > 1024)
            return NativeResult.Fail<ReadyToRunHeader>(
                ErrorKinds.R2RNotPresent,
                $"Unexpected section count {numSections} — possibly a corrupt or non-R2R header.");

        var sectionsStart = mnh_fileOffset + HeaderByteSize;
        var sectionsEnd = sectionsStart + numSections * SectionEntrySize;
        if (sectionsEnd > bytes.Length)
            return NativeResult.Fail<ReadyToRunHeader>(
                ErrorKinds.R2RNotPresent,
                "Section table overflows the file — possibly a corrupt R2R header.");

        var sections = new List<ReadyToRunSection>(numSections);
        for (var i = 0; i < numSections; i++)
        {
            var entryOff = sectionsStart + i * SectionEntrySize;
            var sectionType = BinaryPrimitives.ReadUInt32LittleEndian(bytes[entryOff..]);
            var sectionRva = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(entryOff + 4)..]);
            var sectionSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(entryOff + 8)..]);

            var typeName = Enum.IsDefined(typeof(ReadyToRunSectionType), sectionType)
                ? ((ReadyToRunSectionType)sectionType).ToString()
                : sectionType.ToString(System.Globalization.CultureInfo.InvariantCulture);

            sections.Add(new ReadyToRunSection(sectionType, typeName, sectionRva, sectionSize));
        }

        return NativeResult.Ok(
            $"R2R header parsed: version {major}.{minor}, {numSections} sections.",
            new ReadyToRunHeader(major, minor, flags, sections));
    }

    /// <summary>
    /// Attempts to recover the native target architecture of a ReadyToRun PE from the
    /// COFF <c>Machine</c> field, including the CoreCLR per-OS override encoding used
    /// by managed-native images on non-Windows platforms.
    /// </summary>
    internal static Architecture? TryReadTargetArchitecture(NativeImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.Format != BinaryFormat.Pe)
            return null;

        var headerResult = ReadHeader(image);
        if (headerResult.IsError)
            return null;

        try
        {
            using var stream = new MemoryStream(image.RawBytes.ToArray(), writable: false);
            using var peReader = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);

            var rawMachine = (ushort)peReader.PEHeaders.CoffHeader.Machine;
            var directArchitecture = MapImageFileMachine(rawMachine);
            if (directArchitecture is not null)
                return directArchitecture;

            foreach (var osOverride in ReadyToRunMachineOsOverrides)
            {
                var decodedArchitecture = MapImageFileMachine((ushort)(rawMachine ^ osOverride));
                if (decodedArchitecture is not null)
                    return decodedArchitecture;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// Reads the <c>RuntimeFunctions</c> section from the R2R header.
    /// Returns paginated results; pass the <c>NextCursor</c> from the result to fetch the next page.
    /// Supports x64 and ARM64 layouts.
    /// </summary>
    public static NativeResult<ReadyToRunRuntimeFunctionsPage> ReadRuntimeFunctions(
        NativeImage image,
        ReadyToRunHeader header,
        int cursor = 0,
        int pageSize = 100)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(header);

        var layout = GetRuntimeFunctionLayout(image);
        if (layout is null)
            return NativeResult.Fail<ReadyToRunRuntimeFunctionsPage>(
                ErrorKinds.R2RArchUnsupported,
                $"Only x64 and ARM64 RuntimeFunctions layouts are supported; this image is {image.Architecture}.");

        var rtSection = header.FindSection(ReadyToRunSectionType.RuntimeFunctions);
        if (rtSection is null)
            return NativeResult.Fail<ReadyToRunRuntimeFunctionsPage>(
                ErrorKinds.R2RSectionNotPresent,
                "This R2R image does not contain a RuntimeFunctions section (type 102).");

        var entrySize = GetRuntimeFunctionEntrySize(layout.Value);
        var totalEntries = (int)(rtSection.Size / (uint)entrySize);
        if (totalEntries == 0)
            return NativeResult.Ok(
                "RuntimeFunctions section is empty.",
                new ReadyToRunRuntimeFunctionsPage([], 0, totalEntries, null));

        if (cursor < 0) cursor = 0;
        if (pageSize <= 0) pageSize = 100;
        if (pageSize > 500) pageSize = 500;

        if (cursor >= totalEntries)
            return NativeResult.Fail<ReadyToRunRuntimeFunctionsPage>(
                ErrorKinds.InvalidArgument,
                $"cursor {cursor} is beyond the table end ({totalEntries} entries).");

        var fileOffset = image.RvaToFileOffset(rtSection.VirtualAddress);
        if (fileOffset is null)
            return NativeResult.Fail<ReadyToRunRuntimeFunctionsPage>(
                ErrorKinds.InvalidArgument,
                $"RuntimeFunctions RVA 0x{rtSection.VirtualAddress:X8} could not be mapped to a file offset.");

        var take = Math.Min(pageSize, totalEntries - cursor);
        var results = new List<RuntimeFunction>(take);

        for (var i = 0; i < take; i++)
        {
            var idx = cursor + i;
            var functionResult = ReadRuntimeFunctionAtIndex(image, layout.Value, fileOffset.Value, idx);
            if (functionResult.IsError)
                return NativeResult.Fail<ReadyToRunRuntimeFunctionsPage>(
                    functionResult.Error!.Kind,
                    functionResult.Error.Message,
                    functionResult.Error.Detail);

            results.Add(functionResult.Data!);
        }

        int? nextCursor = cursor + results.Count < totalEntries ? cursor + results.Count : null;

        return NativeResult.Ok(
            $"Returned {results.Count} of {totalEntries} RuntimeFunction entries (page starting at {cursor}).",
            new ReadyToRunRuntimeFunctionsPage(results, cursor, totalEntries, nextCursor));
    }

    /// <summary>
    /// Finds the <c>RUNTIME_FUNCTION</c> entry whose range covers the given <paramref name="rva"/>
    /// (binary search on <c>BeginAddress</c>).
    /// </summary>
    public static NativeResult<RuntimeFunction> FindRuntimeFunction(
        NativeImage image,
        ReadyToRunHeader header,
        uint rva)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(header);

        var layout = GetRuntimeFunctionLayout(image);
        if (layout is null)
            return NativeResult.Fail<RuntimeFunction>(
                ErrorKinds.R2RArchUnsupported,
                $"Only x64 and ARM64 RuntimeFunctions layouts are supported; this image is {image.Architecture}.");

        var rtSection = header.FindSection(ReadyToRunSectionType.RuntimeFunctions);
        if (rtSection is null)
            return NativeResult.Fail<RuntimeFunction>(
                ErrorKinds.R2RSectionNotPresent,
                "This R2R image does not contain a RuntimeFunctions section (type 102).");

        var fileOffset = image.RvaToFileOffset(rtSection.VirtualAddress);
        if (fileOffset is null)
            return NativeResult.Fail<RuntimeFunction>(
                ErrorKinds.InvalidArgument,
                $"RuntimeFunctions RVA 0x{rtSection.VirtualAddress:X8} could not be mapped to a file offset.");

        var totalEntries = (int)(rtSection.Size / (uint)GetRuntimeFunctionEntrySize(layout.Value));

        // Binary search on BeginAddress
        var lo = 0;
        var hi = totalEntries - 1;
        RuntimeFunction? best = null;

        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            var functionResult = ReadRuntimeFunctionAtIndex(image, layout.Value, fileOffset.Value, mid);
            if (functionResult.IsError)
                return functionResult;

            var candidate = functionResult.Data!;

            if (rva < candidate.BeginAddress)
            {
                hi = mid - 1;
            }
            else if (rva >= candidate.EndAddress)
            {
                lo = mid + 1;
            }
            else
            {
                best = candidate;
                break;
            }
        }

        if (best is null)
            return NativeResult.Fail<RuntimeFunction>(
                ErrorKinds.SymbolNotFound,
                $"No RuntimeFunction covers RVA 0x{rva:X8}.");

        return NativeResult.Ok(
            $"Found RuntimeFunction #{best.Index}: [0x{best.BeginAddress:X8}, 0x{best.EndAddress:X8})",
            best);
    }

    private static RuntimeFunctionLayout? GetRuntimeFunctionLayout(NativeImage image)
    {
        if (image.Architecture == Architecture.X64)
        {
            return RuntimeFunctionLayout.X64;
        }

        if (image.Architecture == Architecture.Arm64)
        {
            return RuntimeFunctionLayout.Arm64;
        }

        var inferredArchitecture = TryReadTargetArchitecture(image);
        return inferredArchitecture switch
        {
            Architecture.X64 => RuntimeFunctionLayout.X64,
            Architecture.Arm64 => RuntimeFunctionLayout.Arm64,
            _ => null,
        };
    }

    private static int GetRuntimeFunctionEntrySize(RuntimeFunctionLayout layout) =>
        layout == RuntimeFunctionLayout.X64 ? RuntimeFunctionSizeX64 : RuntimeFunctionSizeArm64;

    private static NativeResult<RuntimeFunction> ReadRuntimeFunctionAtIndex(
        NativeImage image,
        RuntimeFunctionLayout layout,
        int tableFileOffset,
        int index)
    {
        var entrySize = GetRuntimeFunctionEntrySize(layout);
        var bytes = image.RawBytes.Span;
        var off = tableFileOffset + index * entrySize;
        if (off + entrySize > bytes.Length)
            return NativeResult.Fail<RuntimeFunction>(
                ErrorKinds.InvalidArgument,
                $"RuntimeFunctions entry {index} exceeds the file size.");

        var begin = BinaryPrimitives.ReadUInt32LittleEndian(bytes[off..]);
        if (layout == RuntimeFunctionLayout.X64)
        {
            var end = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(off + 4)..]);
            var unwind = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(off + 8)..]);
            return NativeResult.Ok(
                $"Decoded RuntimeFunction #{index}.",
                new RuntimeFunction(index, begin, end, unwind));
        }

        var unwindData = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(off + 4)..]);
        var endResult = ComputeArm64EndAddress(image, begin, unwindData, index);
        if (endResult.IsError)
            return NativeResult.Fail<RuntimeFunction>(
                endResult.Error!.Kind,
                endResult.Error.Message,
                endResult.Error.Detail);

        return NativeResult.Ok(
            $"Decoded RuntimeFunction #{index}.",
            new RuntimeFunction(index, begin, endResult.Data, unwindData));
    }

    private static NativeResult<uint> ComputeArm64EndAddress(
        NativeImage image,
        uint beginAddress,
        uint unwindData,
        int index)
    {
        uint functionLengthUnits;

        if ((unwindData & Arm64PackedFlagMask) != 0)
        {
            functionLengthUnits = (unwindData >> Arm64PackedFunctionLengthShift) & Arm64PackedFunctionLengthMask;
        }
        else
        {
            var xdataFileOffset = image.RvaToFileOffset(unwindData);
            if (xdataFileOffset is null)
                return NativeResult.Fail<uint>(
                    ErrorKinds.InvalidArgument,
                    $"ARM64 RuntimeFunctions entry {index} references xdata RVA 0x{unwindData:X8}, which could not be mapped to a file offset.");

            var bytes = image.RawBytes.Span;
            if (xdataFileOffset.Value + sizeof(uint) > bytes.Length)
                return NativeResult.Fail<uint>(
                    ErrorKinds.InvalidArgument,
                    $"ARM64 RuntimeFunctions entry {index} has a truncated xdata header at RVA 0x{unwindData:X8}.");

            var xdataHeader = BinaryPrimitives.ReadUInt32LittleEndian(bytes[xdataFileOffset.Value..]);
            functionLengthUnits = xdataHeader & Arm64XdataFunctionLengthMask;
        }

        var functionLengthBytes = functionLengthUnits * Arm64FunctionLengthScale;
        var endAddress = beginAddress + functionLengthBytes;
        if (endAddress < beginAddress)
            return NativeResult.Fail<uint>(
                ErrorKinds.InvalidArgument,
                $"ARM64 RuntimeFunctions entry {index} overflowed while computing the end RVA.");

        return NativeResult.Ok(
            $"Computed end RVA 0x{endAddress:X8} for ARM64 RuntimeFunctions entry {index}.",
            endAddress);
    }

    private static Architecture? MapImageFileMachine(ushort machine)
    {
        return machine switch
        {
            ImageFileMachineI386 => Architecture.X86,
            ImageFileMachineAmd64 => Architecture.X64,
            ImageFileMachineArm64 => Architecture.Arm64,
            _ => null,
        };
    }

    /// <summary>
    /// Locates the <c>IMAGE_COR20_HEADER.ManagedNativeHeaderDirectory</c> and returns
    /// its file offset and size, or <c>null</c> if not present.
    /// </summary>
    private static (int FileOffset, uint Size)? FindManagedNativeHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 64) return null;
        if (bytes[0] != 0x4D || bytes[1] != 0x5A) return null;  // MZ

        var e_lfanew = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[0x3C..]);
        if (e_lfanew < 0 || e_lfanew + 24 + 128 > bytes.Length) return null;

        // Verify PE signature
        if (bytes[e_lfanew] != 'P' || bytes[e_lfanew + 1] != 'E' ||
            bytes[e_lfanew + 2] != 0 || bytes[e_lfanew + 3] != 0)
            return null;

        var optMagic = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(e_lfanew + 24)..]);
        // PE32 = 0x10B, PE32+ = 0x20B; only support 64-bit (PE32+)
        if (optMagic is not (0x10B or 0x20B)) return null;

        var optHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(e_lfanew + 20)..]);
        var numSections = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(e_lfanew + 6)..]);

        // DataDirectory base: for PE32+ it's at e_lfanew+24+112; for PE32 it's at e_lfanew+24+96
        var ddBase = optMagic == 0x20B
            ? e_lfanew + 24 + 112
            : e_lfanew + 24 + 96;

        if (ddBase + 15 * 8 + 8 > bytes.Length) return null;

        // DataDirectory[14] = CLR runtime header
        var clrRva = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(ddBase + 14 * 8)..]);
        var clrSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(ddBase + 14 * 8 + 4)..]);
        if (clrRva == 0 || clrSize < 72) return null;

        var sectionsBase = e_lfanew + 24 + optHeaderSize;
        var clrOffset = RvaToFileOffset(bytes, clrRva, numSections, sectionsBase);
        if (clrOffset is null || clrOffset.Value + 72 > bytes.Length) return null;

        // IMAGE_COR20_HEADER.ManagedNativeHeader is at offset 64 within the CLR header
        var mnhRva = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(clrOffset.Value + 64)..]);
        var mnhSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(clrOffset.Value + 68)..]);
        if (mnhRva == 0 || mnhSize == 0) return null;

        var mnhOffset = RvaToFileOffset(bytes, mnhRva, numSections, sectionsBase);
        if (mnhOffset is null) return null;

        return (mnhOffset.Value, mnhSize);
    }

    private static int? RvaToFileOffset(ReadOnlySpan<byte> bytes, uint rva, int numSections, int sectionsBase)
    {
        for (var i = 0; i < numSections; i++)
        {
            var s = sectionsBase + i * 40;
            if (s + 40 > bytes.Length) break;
            var vAddr = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(s + 12)..]);
            var vSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(s + 16)..]);
            var rawPtr = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(s + 20)..]);
            if (rva >= vAddr && rva < vAddr + Math.Max(vSize, 0x1000u))
            {
                var off = (int)(rawPtr + (rva - vAddr));
                return off < bytes.Length ? off : null;
            }
        }
        return null;
    }
}

/// <summary>One page of <see cref="RuntimeFunction"/> results.</summary>
/// <param name="Functions">The entries on this page.</param>
/// <param name="Cursor">The cursor value used to fetch this page.</param>
/// <param name="TotalCount">Total number of entries in the table.</param>
/// <param name="NextCursor">Cursor to pass for the next page, or <c>null</c> if this is the last page.</param>
public sealed record ReadyToRunRuntimeFunctionsPage(
    IReadOnlyList<RuntimeFunction> Functions,
    int Cursor,
    int TotalCount,
    int? NextCursor);
