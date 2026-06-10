using System.Buffers.Binary;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.R2R;
using DotnetNativeMcp.Core.R2R.NativeFormat;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Tests for the NativeFormat <see cref="NativeHashtable"/> reader and the
/// <c>AvailableTypes</c> (type 108) section decode it backs.
/// </summary>
public class NativeHashtableAndAvailableTypesTests
{
    private const uint TypeDefTokenBase = 0x02000000;
    private const uint ExportedTypeTokenBase = 0x27000000;

    // -----------------------------------------------------------------------
    // AvailableTypes — synthetic end-to-end.
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadAvailableTypes_MissingSection_ReturnsNotPresent()
    {
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ComponentAssemblies, new byte[16]);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadAvailableTypes(image, hdr, 200);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.R2RSectionNotPresent);
    }

    [Fact]
    public void ReadAvailableTypes_DecodesTypeDefAndExportedTokens()
    {
        // A single-bucket hashtable with a TypeDef RID 5 and an ExportedType RID 3.
        var blob = BuildSingleBucketHashtable(new[] { (5u, false), (3u, true) });
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.AvailableTypes, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadAvailableTypes(image, hdr, 200);

        result.IsError.Should().BeFalse();
        var table = result.Data!;
        table.Truncated.Should().BeFalse();
        table.Types.Should().HaveCount(2);

        table.Types[0].MetadataToken.Should().Be((int)(TypeDefTokenBase | 5));
        table.Types[0].IsExportedType.Should().BeFalse();

        table.Types[1].MetadataToken.Should().Be((int)(ExportedTypeTokenBase | 3));
        table.Types[1].IsExportedType.Should().BeTrue();
    }

    [Fact]
    public void ReadAvailableTypes_RespectsLimitAndReportsTruncation()
    {
        var blob = BuildSingleBucketHashtable(new[] { (1u, false), (2u, false), (3u, false) });
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.AvailableTypes, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadAvailableTypes(image, hdr, 2);

        result.IsError.Should().BeFalse();
        var table = result.Data!;
        table.Types.Should().HaveCount(2);
        table.Truncated.Should().BeTrue();
        table.Types[0].MetadataToken.Should().Be((int)(TypeDefTokenBase | 1));
        table.Types[1].MetadataToken.Should().Be((int)(TypeDefTokenBase | 2));
    }

    [Fact]
    public void ReadAvailableTypes_MalformedBlob_FailsGracefully()
    {
        // header 0xFF -> numberOfBucketsShift 63 (> 31) -> BadImage -> InvalidArgument.
        var blob = new byte[] { 0xFF };
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.AvailableTypes, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadAvailableTypes(image, hdr, 200);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void ReadAvailableTypes_OutOfRangeRid_FailsGracefully()
    {
        // RID 0 (entry value 0 -> isExported false, rid 0) is not a valid metadata RID.
        var blob = BuildSingleBucketHashtable(new[] { (0u, false) });
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.AvailableTypes, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadAvailableTypes(image, hdr, 200);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void ReadAvailableTypes_ManyEmptyBuckets_ScanIsBounded()
    {
        // An 8-bucket hashtable with every bucket empty. With a scan cap below the
        // bucket count the traversal must stop early and report truncation rather
        // than walking every bucket — the guard against a crafted huge bucket count.
        var blob = BuildAllEmptyHashtable(numberOfBucketsShift: 3);
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.AvailableTypes, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var capped = ReadyToRunReader.ReadAvailableTypes(image, hdr, limit: 50, maxScan: 4);

        capped.IsError.Should().BeFalse();
        capped.Data!.Types.Should().BeEmpty();
        capped.Data.Truncated.Should().BeTrue();

        // With a cap above the bucket count, all (empty) buckets are walked; still no
        // entries, and not flagged truncated.
        var full = ReadyToRunReader.ReadAvailableTypes(image, hdr, limit: 50, maxScan: 100);

        full.IsError.Should().BeFalse();
        full.Data!.Types.Should().BeEmpty();
        full.Data.Truncated.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Real R2R image regression — decode System.Private.CoreLib's AvailableTypes
    // hashtable and sanity-check the recovered metadata tokens.
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadAvailableTypes_RealImage_DecodesValidTokens()
    {
        var spc = FixturePaths.SystemPrivateCoreLib;
        if (spc is null || !File.Exists(spc)) return; // skip when no real R2R fixture is available

        var bytes = File.ReadAllBytes(spc);
        var image = PeNativeReader.Read(new ReadOnlyMemory<byte>(bytes), spc);
        image.Should().NotBeNull();

        var hdr = ReadyToRunReader.ReadHeader(image!).Data!;
        if (hdr.FindSection(ReadyToRunSectionType.AvailableTypes) is null)
            return; // not an R2R layout that carries the section

        var result = ReadyToRunReader.ReadAvailableTypes(image!, hdr, 2000);

        result.IsError.Should().BeFalse();
        var table = result.Data!;
        table.Types.Should().NotBeEmpty();

        foreach (var t in table.Types)
        {
            var token = (uint)t.MetadataToken;
            var tableByte = token & 0xFF000000;
            var rid = token & 0x00FFFFFF;
            rid.Should().BeGreaterThan(0u);
            if (t.IsExportedType)
                tableByte.Should().Be(ExportedTypeTokenBase);
            else
                tableByte.Should().Be(TypeDefTokenBase);
        }
    }

    // -----------------------------------------------------------------------
    // Helpers.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a single-bucket (shift 0, entryIndexSize 0) NativeHashtable whose
    /// entries are the supplied <c>(rid, isExportedType)</c> pairs. All deltas and
    /// payloads are kept in the 1-byte encoding range (rid &lt; 64).
    /// </summary>
    private static byte[] BuildSingleBucketHashtable((uint rid, bool isExported)[] entries)
    {
        var n = entries.Length;
        var blob = new byte[3 + 2 * n + n];

        blob[0] = 0x00;                  // header: shift 0, entryIndexSize 0
        blob[1] = 0x02;                  // index[0] = start (first slot at slice offset 3)
        blob[2] = (byte)(2 + 2 * n);     // index[1] = end (one past the last slot)

        for (var i = 0; i < n; i++)
        {
            var slotOff = 3 + 2 * i;
            blob[slotOff] = 0x00;        // low-hashcode byte (unused by the decode)

            // Signed 1-byte delta from the delta byte's own position to the payload.
            var delta = 2 * n - i - 1;
            blob[slotOff + 1] = (byte)((delta << 1) & 0xFE);

            var entryValue = (entries[i].rid << 1) | (entries[i].isExported ? 1u : 0u);
            blob[3 + 2 * n + i] = (byte)(entryValue << 1); // unsigned 1-byte payload
        }

        return blob;
    }

    /// <summary>
    /// Builds a NativeHashtable with <c>2^numberOfBucketsShift</c> buckets that are
    /// all empty (every bucket-index entry is zero, so start == end).
    /// </summary>
    private static byte[] BuildAllEmptyHashtable(int numberOfBucketsShift)
    {
        var numBuckets = 1 << numberOfBucketsShift;
        // header + (numBuckets + 1) one-byte index entries, all zero.
        var blob = new byte[1 + numBuckets + 1];
        blob[0] = (byte)((numberOfBucketsShift << 2) | 0);
        return blob;
    }

    // -----------------------------------------------------------------------
    // Synthetic PE factory carrying a single raw-bytes R2R section.
    // (Mirrors the factory used by the other R2R section test suites.)
    // -----------------------------------------------------------------------

    private static NativeImage BuildSyntheticR2RWithRawSection(uint sectionType, byte[] payload)
    {
        const uint FileAlignment = 0x200;
        const uint ClrSectionVA = 0x2000;
        const uint ClrSectionRaw = 0x400;
        const uint R2RHeaderVA = ClrSectionVA + 72;

        const uint numR2RSections = 1;
        var r2rHeaderSize = 16 + (int)numR2RSections * 12;

        var payloadVA = R2RHeaderVA + (uint)r2rHeaderSize;
        var payloadSize = payload.Length;

        var clrSectionDataSize = 72 + r2rHeaderSize + payloadSize;
        var clrSectionFileSize = Align(clrSectionDataSize, (int)FileAlignment);
        var totalSize = (int)ClrSectionRaw + clrSectionFileSize;
        var bytes = new byte[totalSize];

        bytes[0] = 0x4D; bytes[1] = 0x5A;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x3C), 0x80);
        var peOff = 0x80;
        bytes[peOff] = (byte)'P'; bytes[peOff + 1] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOff + 4), 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOff + 6), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOff + 20), 0xF0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOff + 22), 0x2022);

        var optOff = peOff + 24;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(optOff), 0x20B);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optOff + 56), 0x40000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optOff + 60), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optOff + 64), FileAlignment);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(optOff + 40), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optOff + 92), 16);

        var ddBase = optOff + 112;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ddBase + 14 * 8), ClrSectionVA);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ddBase + 14 * 8 + 4), 72);

        var secTableOff = peOff + 24 + 0xF0;
        bytes[secTableOff] = (byte)'.'; bytes[secTableOff + 1] = (byte)'c';
        bytes[secTableOff + 2] = (byte)'l'; bytes[secTableOff + 3] = (byte)'r';
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 8), (uint)clrSectionDataSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 12), ClrSectionVA);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 16), (uint)clrSectionFileSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 20), ClrSectionRaw);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 36), 0x40000040u);

        var clrOff = (int)ClrSectionRaw;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clrOff + 0), 72);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clrOff + 64), R2RHeaderVA);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clrOff + 68), (uint)r2rHeaderSize);

        var r2rOff = clrOff + 72;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(r2rOff + 0), 0x00525452u);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(r2rOff + 4), 6);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(r2rOff + 6), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(r2rOff + 8), 0x00000003u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(r2rOff + 12), numR2RSections);

        var secEntOff = r2rOff + 16;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 0), sectionType);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 4), payloadVA);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 8), (uint)payloadSize);

        var tableOff = r2rOff + r2rHeaderSize;
        payload.CopyTo(bytes.AsSpan(tableOff));

        var handle = ImageHandle.From("aabbccddee08", "synthetic_availabletypes.dll");
        var clrSec = new NativeSection(".clr", ClrSectionVA, (ulong)clrSectionDataSize, ClrSectionRaw, (ulong)clrSectionFileSize);
        return new NativeImage(handle, "synthetic_availabletypes.dll", BinaryFormat.Pe, Architecture.X64,
            [clrSec], [], new ReadOnlyMemory<byte>(bytes), 0x40000);
    }

    private static int Align(int value, int alignment) =>
        (value + alignment - 1) & ~(alignment - 1);
}
