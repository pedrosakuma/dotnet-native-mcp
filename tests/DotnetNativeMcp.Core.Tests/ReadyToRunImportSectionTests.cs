using System.Buffers.Binary;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.R2R;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class ReadyToRunImportSectionTests
{
    private sealed record ImportEntry(
        uint SectionRva,
        uint SectionSize,
        ushort Flags,
        byte Type,
        byte EntrySize,
        uint SignaturesRva,
        uint AuxiliaryDataRva);

    // -----------------------------------------------------------------------
    // Decoder unit tests
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0, "Unknown")]
    [InlineData(2, "StubDispatch")]
    [InlineData(3, "StringHandle")]
    [InlineData(7, "ILBodyFixups")]
    public void TypeName_KnownTypes_DecodeToNames(byte type, string expected)
    {
        ReadyToRunImportSectionDecoder.TypeName(type).Should().Be(expected);
    }

    [Fact]
    public void TypeName_UnknownType_ReturnsNumericValue()
    {
        ReadyToRunImportSectionDecoder.TypeName(42).Should().Be("42");
    }

    [Fact]
    public void DecodeFlagNames_Zero_ReturnsEmpty()
    {
        ReadyToRunImportSectionDecoder.DecodeFlagNames(0).Should().BeEmpty();
    }

    [Fact]
    public void DecodeFlagNames_KnownFlags_DecodeInBitOrder()
    {
        // 0x5 == Eager (0x1) | PCode (0x4)
        ReadyToRunImportSectionDecoder.DecodeFlagNames(0x0005)
            .Should().Equal("Eager", "PCode");
    }

    [Fact]
    public void DecodeFlagNames_UnknownBits_ReportedAsSingleUnknownEntry()
    {
        // 0x8000 is not a defined flag; 0x1 is Eager.
        ReadyToRunImportSectionDecoder.DecodeFlagNames(0x8001)
            .Should().Equal("Eager", "Unknown(0x8000)");
    }

    // -----------------------------------------------------------------------
    // ReadImportSections — synthetic
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadImportSections_MissingSection_ReturnsR2RSectionNotPresent()
    {
        var image = BuildSyntheticR2RWithImportSections();  // no entries → section omitted

        var hdr = ReadyToRunReader.ReadHeader(image).Data!;
        var result = ReadyToRunReader.ReadImportSections(image, hdr);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.R2RSectionNotPresent);
    }

    [Fact]
    public void ReadImportSections_DecodesAllFields()
    {
        var entries = new[]
        {
            new ImportEntry(0x4000, 0x80, (ushort)ReadyToRunImportSectionAttributes.Eager, 2, 8, 0x5000, 0x6000),
            new ImportEntry(0x4100, 0x40,
                (ushort)(ReadyToRunImportSectionAttributes.Eager | ReadyToRunImportSectionAttributes.PCode),
                3, 4, 0, 0),
        };
        var image = BuildSyntheticR2RWithImportSections(entries);

        var hdr = ReadyToRunReader.ReadHeader(image).Data!;
        var result = ReadyToRunReader.ReadImportSections(image, hdr);

        result.IsError.Should().BeFalse();
        var data = result.Data!;
        data.Should().HaveCount(2);

        var first = data[0];
        first.Index.Should().Be(0);
        first.SectionRva.Should().Be(0x4000);
        first.SectionSize.Should().Be(0x80);
        first.Flags.Should().Be((ushort)ReadyToRunImportSectionAttributes.Eager);
        first.Type.Should().Be(2);
        first.EntrySize.Should().Be(8);
        first.SignaturesRva.Should().Be(0x5000);
        first.AuxiliaryDataRva.Should().Be(0x6000);
        ReadyToRunImportSectionDecoder.TypeName(first.Type).Should().Be("StubDispatch");

        var second = data[1];
        second.Index.Should().Be(1);
        second.SectionRva.Should().Be(0x4100);
        second.Type.Should().Be(3);
        second.EntrySize.Should().Be(4);
        second.SignaturesRva.Should().Be(0);
        second.AuxiliaryDataRva.Should().Be(0);
        ReadyToRunImportSectionDecoder.DecodeFlagNames(second.Flags)
            .Should().Equal("Eager", "PCode");
    }

    [Fact]
    public void ReadImportSections_EmptySection_ReturnsEmptyList()
    {
        var image = BuildSyntheticR2RWithImportSections(Array.Empty<ImportEntry>(), emitEmptySection: true);

        var hdr = ReadyToRunReader.ReadHeader(image).Data!;
        var result = ReadyToRunReader.ReadImportSections(image, hdr);

        result.IsError.Should().BeFalse();
        result.Data!.Should().BeEmpty();
    }

    [Fact]
    public void ReadImportSections_DeclaredSizeBeyondFile_FailsGracefully()
    {
        // A crafted header whose section Size claims far more entries than the file actually
        // contains must be rejected — never throw, OOM, or read out of bounds.
        var entries = new[] { new ImportEntry(0x4000, 0x80, 0x1, 2, 8, 0, 0) };
        var image = BuildSyntheticR2RWithImportSections(entries, overrideSectionSize: 0xFFFFFFFFu);

        var hdr = ReadyToRunReader.ReadHeader(image).Data!;
        var result = ReadyToRunReader.ReadImportSections(image, hdr);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // -----------------------------------------------------------------------
    // ReadImportSections — real R2R image regression
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadImportSections_RealR2RImage_DecodesConsistently()
    {
        var spc = FixturePaths.SystemPrivateCoreLib;
        if (spc is null || !File.Exists(spc)) return;  // skip when no real R2R fixture is available

        var bytes = File.ReadAllBytes(spc);
        var image = PeNativeReader.Read(new ReadOnlyMemory<byte>(bytes), spc);
        image.Should().NotBeNull();

        var hdr = ReadyToRunReader.ReadHeader(image!).Data!;
        var section = hdr.FindSection(ReadyToRunSectionType.ImportSections);
        if (section is null) return;  // not every R2R image carries import sections

        section.Type.Should().Be(101u);

        var result = ReadyToRunReader.ReadImportSections(image!, hdr);
        result.IsError.Should().BeFalse(
            "ImportSections must be decodable on a real R2R image");

        // The decoded entry count must equal the section size divided by the 20-byte entry stride.
        result.Data!.Count.Should().Be((int)(section.Size / 20u));

        foreach (var entry in result.Data)
        {
            // Decoding flags must never throw or lose bits.
            var names = ReadyToRunImportSectionDecoder.DecodeFlagNames(entry.Flags);
            uint reencoded = 0;
            foreach (var name in names)
            {
                if (name.StartsWith("Unknown(0x", StringComparison.Ordinal))
                    reencoded |= Convert.ToUInt32(name["Unknown(0x".Length..].TrimEnd(')'), 16);
                else
                    reencoded |= (uint)Enum.Parse<ReadyToRunImportSectionAttributes>(name);
            }
            reencoded.Should().Be(entry.Flags, "decoded flag names must round-trip");
        }
    }

    // -----------------------------------------------------------------------
    // Synthetic PE factory with an ImportSections (type 101) section.
    // -----------------------------------------------------------------------

    private static NativeImage BuildSyntheticR2RWithImportSections(
        ImportEntry[]? entries = null,
        bool emitEmptySection = false,
        uint? overrideSectionSize = null)
    {
        const uint FileAlignment = 0x200;
        const uint ClrSectionVA = 0x2000;
        const uint ClrSectionRaw = 0x400;
        const uint R2RHeaderVA = ClrSectionVA + 72;
        const int ImportEntrySize = 20;

        bool hasSection = emitEmptySection || (entries is { Length: > 0 });
        int numR2RSections = hasSection ? 1 : 0;
        int r2rHeaderSize = 16 + numR2RSections * 12;

        uint importTableVA = R2RHeaderVA + (uint)r2rHeaderSize;
        int importTableSize = (entries?.Length ?? 0) * ImportEntrySize;

        int clrSectionDataSize = 72 + r2rHeaderSize + importTableSize;
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
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(r2rOff + 12), (uint)numR2RSections);

        if (hasSection)
        {
            int secEntOff = r2rOff + 16;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 0), (uint)ReadyToRunSectionType.ImportSections);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 4), importTableVA);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 8), overrideSectionSize ?? (uint)importTableSize);

            int tableOff = r2rOff + r2rHeaderSize;
            for (var i = 0; i < (entries?.Length ?? 0); i++)
            {
                var e = entries![i];
                int o = tableOff + i * ImportEntrySize;
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(o + 0), e.SectionRva);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(o + 4), e.SectionSize);
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(o + 8), e.Flags);
                bytes[o + 10] = e.Type;
                bytes[o + 11] = e.EntrySize;
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(o + 12), e.SignaturesRva);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(o + 16), e.AuxiliaryDataRva);
            }
        }

        var handle = ImageHandle.From("aabbccddee01", "synthetic_import.dll");
        var clrSec = new NativeSection(".clr", ClrSectionVA, (ulong)clrSectionDataSize, ClrSectionRaw, (ulong)clrSectionFileSize);
        return new NativeImage(handle, "synthetic_import.dll", BinaryFormat.Pe, Architecture.X64,
            [clrSec], [], new ReadOnlyMemory<byte>(bytes), 0x40000);
    }

    private static int Align(int value, int alignment) =>
        (value + alignment - 1) & ~(alignment - 1);
}
