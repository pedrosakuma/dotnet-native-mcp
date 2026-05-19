using System.Buffers.Binary;
using System.Text;
using DotnetNativeMcp.Core;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public sealed class NativeStringExtractorTests
{
    [Fact]
    public void Extract_strings_finds_hi_literal_in_sample_aot_fixture()
    {
        using var fixture = SampleAotFixture.Create();

        var result = NativeStringExtractor.Extract(fixture.Path, minLength: 2);

        result.Items.Should().Contain(x => x.Value == "hi");
    }

    [Fact]
    public void Extract_strings_detects_utf16le_string_pool_entries()
    {
        using var fixture = SampleAotFixture.Create();

        var result = NativeStringExtractor.Extract(fixture.Path);

        result.Items.Should().Contain(x => x.Encoding == "utf-16le" && x.Value == "ManagedPool");
    }

    [Fact]
    public void Extract_strings_respects_section_filter()
    {
        using var fixture = SampleAotFixture.Create();

        var rodataOnly = NativeStringExtractor.Extract(fixture.Path, [".rodata"], minLength: 4);
        var textOnly = NativeStringExtractor.Extract(fixture.Path, [".text"], minLength: 4);

        rodataOnly.Items.Should().NotContain(x => x.Value == "CODEIMM");
        textOnly.Items.Should().Contain(x => x.Value == "CODEIMM");
    }

    private sealed class SampleAotFixture : IDisposable
    {
        private SampleAotFixture(string path) => Path = path;

        public string Path { get; }

        public static SampleAotFixture Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sample-aot-{Guid.NewGuid():N}.elf");
            File.WriteAllBytes(path, BuildElfFixture());
            return new(path);
        }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }

        private static byte[] BuildElfFixture()
        {
            const int headerOffset = 0x0;
            const int textOffset = 0x100;
            const int rodataOffset = 0x140;
            const int shstrOffset = 0x1C0;
            const int sectionHeadersOffset = 0x240;
            const int sectionHeaderSize = 0x40;
            const int sectionCount = 4;

            var textBytes = Encoding.ASCII.GetBytes("CODEIMM\0");
            var rodataAscii = Encoding.ASCII.GetBytes("hello-from-rodata\0hi\0\0");
            var rodataUtf16 = Encoding.Unicode.GetBytes("ManagedPool\0");
            var rodataBytes = rodataAscii.Concat(rodataUtf16).ToArray();
            var shstrBytes = Encoding.ASCII.GetBytes("\0.text\0.rodata\0.shstrtab\0");

            var image = new byte[sectionHeadersOffset + (sectionCount * sectionHeaderSize)];

            // ELF64 little-endian header
            image[0] = 0x7F;
            image[1] = (byte)'E';
            image[2] = (byte)'L';
            image[3] = (byte)'F';
            image[4] = 2; // 64-bit
            image[5] = 1; // little-endian
            image[6] = 1; // version
            WriteUInt16(image, headerOffset + 0x34, 0x40); // e_ehsize
            WriteUInt64(image, headerOffset + 0x28, (ulong)sectionHeadersOffset); // e_shoff
            WriteUInt16(image, headerOffset + 0x3A, sectionHeaderSize); // e_shentsize
            WriteUInt16(image, headerOffset + 0x3C, sectionCount); // e_shnum
            WriteUInt16(image, headerOffset + 0x3E, 3); // e_shstrndx

            Array.Copy(textBytes, 0, image, textOffset, textBytes.Length);
            Array.Copy(rodataBytes, 0, image, rodataOffset, rodataBytes.Length);
            Array.Copy(shstrBytes, 0, image, shstrOffset, shstrBytes.Length);

            // Section 1: .text
            WriteSectionHeader(image, sectionHeadersOffset + sectionHeaderSize, 1, textOffset, textBytes.Length);
            // Section 2: .rodata
            WriteSectionHeader(image, sectionHeadersOffset + (sectionHeaderSize * 2), 7, rodataOffset, rodataBytes.Length);
            // Section 3: .shstrtab
            WriteSectionHeader(image, sectionHeadersOffset + (sectionHeaderSize * 3), 15, shstrOffset, shstrBytes.Length, sectionType: 3);

            return image;
        }

        private static void WriteSectionHeader(byte[] image, int offset, uint nameOffset, int sectionOffset, int sectionSize, uint sectionType = 1)
        {
            WriteUInt32(image, offset, nameOffset); // sh_name
            WriteUInt32(image, offset + 0x4, sectionType); // sh_type
            WriteUInt64(image, offset + 0x18, (ulong)sectionOffset); // sh_offset
            WriteUInt64(image, offset + 0x20, (ulong)sectionSize); // sh_size
        }

        private static void WriteUInt16(byte[] image, int offset, int value) =>
            BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(offset, 2), checked((ushort)value));

        private static void WriteUInt32(byte[] image, int offset, uint value) =>
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(offset, 4), value);

        private static void WriteUInt64(byte[] image, int offset, ulong value) =>
            BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(offset, 8), value);
    }
}
