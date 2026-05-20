using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;

namespace DotnetNativeMcp.Core.R2R;

/// <summary>
/// Parses ReadyToRun (R2R) metadata from a managed PE binary.
/// Only x64 <c>RUNTIME_FUNCTION</c> layout is supported in v1; ARM64 is rejected with
/// <see cref="ErrorKinds.R2RArchUnsupported"/>.
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
    /// Reads the <c>RuntimeFunctions</c> section from the R2R header.
    /// Returns paginated results; pass the <c>NextCursor</c> from the result to fetch the next page.
    /// Only x64 layout is supported.
    /// </summary>
    public static NativeResult<ReadyToRunRuntimeFunctionsPage> ReadRuntimeFunctions(
        NativeImage image,
        ReadyToRunHeader header,
        int cursor = 0,
        int pageSize = 100)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(header);

        if (image.Architecture != Architecture.X64)
            return NativeResult.Fail<ReadyToRunRuntimeFunctionsPage>(
                ErrorKinds.R2RArchUnsupported,
                $"Only x64 RUNTIME_FUNCTION layout is supported in v1; " +
                $"this image is {image.Architecture}.");

        var rtSection = header.FindSection(ReadyToRunSectionType.RuntimeFunctions);
        if (rtSection is null)
            return NativeResult.Fail<ReadyToRunRuntimeFunctionsPage>(
                ErrorKinds.R2RSectionNotPresent,
                "This R2R image does not contain a RuntimeFunctions section (type 5). " +
                "Newer R2R versions (>= 14) use MethodHeaderAndCodeInfo (type 105) instead.");

        var totalEntries = (int)(rtSection.Size / RuntimeFunctionSizeX64);
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

        var bytes = image.RawBytes.Span;
        var take = Math.Min(pageSize, totalEntries - cursor);
        var results = new List<RuntimeFunction>(take);

        for (var i = 0; i < take; i++)
        {
            var idx = cursor + i;
            var off = fileOffset.Value + idx * RuntimeFunctionSizeX64;
            if (off + RuntimeFunctionSizeX64 > bytes.Length) break;

            var begin = BinaryPrimitives.ReadUInt32LittleEndian(bytes[off..]);
            var end = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(off + 4)..]);
            var unwind = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(off + 8)..]);
            results.Add(new RuntimeFunction(idx, begin, end, unwind));
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

        if (image.Architecture != Architecture.X64)
            return NativeResult.Fail<RuntimeFunction>(
                ErrorKinds.R2RArchUnsupported,
                $"Only x64 RUNTIME_FUNCTION layout is supported in v1; " +
                $"this image is {image.Architecture}.");

        var rtSection = header.FindSection(ReadyToRunSectionType.RuntimeFunctions);
        if (rtSection is null)
            return NativeResult.Fail<RuntimeFunction>(
                ErrorKinds.R2RSectionNotPresent,
                "This R2R image does not contain a RuntimeFunctions section (type 5).");

        var fileOffset = image.RvaToFileOffset(rtSection.VirtualAddress);
        if (fileOffset is null)
            return NativeResult.Fail<RuntimeFunction>(
                ErrorKinds.InvalidArgument,
                $"RuntimeFunctions RVA 0x{rtSection.VirtualAddress:X8} could not be mapped to a file offset.");

        var bytes = image.RawBytes.Span;
        var totalEntries = (int)(rtSection.Size / RuntimeFunctionSizeX64);

        // Binary search on BeginAddress
        var lo = 0;
        var hi = totalEntries - 1;
        RuntimeFunction? best = null;

        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            var off = fileOffset.Value + mid * RuntimeFunctionSizeX64;
            if (off + RuntimeFunctionSizeX64 > bytes.Length) break;

            var begin = BinaryPrimitives.ReadUInt32LittleEndian(bytes[off..]);
            var end = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(off + 4)..]);
            var unwind = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(off + 8)..]);

            if (rva < begin)
            {
                hi = mid - 1;
            }
            else if (rva >= end)
            {
                lo = mid + 1;
            }
            else
            {
                // rva is within [begin, end)
                best = new RuntimeFunction(mid, begin, end, unwind);
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
