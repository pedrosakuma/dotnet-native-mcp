using System.Buffers.Binary;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.R2R;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Tests for the V9 RID-indexed R2R info maps: EnclosingTypeMap (type 122),
/// MethodIsGenericMap (type 121) and TypeGenericInfoMap (type 123).
/// </summary>
public class R2RInfoMapsTests
{
    private const uint TypeDefTokenBase = 0x02000000;
    private const uint MethodDefTokenBase = 0x06000000;

    // -----------------------------------------------------------------------
    // EnclosingTypeMap (122).
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadEnclosingTypeMap_MissingSection_ReturnsNotPresent()
    {
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.AvailableTypes, new byte[8]);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadEnclosingTypeMap(image, hdr, 200);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.R2RSectionNotPresent);
    }

    [Fact]
    public void ReadEnclosingTypeMap_DecodesNestedTypesOnly()
    {
        // 3 types: rid 1 top-level, rid 2 nested in rid 1, rid 3 top-level.
        var blob = BuildEnclosingTypeMap(new ushort[] { 0, 1, 0 });
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.EnclosingTypeMap, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadEnclosingTypeMap(image, hdr, 200);

        result.IsError.Should().BeFalse();
        var t = result.Data!;
        t.TypeDefCount.Should().Be(3);
        t.Truncated.Should().BeFalse();
        t.NestedTypes.Should().ContainSingle();
        t.NestedTypes[0].NestedTypeToken.Should().Be((int)(TypeDefTokenBase | 2));
        t.NestedTypes[0].EnclosingTypeToken.Should().Be((int)(TypeDefTokenBase | 1));
    }

    [Fact]
    public void ReadEnclosingTypeMap_RespectsLimit()
    {
        // rid 2 -> 1, rid 3 -> 1, rid 4 -> 1 (three nested types).
        var blob = BuildEnclosingTypeMap(new ushort[] { 0, 1, 1, 1 });
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.EnclosingTypeMap, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadEnclosingTypeMap(image, hdr, 2);

        result.IsError.Should().BeFalse();
        result.Data!.NestedTypes.Should().HaveCount(2);
        result.Data.Truncated.Should().BeTrue();
    }

    [Fact]
    public void ReadEnclosingTypeMap_DeclaredCountBeyondSection_FailsGracefully()
    {
        // count says 4 but only one entry follows.
        var blob = new byte[] { 0x04, 0x00, 0x01, 0x00 };
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.EnclosingTypeMap, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadEnclosingTypeMap(image, hdr, 200);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // -----------------------------------------------------------------------
    // MethodIsGenericMap (121).
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadMethodIsGenericMap_DecodesSetBits()
    {
        // 10 methods, methods at index 0 and 9 (rid 1 and 10) are generic.
        var blob = BuildMethodIsGenericMap(10, 0, 9);
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.MethodIsGenericMap, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadMethodIsGenericMap(image, hdr, 200);

        result.IsError.Should().BeFalse();
        var t = result.Data!;
        t.MethodDefCount.Should().Be(10);
        t.GenericMethodCount.Should().Be(2);
        t.Truncated.Should().BeFalse();
        t.GenericMethodTokens.Should().Equal(
            (int)(MethodDefTokenBase | 1), (int)(MethodDefTokenBase | 10));
    }

    [Fact]
    public void ReadMethodIsGenericMap_RespectsLimitButCountsAll()
    {
        var blob = BuildMethodIsGenericMap(12, 1, 4, 7);
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.MethodIsGenericMap, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadMethodIsGenericMap(image, hdr, 2);

        result.IsError.Should().BeFalse();
        var t = result.Data!;
        t.GenericMethodCount.Should().Be(3);
        t.GenericMethodTokens.Should().HaveCount(2);
        t.Truncated.Should().BeTrue();
        t.GenericMethodTokens.Should().Equal(
            (int)(MethodDefTokenBase | 2), (int)(MethodDefTokenBase | 5));
    }

    [Fact]
    public void ReadMethodIsGenericMap_DeclaredCountBeyondSection_FailsGracefully()
    {
        // count 100 but only one bit byte follows.
        var blob = new byte[] { 0x64, 0x00, 0x00, 0x00, 0x00 };
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.MethodIsGenericMap, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadMethodIsGenericMap(image, hdr, 200);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void ReadMethodIsGenericMap_CountNearIntMax_FailsGracefully()
    {
        // count 0x7FFFFFFF with no bit bytes — (count + 7) overflows int if not widened.
        var blob = new byte[] { 0xFF, 0xFF, 0xFF, 0x7F };
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.MethodIsGenericMap, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadMethodIsGenericMap(image, hdr, 200);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // -----------------------------------------------------------------------
    // TypeGenericInfoMap (123).
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadTypeGenericInfoMap_DecodesGenericTypesOnly()
    {
        // rid1 non-generic; rid2 generic 1-arg + variance; rid3 generic 2-arg + constraints.
        var blob = BuildTypeGenericInfoMap(new byte[] { 0x0, 0x9, 0x6 });
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.TypeGenericInfoMap, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadTypeGenericInfoMap(image, hdr, 200);

        result.IsError.Should().BeFalse();
        var t = result.Data!;
        t.TypeDefCount.Should().Be(3);
        t.GenericTypeCount.Should().Be(2);
        t.Truncated.Should().BeFalse();
        t.GenericTypes.Should().HaveCount(2);

        t.GenericTypes[0].TypeToken.Should().Be((int)(TypeDefTokenBase | 2));
        t.GenericTypes[0].GenericArgCount.Should().Be(1);
        t.GenericTypes[0].HasVariance.Should().BeTrue();
        t.GenericTypes[0].HasConstraints.Should().BeFalse();

        t.GenericTypes[1].TypeToken.Should().Be((int)(TypeDefTokenBase | 3));
        t.GenericTypes[1].GenericArgCount.Should().Be(2);
        t.GenericTypes[1].HasVariance.Should().BeFalse();
        t.GenericTypes[1].HasConstraints.Should().BeTrue();
    }

    [Fact]
    public void ReadTypeGenericInfoMap_MoreThanTwoArgs_ReportsThree()
    {
        // single generic type with arg-count nibble 3 ("more than two").
        var blob = BuildTypeGenericInfoMap(new byte[] { 0x3 });
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.TypeGenericInfoMap, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadTypeGenericInfoMap(image, hdr, 200);

        result.IsError.Should().BeFalse();
        result.Data!.GenericTypes.Should().ContainSingle()
            .Which.GenericArgCount.Should().Be(3);
    }

    [Fact]
    public void ReadTypeGenericInfoMap_DeclaredCountBeyondSection_FailsGracefully()
    {
        // count 100 but only one nibble byte follows.
        var blob = new byte[] { 0x64, 0x00, 0x00, 0x00, 0x00 };
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.TypeGenericInfoMap, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadTypeGenericInfoMap(image, hdr, 200);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // -----------------------------------------------------------------------
    // Real R2R image regression — System.Private.CoreLib carries all three maps.
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadInfoMaps_RealImage_DecodeValidTokens()
    {
        var spc = FixturePaths.SystemPrivateCoreLib;
        if (spc is null || !File.Exists(spc)) return;

        var bytes = File.ReadAllBytes(spc);
        var image = PeNativeReader.Read(new ReadOnlyMemory<byte>(bytes), spc);
        image.Should().NotBeNull();
        var hdr = ReadyToRunReader.ReadHeader(image!).Data!;

        if (hdr.FindSection(ReadyToRunSectionType.EnclosingTypeMap) is not null)
        {
            var em = ReadyToRunReader.ReadEnclosingTypeMap(image!, hdr, 2000);
            em.IsError.Should().BeFalse();
            em.Data!.TypeDefCount.Should().BeGreaterThan(0);
            foreach (var n in em.Data.NestedTypes)
            {
                ((uint)n.NestedTypeToken & 0xFF000000).Should().Be(TypeDefTokenBase);
                ((uint)n.EnclosingTypeToken & 0xFF000000).Should().Be(TypeDefTokenBase);
                ((uint)n.EnclosingTypeToken & 0x00FFFFFF).Should().BeGreaterThan(0u);
            }
        }

        if (hdr.FindSection(ReadyToRunSectionType.MethodIsGenericMap) is not null)
        {
            var mm = ReadyToRunReader.ReadMethodIsGenericMap(image!, hdr, 2000);
            mm.IsError.Should().BeFalse();
            mm.Data!.MethodDefCount.Should().BeGreaterThan(0);
            mm.Data.GenericMethodCount.Should().BeGreaterThan(0);
            foreach (var tok in mm.Data.GenericMethodTokens)
                ((uint)tok & 0xFF000000).Should().Be(MethodDefTokenBase);
        }

        if (hdr.FindSection(ReadyToRunSectionType.TypeGenericInfoMap) is not null)
        {
            var tm = ReadyToRunReader.ReadTypeGenericInfoMap(image!, hdr, 2000);
            tm.IsError.Should().BeFalse();
            tm.Data!.TypeDefCount.Should().BeGreaterThan(0);
            foreach (var g in tm.Data.GenericTypes)
            {
                ((uint)g.TypeToken & 0xFF000000).Should().Be(TypeDefTokenBase);
                g.GenericArgCount.Should().BeInRange(1, 3);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Blob builders.
    // -----------------------------------------------------------------------

    private static byte[] BuildEnclosingTypeMap(ushort[] enclosingRids)
    {
        var blob = new byte[2 + enclosingRids.Length * 2];
        BinaryPrimitives.WriteUInt16LittleEndian(blob, (ushort)enclosingRids.Length);
        for (var i = 0; i < enclosingRids.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(2 + i * 2), enclosingRids[i]);
        return blob;
    }

    private static byte[] BuildMethodIsGenericMap(int count, params int[] genericIndices)
    {
        var bitBytes = (count + 7) / 8;
        var blob = new byte[4 + bitBytes];
        BinaryPrimitives.WriteInt32LittleEndian(blob, count);
        foreach (var j in genericIndices)
            blob[4 + (j >> 3)] |= (byte)(1 << (7 - (j & 7)));
        return blob;
    }

    private static byte[] BuildTypeGenericInfoMap(byte[] nibbles)
    {
        var count = nibbles.Length;
        var nibbleBytes = (count + 1) / 2;
        var blob = new byte[4 + nibbleBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(blob, (uint)count);
        for (var i = 0; i < count; i++)
        {
            var n = (byte)(nibbles[i] & 0xF);
            if ((i & 1) == 0)
                blob[4 + (i >> 1)] |= (byte)(n << 4);
            else
                blob[4 + (i >> 1)] |= n;
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

        var handle = ImageHandle.From("aabbccddee09", "synthetic_infomaps.dll");
        var clrSec = new NativeSection(".clr", ClrSectionVA, (ulong)clrSectionDataSize, ClrSectionRaw, (ulong)clrSectionFileSize);
        return new NativeImage(handle, "synthetic_infomaps.dll", BinaryFormat.Pe, Architecture.X64,
            [clrSec], [], new ReadOnlyMemory<byte>(bytes), 0x40000);
    }

    private static int Align(int value, int alignment) =>
        (value + alignment - 1) & ~(alignment - 1);
}
