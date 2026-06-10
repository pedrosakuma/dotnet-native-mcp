using System.Buffers.Binary;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.R2R;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class ReadyToRunCompositeMetadataTests
{
    // -----------------------------------------------------------------------
    // ReadComponentAssemblies — synthetic
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadComponentAssemblies_DecodesEntries()
    {
        // Two 16-byte entries: {corRva, corSize, asmRva, asmSize}.
        var payload = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0), 0x1111u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), 0x10u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), 0x2222u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), 0x20u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16), 0x3333u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20), 0x30u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(24), 0x4444u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(28), 0x40u);

        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ComponentAssemblies, payload);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadComponentAssemblies(image, hdr);
        result.IsError.Should().BeFalse();
        result.Data!.Should().HaveCount(2);
        result.Data![0].Should().Be(new ReadyToRunComponentAssembly(0, 0x1111u, 0x10u, 0x2222u, 0x20u));
        result.Data![1].Should().Be(new ReadyToRunComponentAssembly(1, 0x3333u, 0x30u, 0x4444u, 0x40u));
    }

    [Fact]
    public void ReadComponentAssemblies_SectionAbsent_Fails()
    {
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ManifestAssemblyMvids, new byte[16]);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadComponentAssemblies(image, hdr);
        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.R2RSectionNotPresent);
    }

    [Fact]
    public void ReadComponentAssemblies_EmptySection_ReturnsEmpty()
    {
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ComponentAssemblies, Array.Empty<byte>(), emitZeroSizeSection: true);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadComponentAssemblies(image, hdr);
        result.IsError.Should().BeFalse();
        result.Data!.Should().BeEmpty();
    }

    [Fact]
    public void ReadComponentAssemblies_DeclaredSizeBeyondFile_Fails()
    {
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ComponentAssemblies, new byte[16], overrideSectionSize: 0xFFFFFF00u);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadComponentAssemblies(image, hdr);
        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // -----------------------------------------------------------------------
    // ReadManifestAssemblyMvids — synthetic
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadManifestAssemblyMvids_DecodesGuids()
    {
        var g0 = Guid.NewGuid();
        var g1 = Guid.NewGuid();
        var payload = new byte[32];
        g0.ToByteArray().CopyTo(payload, 0);
        g1.ToByteArray().CopyTo(payload, 16);

        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ManifestAssemblyMvids, payload);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadManifestAssemblyMvids(image, hdr);
        result.IsError.Should().BeFalse();
        result.Data!.Should().Equal(g0, g1);
    }

    [Fact]
    public void ReadManifestAssemblyMvids_SectionAbsent_Fails()
    {
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ComponentAssemblies, new byte[16]);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadManifestAssemblyMvids(image, hdr);
        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.R2RSectionNotPresent);
    }

    [Fact]
    public void ReadManifestAssemblyMvids_EmptySection_ReturnsEmpty()
    {
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ManifestAssemblyMvids, Array.Empty<byte>(), emitZeroSizeSection: true);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadManifestAssemblyMvids(image, hdr);
        result.IsError.Should().BeFalse();
        result.Data!.Should().BeEmpty();
    }

    [Fact]
    public void ReadManifestAssemblyMvids_DeclaredSizeBeyondFile_Fails()
    {
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ManifestAssemblyMvids, new byte[16], overrideSectionSize: 0xFFFFFF00u);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadManifestAssemblyMvids(image, hdr);
        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // -----------------------------------------------------------------------
    // Real R2R image regression — standalone SPC is NOT composite, so the
    // sections are absent and the readers must fail gracefully (no throw).
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadComposite_RealNonCompositeImage_FailsGracefully()
    {
        var spc = FixturePaths.SystemPrivateCoreLib;
        if (spc is null || !File.Exists(spc)) return;  // skip when no real R2R fixture is available

        var bytes = File.ReadAllBytes(spc);
        var image = PeNativeReader.Read(new ReadOnlyMemory<byte>(bytes), spc);
        image.Should().NotBeNull();

        var hdr = ReadyToRunReader.ReadHeader(image!).Data!;

        if (hdr.FindSection(ReadyToRunSectionType.ComponentAssemblies) is null)
            ReadyToRunReader.ReadComponentAssemblies(image!, hdr).Error!.Kind
                .Should().Be(ErrorKinds.R2RSectionNotPresent);

        if (hdr.FindSection(ReadyToRunSectionType.ManifestAssemblyMvids) is null)
            ReadyToRunReader.ReadManifestAssemblyMvids(image!, hdr).Error!.Kind
                .Should().Be(ErrorKinds.R2RSectionNotPresent);
    }

    // -----------------------------------------------------------------------
    // Synthetic PE factory carrying a single raw-bytes R2R section.
    // -----------------------------------------------------------------------

    private static NativeImage BuildSyntheticR2RWithRawSection(
        uint sectionType,
        byte[] payload,
        bool emitZeroSizeSection = false,
        uint? overrideSectionSize = null)
    {
        const uint FileAlignment = 0x200;
        const uint ClrSectionVA = 0x2000;
        const uint ClrSectionRaw = 0x400;
        const uint R2RHeaderVA = ClrSectionVA + 72;

        const int numR2RSections = 1;
        int r2rHeaderSize = 16 + numR2RSections * 12;

        if (emitZeroSizeSection)
            payload = Array.Empty<byte>();

        uint payloadVA = R2RHeaderVA + (uint)r2rHeaderSize;
        int payloadSize = payload.Length;

        int clrSectionDataSize = 72 + r2rHeaderSize + payloadSize;
        int clrSectionFileSize = Align(clrSectionDataSize, (int)FileAlignment);
        int totalSize = (int)ClrSectionRaw + clrSectionFileSize;
        var bytes = new byte[totalSize];

        // DOS + PE
        bytes[0] = 0x4D; bytes[1] = 0x5A;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x3C), 0x80);
        int peOff = 0x80;
        bytes[peOff] = (byte)'P'; bytes[peOff + 1] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOff + 4), 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOff + 6), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOff + 20), 0xF0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOff + 22), 0x2022);

        int optOff = peOff + 24;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(optOff), 0x20B);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optOff + 56), 0x40000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optOff + 60), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optOff + 64), FileAlignment);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(optOff + 40), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optOff + 92), 16);

        int ddBase = optOff + 112;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ddBase + 14 * 8), ClrSectionVA);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ddBase + 14 * 8 + 4), 72);

        int secTableOff = peOff + 24 + 0xF0;
        bytes[secTableOff] = (byte)'.'; bytes[secTableOff + 1] = (byte)'c';
        bytes[secTableOff + 2] = (byte)'l'; bytes[secTableOff + 3] = (byte)'r';
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 8), (uint)clrSectionDataSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 12), ClrSectionVA);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 16), (uint)clrSectionFileSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 20), ClrSectionRaw);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 36), 0x40000040u);

        int clrOff = (int)ClrSectionRaw;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clrOff + 0), 72);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clrOff + 64), R2RHeaderVA);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clrOff + 68), (uint)r2rHeaderSize);

        int r2rOff = clrOff + 72;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(r2rOff + 0), 0x00525452u);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(r2rOff + 4), 6);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(r2rOff + 6), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(r2rOff + 8), 0x00000003u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(r2rOff + 12), numR2RSections);

        int secEntOff = r2rOff + 16;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 0), sectionType);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 4), payloadVA);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 8), overrideSectionSize ?? (uint)payloadSize);

        int tableOff = r2rOff + r2rHeaderSize;
        payload.CopyTo(bytes.AsSpan(tableOff));

        var handle = ImageHandle.From("aabbccddee05", "synthetic_composite.dll");
        var clrSec = new NativeSection(".clr", ClrSectionVA, (ulong)clrSectionDataSize, ClrSectionRaw, (ulong)clrSectionFileSize);
        return new NativeImage(handle, "synthetic_composite.dll", BinaryFormat.Pe, Architecture.X64,
            [clrSec], [], new ReadOnlyMemory<byte>(bytes), 0x40000);
    }

    private static int Align(int value, int alignment) =>
        (value + alignment - 1) & ~(alignment - 1);
}
