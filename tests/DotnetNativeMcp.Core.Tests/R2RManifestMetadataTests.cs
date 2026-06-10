using System.Buffers.Binary;
using System.Text;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.R2R;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Tests for the ManifestMetadata section (type 112) — the embedded ECMA-335
/// metadata blob surfaced as a handoff descriptor (offset/RVA/size + metadata
/// root version and stream directory).
/// </summary>
public class R2RManifestMetadataTests
{
    [Fact]
    public void ReadManifestMetadata_MissingSection_ReturnsNotPresent()
    {
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.AvailableTypes, new byte[8]);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadManifestMetadata(image, hdr);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.R2RSectionNotPresent);
    }

    [Fact]
    public void ReadManifestMetadata_DecodesVersionAndStreams()
    {
        var blob = BuildEcmaMetadataRoot(
            "v4.0.30319",
            ("#~", 0x6Cu, 0x100u),
            ("#Strings", 0x16Cu, 0x80u),
            ("#US", 0x1ECu, 0x10u),
            ("#GUID", 0x1FCu, 0x10u),
            ("#Blob", 0x20Cu, 0x40u));
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ManifestMetadata, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadManifestMetadata(image, hdr);

        result.IsError.Should().BeFalse();
        var m = result.Data!;
        m.Version.Should().Be("v4.0.30319");
        m.MajorVersion.Should().Be(1);
        m.MinorVersion.Should().Be(1);
        m.Size.Should().Be((uint)blob.Length);
        m.FileOffset.Should().BeGreaterThan(0);
        m.Streams.Select(s => s.Name).Should().Equal("#~", "#Strings", "#US", "#GUID", "#Blob");
        m.Streams[0].Offset.Should().Be(0x6Cu);
        m.Streams[0].Size.Should().Be(0x100u);
    }

    [Fact]
    public void ReadManifestMetadata_BadSignature_FailsGracefully()
    {
        var blob = BuildEcmaMetadataRoot("v4.0.30319", ("#~", 0x6Cu, 0x10u));
        // Corrupt the BSJB signature.
        blob[0] = 0xFF;
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ManifestMetadata, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadManifestMetadata(image, hdr);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void ReadManifestMetadata_TruncatedBeforeStreams_FailsGracefully()
    {
        // A valid root header but no flags/stream-count bytes.
        var version = Encoding.UTF8.GetBytes("v4.0.30319");
        var padded = (version.Length + 1 + 3) & ~3;
        var blob = new byte[16 + padded];
        BinaryPrimitives.WriteUInt32LittleEndian(blob, 0x424A5342);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(6), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(12), (uint)padded);
        version.CopyTo(blob.AsSpan(16));
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ManifestMetadata, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadManifestMetadata(image, hdr);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void ReadManifestMetadata_StreamCountBeyondBlob_FailsGracefully()
    {
        // Declares 5 streams but provides only one header's worth of bytes.
        var blob = BuildEcmaMetadataRoot("v4.0.30319", ("#~", 0x6Cu, 0x10u));
        var afterVersion = 16 + ((Encoding.UTF8.GetByteCount("v4.0.30319") + 1 + 3) & ~3);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(afterVersion + 2), 5);
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ManifestMetadata, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadManifestMetadata(image, hdr);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void ReadManifestMetadata_VersionLengthBeyondBlob_FailsGracefully()
    {
        var blob = BuildEcmaMetadataRoot("v4.0.30319", ("#~", 0x6Cu, 0x10u));
        // Set an absurd version length.
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(12), 0xFFFFu);
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ManifestMetadata, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadManifestMetadata(image, hdr);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void ReadManifestMetadata_VersionNotNullTerminated_FailsGracefully()
    {
        // VersionLength fully consumed by non-NUL bytes (no terminator).
        var blob = new byte[16 + 12 + 4 + 12];
        BinaryPrimitives.WriteUInt32LittleEndian(blob, 0x424A5342);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(6), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(12), 12);
        for (var i = 0; i < 12; i++) blob[16 + i] = (byte)'A'; // no NUL
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ManifestMetadata, blob);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadManifestMetadata(image, hdr);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void ReadManifestMetadata_StreamNamePaddingTruncated_FailsGracefully()
    {
        // Build a one-stream root, then chop the trailing name-padding bytes so the
        // padded name length runs past the blob end.
        var blob = BuildEcmaMetadataRoot("v4.0.30319", ("#~", 0x6Cu, 0x10u));
        // The "#~\0" name pads to 4 bytes; remove the final padding byte.
        var truncated = blob.AsSpan(0, blob.Length - 1).ToArray();
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ManifestMetadata, truncated);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadManifestMetadata(image, hdr);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // -----------------------------------------------------------------------
    // Real R2R image regression — System.Private.CoreLib carries a manifest.
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadManifestMetadata_RealImage_LocatesEcmaBlob()
    {
        var spc = FixturePaths.SystemPrivateCoreLib;
        if (spc is null || !File.Exists(spc)) return;

        var bytes = File.ReadAllBytes(spc);
        var image = PeNativeReader.Read(new ReadOnlyMemory<byte>(bytes), spc);
        image.Should().NotBeNull();
        var hdr = ReadyToRunReader.ReadHeader(image!).Data!;

        if (hdr.FindSection(ReadyToRunSectionType.ManifestMetadata) is null)
            return; // not all R2R layouts carry the section

        var result = ReadyToRunReader.ReadManifestMetadata(image!, hdr);

        result.IsError.Should().BeFalse();
        var m = result.Data!;
        m.Size.Should().BeGreaterThan(0u);
        m.FileOffset.Should().BeGreaterThan(0);
        m.Version.Should().StartWith("v");
        m.Streams.Should().NotBeEmpty();
        m.Streams.Select(s => s.Name).Should().Contain(n => n == "#~" || n == "#-");
    }

    // -----------------------------------------------------------------------
    // ECMA-335 II.24.2.1 metadata-root blob builder.
    // -----------------------------------------------------------------------

    private static byte[] BuildEcmaMetadataRoot(string version, params (string Name, uint Offset, uint Size)[] streams)
    {
        var versionBytes = Encoding.UTF8.GetBytes(version);
        var versionLen = (versionBytes.Length + 1 + 3) & ~3; // null-terminated, 4-byte padded

        var streamHeaders = new List<byte[]>();
        foreach (var (name, offset, size) in streams)
        {
            var nameBytes = Encoding.ASCII.GetBytes(name);
            var nameLen = (nameBytes.Length + 1 + 3) & ~3;
            var hdr = new byte[8 + nameLen];
            BinaryPrimitives.WriteUInt32LittleEndian(hdr, offset);
            BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(4), size);
            nameBytes.CopyTo(hdr.AsSpan(8));
            streamHeaders.Add(hdr);
        }

        var total = 16 + versionLen + 4 + streamHeaders.Sum(h => h.Length);
        var blob = new byte[total];
        BinaryPrimitives.WriteUInt32LittleEndian(blob, 0x424A5342); // "BSJB"
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(4), 1); // MajorVersion
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(6), 1); // MinorVersion
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(8), 0); // Reserved
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(12), (uint)versionLen);
        versionBytes.CopyTo(blob.AsSpan(16));

        var pos = 16 + versionLen;
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(pos), 0); // Flags
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(pos + 2), (ushort)streams.Length);
        pos += 4;
        foreach (var hdr in streamHeaders)
        {
            hdr.CopyTo(blob.AsSpan(pos));
            pos += hdr.Length;
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

        var handle = ImageHandle.From("aabbccddee12", "synthetic_manifest.dll");
        var clrSec = new NativeSection(".clr", ClrSectionVA, (ulong)clrSectionDataSize, ClrSectionRaw, (ulong)clrSectionFileSize);
        return new NativeImage(handle, "synthetic_manifest.dll", BinaryFormat.Pe, Architecture.X64,
            [clrSec], [], new ReadOnlyMemory<byte>(bytes), 0x40000);
    }

    private static int Align(int value, int alignment) =>
        (value + alignment - 1) & ~(alignment - 1);
}
