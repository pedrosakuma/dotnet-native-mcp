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
/// Tests for the NativeFormat <see cref="NativeArray"/> reader and the
/// <c>MethodDefEntryPoints</c> (type 103) section decode it backs.
/// </summary>
public class NativeArrayAndMethodEntryPointsTests
{
    // -----------------------------------------------------------------------
    // NativeArray — unit tests over hand-encoded blobs.
    //
    // Each present element sits at local index 0 of its 16-element block, so
    // every block is a single bit-tree leaf (node 0x00). This exercises the
    // header decode, the per-block index array (all three entryIndexSize
    // widths) and leaf navigation without re-implementing the full bit-tree.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NativeArray_TwoBlocks_ResolvesPresentElements(int entryIndexSize)
    {
        // Block 0 carries index 0; block 1 carries index 16. Payloads are
        // single ldc-style unsigned values we read back to prove the offset.
        var blob = BuildSingleLeafPerBlockArray(
            new[] { Encoded(0x11u), Encoded(0x22u) }, entryIndexSize);
        var reader = new NativeReader(blob.AsMemory());
        var array = new NativeArray(reader, 0);

        array.Count.Should().Be(17u); // (2 - 1) * 16 + 1

        array.TryGetAt(0, out var off0).Should().BeTrue();
        reader.DecodeUnsigned(off0, out var v0);
        v0.Should().Be(0x11u);

        array.TryGetAt(16, out var off16).Should().BeTrue();
        reader.DecodeUnsigned(off16, out var v16);
        v16.Should().Be(0x22u);
    }

    [Fact]
    public void NativeArray_AbsentElementsInBlock_ReturnFalse()
    {
        var blob = BuildSingleLeafPerBlockArray(new[] { Encoded(0x11u), Encoded(0x22u) }, 1);
        var reader = new NativeReader(blob.AsMemory());
        var array = new NativeArray(reader, 0);

        // Indices 1..15 share block 0 but are absent (leaf only matches local 0).
        array.TryGetAt(1, out _).Should().BeFalse();
        array.TryGetAt(15, out _).Should().BeFalse();
        // Out of range.
        array.TryGetAt(17, out _).Should().BeFalse();
        array.TryGetAt(1000, out _).Should().BeFalse();
    }

    [Fact]
    public void NativeArray_SingleElement_Resolves()
    {
        var blob = BuildSingleLeafPerBlockArray(new[] { Encoded(0x7Fu) }, 0);
        var reader = new NativeReader(blob.AsMemory());
        var array = new NativeArray(reader, 0);

        array.Count.Should().Be(1u);
        array.TryGetAt(0, out var off).Should().BeTrue();
        reader.DecodeUnsigned(off, out var v);
        v.Should().Be(0x7Fu);
    }

