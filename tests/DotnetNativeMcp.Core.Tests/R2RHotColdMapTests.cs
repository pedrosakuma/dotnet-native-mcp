using System.Buffers.Binary;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.R2R;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Tests for the HotColdMap section (type 120) — a flat uint[] of
/// (coldRuntimeFunctionIndex, hotRuntimeFunctionIndex) pairs.
/// </summary>
public class R2RHotColdMapTests
{
    [Fact]
    public void ReadHotColdMap_MissingSection_ReturnsNotPresent()
    {
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.AvailableTypes, new byte[8]);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadHotColdMap(image, hdr, 200);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.R2RSectionNotPresent);
    }

    [Fact]
    public void ReadHotColdMap_DecodesPairs()
    {
        // Two pairs: (cold 5, hot 4) and (cold 9, hot 7).
        var blob = BuildHotColdMap((5u, 4u), (9u, 7u));
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.HotColdMap, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadHotColdMap(image, hdr, 200);

        result.IsError.Should().BeFalse();
        var t = result.Data!;
        t.PairCount.Should().Be(2);
        t.Truncated.Should().BeFalse();
        t.Pairs.Should().HaveCount(2);
        t.Pairs[0].ColdRuntimeFunctionIndex.Should().Be(5u);
        t.Pairs[0].HotRuntimeFunctionIndex.Should().Be(4u);
        t.Pairs[1].ColdRuntimeFunctionIndex.Should().Be(9u);
        t.Pairs[1].HotRuntimeFunctionIndex.Should().Be(7u);
    }

    [Fact]
    public void ReadHotColdMap_RespectsLimit()
    {
        var blob = BuildHotColdMap((5u, 4u), (9u, 7u), (12u, 10u));
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.HotColdMap, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadHotColdMap(image, hdr, 2);

        result.IsError.Should().BeFalse();
        var t = result.Data!;
        t.PairCount.Should().Be(3);
        t.Pairs.Should().HaveCount(2);
        t.Truncated.Should().BeTrue();
    }

    [Fact]
    public void ReadHotColdMap_OddU32Count_FailsGracefully()
    {
        // 12 bytes = 3 uints = 1.5 pairs — not a whole number of pairs.
        var blob = new byte[12];
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.HotColdMap, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadHotColdMap(image, hdr, 200);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void ReadHotColdMap_EmptySection_FailsGracefully()
    {
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.HotColdMap, Array.Empty<byte>());
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadHotColdMap(image, hdr, 200);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // -----------------------------------------------------------------------
    // Blob builder.
    // -----------------------------------------------------------------------

    private static byte[] BuildHotColdMap(params (uint Cold, uint Hot)[] pairs)
    {
        var blob = new byte[pairs.Length * 8];
        for (var i = 0; i < pairs.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(i * 8), pairs[i].Cold);
            BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(i * 8 + 4), pairs[i].Hot);
        }
        return blob;
    }

    // -----------------------------------------------------------------------
    // Synthetic PE factory carrying a single raw-bytes R2R section.
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

        var handle = ImageHandle.From("aabbccddee20", "synthetic_hotcold.dll");
        var clrSec = new NativeSection(".clr", ClrSectionVA, (ulong)clrSectionDataSize, ClrSectionRaw, (ulong)clrSectionFileSize);
        return new NativeImage(handle, "synthetic_hotcold.dll", BinaryFormat.Pe, Architecture.X64,
            [clrSec], [], new ReadOnlyMemory<byte>(bytes), 0x40000);
    }

    private static int Align(int value, int alignment) =>
        (value + alignment - 1) & ~(alignment - 1);
}
