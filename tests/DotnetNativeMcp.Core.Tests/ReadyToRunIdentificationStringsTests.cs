using System.Buffers.Binary;
using System.Text;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.R2R;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class ReadyToRunIdentificationStringsTests
{
    // -----------------------------------------------------------------------
    // ReadCompilerIdentifier / ReadOwnerCompositeExecutable — synthetic
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadCompilerIdentifier_DecodesZeroTerminatedUtf8()
    {
        const string identifier = "Crossgen2 9.0.0 synthetic";
        var image = BuildSyntheticR2RWithStringSection(
            (uint)ReadyToRunSectionType.CompilerIdentifier, identifier);

        var hdr = ReadyToRunReader.ReadHeader(image).Data!;
        ReadyToRunReader.ReadCompilerIdentifier(image, hdr).Should().Be(identifier);
    }

    [Fact]
    public void ReadOwnerCompositeExecutable_DecodesZeroTerminatedUtf8()
    {
        const string owner = "framework.r2r.dll";
        var image = BuildSyntheticR2RWithStringSection(
            (uint)ReadyToRunSectionType.OwnerCompositeExecutable, owner);

        var hdr = ReadyToRunReader.ReadHeader(image).Data!;
        ReadyToRunReader.ReadOwnerCompositeExecutable(image, hdr).Should().Be(owner);
    }

    [Fact]
    public void ReadCompilerIdentifier_SectionAbsent_ReturnsNull()
    {
        // An image that only carries an OwnerCompositeExecutable section has no CompilerIdentifier.
        var image = BuildSyntheticR2RWithStringSection(
            (uint)ReadyToRunSectionType.OwnerCompositeExecutable, "x.dll");

        var hdr = ReadyToRunReader.ReadHeader(image).Data!;
        ReadyToRunReader.ReadCompilerIdentifier(image, hdr).Should().BeNull();
    }

    [Fact]
    public void ReadCompilerIdentifier_DeclaredSizeBeyondFile_ReturnsNullGracefully()
    {
        // A crafted section size that runs past the end of the file must not throw — it is
        // best-effort auxiliary metadata, so the decoder returns null.
        var image = BuildSyntheticR2RWithStringSection(
            (uint)ReadyToRunSectionType.CompilerIdentifier, "abc", overrideSectionSize: 0xFFFFFFFFu);

        var hdr = ReadyToRunReader.ReadHeader(image).Data!;
        ReadyToRunReader.ReadCompilerIdentifier(image, hdr).Should().BeNull();
    }

    [Fact]
    public void ReadCompilerIdentifier_EmptySection_ReturnsNull()
    {
        var image = BuildSyntheticR2RWithStringSection(
            (uint)ReadyToRunSectionType.CompilerIdentifier, string.Empty, emitZeroSizeSection: true);

        var hdr = ReadyToRunReader.ReadHeader(image).Data!;
        ReadyToRunReader.ReadCompilerIdentifier(image, hdr).Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // ReadCompilerIdentifier — real R2R image regression
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadCompilerIdentifier_RealR2RImage_IsNonEmpty()
    {
        var spc = FixturePaths.SystemPrivateCoreLib;
        if (spc is null || !File.Exists(spc)) return;  // skip when no real R2R fixture is available

        var bytes = File.ReadAllBytes(spc);
        var image = PeNativeReader.Read(new ReadOnlyMemory<byte>(bytes), spc);
        image.Should().NotBeNull();

        var hdr = ReadyToRunReader.ReadHeader(image!).Data!;
        if (hdr.FindSection(ReadyToRunSectionType.CompilerIdentifier) is null) return;

        var identifier = ReadyToRunReader.ReadCompilerIdentifier(image!, hdr);
        identifier.Should().NotBeNullOrWhiteSpace(
            "a real R2R image's CompilerIdentifier section must decode to a printable string");
    }

    // -----------------------------------------------------------------------
    // Synthetic PE factory carrying a single string-payload R2R section.
    // -----------------------------------------------------------------------

    private static NativeImage BuildSyntheticR2RWithStringSection(
        uint sectionType,
        string value,
        bool emitZeroSizeSection = false,
        uint? overrideSectionSize = null)
    {
        const uint FileAlignment = 0x200;
        const uint ClrSectionVA = 0x2000;
        const uint ClrSectionRaw = 0x400;
        const uint R2RHeaderVA = ClrSectionVA + 72;

        const int numR2RSections = 1;
        int r2rHeaderSize = 16 + numR2RSections * 12;

        // Payload is the zero-terminated UTF-8 string; stored Size includes the terminator.
        byte[] payload = emitZeroSizeSection
            ? Array.Empty<byte>()
            : [.. Encoding.UTF8.GetBytes(value), 0];

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

        var handle = ImageHandle.From("aabbccddee02", "synthetic_idstr.dll");
        var clrSec = new NativeSection(".clr", ClrSectionVA, (ulong)clrSectionDataSize, ClrSectionRaw, (ulong)clrSectionFileSize);
        return new NativeImage(handle, "synthetic_idstr.dll", BinaryFormat.Pe, Architecture.X64,
            [clrSec], [], new ReadOnlyMemory<byte>(bytes), 0x40000);
    }

    private static int Align(int value, int alignment) =>
        (value + alignment - 1) & ~(alignment - 1);
}