    // -----------------------------------------------------------------------
    // ReadMethodDefEntryPoints — synthetic end-to-end.
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadMethodDefEntryPoints_MissingSection_ReturnsNotPresent()
    {
        // A composite section stands in for "some other section, but not 103".
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ComponentAssemblies, new byte[16]);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadMethodDefEntryPoints(image, hdr, 200);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.R2RSectionNotPresent);
    }

    [Fact]
    public void ReadMethodDefEntryPoints_DecodesRidRuntimeFunctionAndFixups()
    {
        // RID 1 (index 0): runtime function 5, no fixups -> id = 5 << 1 = 10.
        // RID 17 (index 16): runtime function 9, with fixups -> id = (9 << 2) | 1 = 37.
        var blob = BuildSingleLeafPerBlockArray(
            new[] { Encoded(5u << 1), Encoded((9u << 2) | 1u) }, 1);
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.MethodDefEntryPoints, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadMethodDefEntryPoints(image, hdr, 200);

        result.IsError.Should().BeFalse();
        var table = result.Data!;
        table.MethodCount.Should().Be(17u);
        table.Truncated.Should().BeFalse();
        table.Entries.Should().HaveCount(2);

        table.Entries[0].Rid.Should().Be(1);
        table.Entries[0].RuntimeFunctionIndex.Should().Be(5);
        table.Entries[0].HasFixups.Should().BeFalse();

        table.Entries[1].Rid.Should().Be(17);
        table.Entries[1].RuntimeFunctionIndex.Should().Be(9);
        table.Entries[1].HasFixups.Should().BeTrue();
    }

    [Fact]
    public void ReadMethodDefEntryPoints_FixupDelta_ConsumesTrailingOffset()
    {
        // id with both marker bits set (bit0 = fixups, bit1 = trailing delta):
        // id = (7 << 2) | 3 = 31, followed by a delta-encoded fixup offset.
        byte[] payload = [.. Encoded((7u << 2) | 3u), .. Encoded(123u)];
        var blob = BuildSingleLeafPerBlockArray(new[] { payload }, 1);
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.MethodDefEntryPoints, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadMethodDefEntryPoints(image, hdr, 200);

        result.IsError.Should().BeFalse();
        var table = result.Data!;
        table.Entries.Should().HaveCount(1);
        table.Entries[0].Rid.Should().Be(1);
        table.Entries[0].RuntimeFunctionIndex.Should().Be(7);
        table.Entries[0].HasFixups.Should().BeTrue();
    }

    [Fact]
    public void ReadMethodDefEntryPoints_FixupDelta_Truncated_FailsGracefully()
    {
        // Same id = (7 << 2) | 3 marking a trailing delta, but the section ends
        // immediately after the id — decoding the missing delta must raise
        // NativeFormatException and surface as InvalidArgument, not be accepted.
        byte[] payload = Encoded((7u << 2) | 3u);
        var blob = BuildSingleLeafPerBlockArray(new[] { payload }, 1);
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.MethodDefEntryPoints, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadMethodDefEntryPoints(image, hdr, 200);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void ReadMethodDefEntryPoints_RespectsLimitAndReportsTruncation()
    {
        var blob = BuildSingleLeafPerBlockArray(
            new[] { Encoded(1u << 1), Encoded(2u << 1), Encoded(3u << 1) }, 1);
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.MethodDefEntryPoints, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadMethodDefEntryPoints(image, hdr, 2);

        result.IsError.Should().BeFalse();
        var table = result.Data!;
        table.Entries.Should().HaveCount(2);
        table.Truncated.Should().BeTrue();
        table.Entries[0].Rid.Should().Be(1);
        table.Entries[1].Rid.Should().Be(17);
    }

    [Fact]
    public void ReadMethodDefEntryPoints_HugeCount_AllAbsent_ScanIsBounded()
    {
        // A NativeArray advertising 100 slots that are all absent. With a scan cap
        // below the advertised count the decode must stop early and report
        // truncation rather than probing every slot — the guard that prevents a
        // crafted huge-Count / all-absent table from spinning unbounded.
        var blob = BuildAllAbsentArray(100, entryIndexSize: 0);
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.MethodDefEntryPoints, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var capped = ReadyToRunReader.ReadMethodDefEntryPoints(image, hdr, limit: 50, maxScan: 32);

        capped.IsError.Should().BeFalse();
        capped.Data!.MethodCount.Should().Be(100u);
        capped.Data.Entries.Should().BeEmpty();
        capped.Data.Truncated.Should().BeTrue();

        // With a cap at/above the advertised count the full sparse table is
        // scanned; still no present entries, and not flagged truncated.
        var full = ReadyToRunReader.ReadMethodDefEntryPoints(image, hdr, limit: 50, maxScan: 200);

        full.IsError.Should().BeFalse();
        full.Data!.Entries.Should().BeEmpty();
        full.Data.Truncated.Should().BeFalse();
    }

    [Fact]
    public void ReadMethodDefEntryPoints_MalformedBlob_FailsGracefully()
    {
        // A header claiming a huge element count with no backing index/blocks.
        // 0xFF... selects a multi-byte unsigned the truncated blob cannot satisfy.
        var blob = new byte[] { 0xFF };
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.MethodDefEntryPoints, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadMethodDefEntryPoints(image, hdr, 200);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // -----------------------------------------------------------------------
    // Real R2R image regression — decode the runtime-encoded MethodDefEntryPoints
    // of System.Private.CoreLib and cross-check every entry against the
    // RuntimeFunctions table bound.
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadMethodDefEntryPoints_RealImage_DecodesWithinRuntimeFunctionBounds()
    {
        var spc = FixturePaths.SystemPrivateCoreLib;
        if (spc is null || !File.Exists(spc)) return; // skip when no real R2R fixture is available

        var bytes = File.ReadAllBytes(spc);
        var image = PeNativeReader.Read(new ReadOnlyMemory<byte>(bytes), spc);
        image.Should().NotBeNull();

        var hdr = ReadyToRunReader.ReadHeader(image!).Data!;
        if (hdr.FindSection(ReadyToRunSectionType.MethodDefEntryPoints) is null)
            return; // not an R2R layout that carries the section

        var rtFuncs = ReadyToRunReader.ReadRuntimeFunctions(image!, hdr, 0, 1);
        rtFuncs.IsError.Should().BeFalse();
        var runtimeFunctionCount = rtFuncs.Data!.TotalCount;

        var result = ReadyToRunReader.ReadMethodDefEntryPoints(image!, hdr, 2000);

        result.IsError.Should().BeFalse();
        var table = result.Data!;
        table.MethodCount.Should().BeGreaterThan(0u);
        table.Entries.Should().NotBeEmpty();

        var previousRid = 0;
        foreach (var e in table.Entries)
        {
            e.Rid.Should().BeGreaterThan(previousRid); // RIDs ascend
            e.Rid.Should().BeLessThanOrEqualTo((int)table.MethodCount);
            e.RuntimeFunctionIndex.Should().BeGreaterThanOrEqualTo(0);
            e.RuntimeFunctionIndex.Should().BeLessThan(runtimeFunctionCount);
            previousRid = e.Rid;
        }
    }

    // -----------------------------------------------------------------------
    // Helpers.
    // -----------------------------------------------------------------------

    /// <summary>NativeFormat unsigned encoder (inverse of DecodeUnsigned).</summary>
    private static byte[] Encoded(uint value)
    {
        var dest = new List<byte>();
        WriteUnsigned(dest, value);
        return dest.ToArray();
    }

    private static void WriteUnsigned(List<byte> dest, uint value)
    {
        if (value < 128)
        {
            dest.Add((byte)(value << 1));
        }
        else if (value < 128 * 128)
        {
            dest.Add((byte)((value << 2) | 1));
            dest.Add((byte)(value >> 6));
        }
        else if (value < 128 * 128 * 128)
        {
            dest.Add((byte)((value << 3) | 3));
            dest.Add((byte)(value >> 5));
            dest.Add((byte)(value >> 13));
        }
        else if (value < 128 * 128 * 128 * 128)
        {
            dest.Add((byte)((value << 4) | 7));
            dest.Add((byte)(value >> 4));
            dest.Add((byte)(value >> 12));
            dest.Add((byte)(value >> 20));
        }
        else
        {
            dest.Add(0x0F);
            var t = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(t, value);
            dest.AddRange(t);
        }
    }

    /// <summary>
    /// Builds a NativeArray where element <c>i*16</c> carries <c>payloads[i]</c>
    /// and every other slot is absent. Each block is therefore a single leaf
    /// node (0x00) followed by its payload.
    /// </summary>
    private static byte[] BuildSingleLeafPerBlockArray(byte[][] payloads, int entryIndexSize)
    {
        var blockCount = payloads.Length;
        var nElements = (uint)((blockCount - 1) * 16 + 1);

        var header = new List<byte>();
        WriteUnsigned(header, (nElements << 2) | (uint)entryIndexSize);
        var baseOffset = header.Count;

        var entryWidth = entryIndexSize == 0 ? 1 : entryIndexSize == 1 ? 2 : 4;
        var indexArraySize = blockCount * entryWidth;

        var blocks = new byte[blockCount][];
        for (var i = 0; i < blockCount; i++)
        {
            var b = new byte[1 + payloads[i].Length];
            b[0] = 0x00; // leaf for local index 0
            Array.Copy(payloads[i], 0, b, 1, payloads[i].Length);
            blocks[i] = b;
        }

        var starts = new int[blockCount];
        var cur = baseOffset + indexArraySize;
        for (var i = 0; i < blockCount; i++)
        {
            starts[i] = cur;
            cur += blocks[i].Length;
        }

        var blob = new List<byte>(header);
        for (var i = 0; i < blockCount; i++)
        {
            var rel = (uint)(starts[i] - baseOffset);
            switch (entryWidth)
            {
                case 1:
                    blob.Add((byte)rel);
                    break;
                case 2:
                    var t2 = new byte[2];
                    BinaryPrimitives.WriteUInt16LittleEndian(t2, (ushort)rel);
                    blob.AddRange(t2);
                    break;
                default:
                    var t4 = new byte[4];
                    BinaryPrimitives.WriteUInt32LittleEndian(t4, rel);
                    blob.AddRange(t4);
                    break;
            }
        }

        foreach (var b in blocks)
            blob.AddRange(b);

        return blob.ToArray();
    }

    /// <summary>
    /// Builds a NativeArray with <paramref name="nElements"/> slots that are all
    /// absent: every block's index entry points at a single shared leaf node whose
    /// encoded value (64 → <c>val&amp;3 == 0</c>, <c>val&gt;&gt;2 == 16</c>) matches no
    /// local index 0..15, so <see cref="NativeArray.TryGetAt"/> returns false for
    /// every slot without throwing.
    /// </summary>
    private static byte[] BuildAllAbsentArray(uint nElements, int entryIndexSize)
    {
        var blockCount = (int)((nElements + 15) / 16);

        var header = new List<byte>();
        WriteUnsigned(header, (nElements << 2) | (uint)entryIndexSize);
        var baseOffset = header.Count;

        var entryWidth = entryIndexSize == 0 ? 1 : entryIndexSize == 1 ? 2 : 4;
        var indexArraySize = blockCount * entryWidth;

        // Shared all-absent leaf placed immediately after the index table.
        var leafRel = (uint)indexArraySize;

        var blob = new List<byte>(header);
        for (var i = 0; i < blockCount; i++)
        {
            switch (entryWidth)
            {
                case 1:
                    blob.Add((byte)leafRel);
                    break;
                case 2:
                    var t2 = new byte[2];
                    BinaryPrimitives.WriteUInt16LittleEndian(t2, (ushort)leafRel);
                    blob.AddRange(t2);
                    break;
                default:
                    var t4 = new byte[4];
                    BinaryPrimitives.WriteUInt32LittleEndian(t4, leafRel);
                    blob.AddRange(t4);
                    break;
            }
        }

        // Encoded(64): val&3 == 0, val>>2 == 16 — out of the 0..15 local-index range.
        blob.AddRange(Encoded(64u));

        return blob.ToArray();
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

        var handle = ImageHandle.From("aabbccddee07", "synthetic_methodentry.dll");
        var clrSec = new NativeSection(".clr", ClrSectionVA, (ulong)clrSectionDataSize, ClrSectionRaw, (ulong)clrSectionFileSize);
        return new NativeImage(handle, "synthetic_methodentry.dll", BinaryFormat.Pe, Architecture.X64,
            [clrSec], [], new ReadOnlyMemory<byte>(bytes), 0x40000);
    }

    private static int Align(int value, int alignment) =>
        (value + alignment - 1) & ~(alignment - 1);
}
