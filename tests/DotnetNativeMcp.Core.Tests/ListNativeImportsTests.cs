using System.Buffers.Binary;
using System.Text;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Server.Tools;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class ListNativeImportsTests
{
    [Fact]
    public void PeReader_SyntheticMinimalPe_ReturnsImportedLibrariesAndFunctions()
    {
        var image = CreateSyntheticPeImage(CreateSyntheticPeBytes());

        var libraries = PeNativeReader.ReadImportedLibraries(image);
        var functions = PeNativeReader.ReadImportedFunctions(image);

        libraries.IsError.Should().BeFalse();
        libraries.Data!.Select(row => row.Name).Should().Equal("kernel32.dll", "ntdll.dll");
        functions.IsError.Should().BeFalse();
        functions.Data!.Should().ContainEquivalentOf(new ImportedFunction("kernel32.dll", "GetProcAddress", null));
        functions.Data!.Should().ContainEquivalentOf(new ImportedFunction("kernel32.dll", "#123", 123));
        functions.Data!.Should().ContainEquivalentOf(new ImportedFunction("ntdll.dll", "NtClose", null));
    }

    [Fact]
    public void PeReader_MalformedImportTable_ReturnsTypedError()
    {
        var image = CreateSyntheticPeImage(CreateSyntheticPeBytes(malformed: true));

        var result = PeNativeReader.ReadImportedFunctions(image);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InternalError);
    }

    [Fact]
    public void ElfReader_SampleAot_ReturnsImportedLibrariesAndFunctions()
    {
        var fixturePath = FixturePaths.SampleAot;
        if (fixturePath is null || !File.Exists(fixturePath))
            return;

        var bytes = File.ReadAllBytes(fixturePath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), fixturePath);
        image.Should().NotBeNull();

        var libraries = ElfReader.ReadImportedLibraries(image!);
        var functions = ElfReader.ReadImportedFunctions(image!);

        libraries.IsError.Should().BeFalse();
        libraries.Data!.Select(row => row.Name).Should().Contain("libc.so.6");
        functions.IsError.Should().BeFalse();
        functions.Data!.Should().Contain(row => row.Name.Contains("mprotect", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ListNativeImports_UnknownHandle_ReturnsBinaryNotFound()
    {
        var tool = new NativeTools(new ImportTestBinaryRegistry());

        var result = tool.ListNativeImports("i:deadbeef:00000000");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.BinaryNotFound);
    }

    [Fact]
    public void ListNativeImports_InvalidKind_ReturnsInvalidArgument()
    {
        var image = CreateSyntheticPeImage(CreateSyntheticPeBytes());
        var tool = new NativeTools(new ImportTestBinaryRegistry(image));

        var result = tool.ListNativeImports(image.Handle.Value, kind: "bogus");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
        result.Error.Message.Should().Contain("functions").And.Contain("libraries");
    }

    [Fact]
    public void ListNativeImports_Functions_PaginatesAndFiltersByLibrary()
    {
        var image = CreateSyntheticPeImage(CreateSyntheticPeBytes());
        var tool = new NativeTools(new ImportTestBinaryRegistry(image));

        var page = tool.ListNativeImports(image.Handle.Value, kind: "functions", pageSize: 2);
        var filtered = tool.ListNativeImports(image.Handle.Value, kind: "functions", nameFilter: "ntdll");

        page.IsError.Should().BeFalse();
        page.Data!.Kind.Should().Be("functions");
        page.Data!.Functions!.Should().HaveCount(2);
        page.Data!.TotalCount.Should().Be(3);
        page.Data!.NextCursor.Should().Be(2);
        page.Hints.Should().ContainSingle(hint => hint.NextTool == "list_native_imports");

        filtered.IsError.Should().BeFalse();
        filtered.Data!.Functions!.Should().ContainSingle();
        filtered.Data!.Functions![0].Library.Should().Be("ntdll.dll");
        filtered.Data!.Functions![0].Name.Should().Be("NtClose");
    }

    [Fact]
    public void ListNativeImports_Libraries_ReturnsDrillDownHint()
    {
        var image = CreateSyntheticPeImage(CreateSyntheticPeBytes());
        var tool = new NativeTools(new ImportTestBinaryRegistry(image));

        var result = tool.ListNativeImports(image.Handle.Value, kind: "libraries");

        result.IsError.Should().BeFalse();
        result.Data!.Kind.Should().Be("libraries");
        result.Data!.Libraries!.Select(row => row.Name).Should().Equal("kernel32.dll", "ntdll.dll");
        result.Hints.Any(hint =>
            hint.NextTool == "list_native_imports" &&
            hint.SuggestedArguments is not null &&
            Equals(hint.SuggestedArguments["kind"], "functions") &&
            Equals(hint.SuggestedArguments["nameFilter"], "kernel32.dll")).Should().BeTrue();
    }

    [Fact]
    public void ListNativeImports_MalformedImportTable_ReturnsEmptyDataAndTypedError()
    {
        var image = CreateSyntheticPeImage(CreateSyntheticPeBytes(malformed: true), fileName: "broken.dll");
        var tool = new NativeTools(new ImportTestBinaryRegistry(image));

        var result = tool.ListNativeImports(image.Handle.Value, kind: "functions");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InternalError);
        result.Data.Should().NotBeNull();
        result.Data!.Kind.Should().Be("functions");
        result.Data!.Functions!.Should().BeEmpty();
        result.Data!.TotalCount.Should().Be(0);
        result.Data!.NextCursor.Should().BeNull();
    }

    private static NativeImage CreateSyntheticPeImage(byte[] bytes, string buildId = "aabb", string fileName = "imports.dll")
    {
        var handle = ImageHandle.From(buildId, fileName);
        var section = new NativeSection(".idata", 0x1000, 0x200, 0x200, 0x200);
        return new NativeImage(handle, fileName, BinaryFormat.Pe, Architecture.X64, [section], [], bytes, 0x140000000);
    }

    private static byte[] CreateSyntheticPeBytes(bool malformed = false)
    {
        var bytes = new byte[0x400];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        WriteUInt32(0x3C, 0x80);
        Encoding.ASCII.GetBytes("PE\0\0").CopyTo(bytes, 0x80);

        const int coffHeaderOffset = 0x84;
        WriteUInt16(coffHeaderOffset + 0, 0x8664);
        WriteUInt16(coffHeaderOffset + 2, 1);
        WriteUInt16(coffHeaderOffset + 16, 0xF0);
        WriteUInt16(coffHeaderOffset + 18, 0x2022);

        const int optionalHeaderOffset = 0x98;
        WriteUInt16(optionalHeaderOffset + 0, 0x20B);
        bytes[optionalHeaderOffset + 2] = 0x0E;
        WriteUInt32(optionalHeaderOffset + 16, 0x1000);
        WriteUInt32(optionalHeaderOffset + 20, 0x1000);
        WriteUInt64(optionalHeaderOffset + 24, 0x0000000140000000);
        WriteUInt32(optionalHeaderOffset + 32, 0x1000);
        WriteUInt32(optionalHeaderOffset + 36, 0x200);
        WriteUInt32(optionalHeaderOffset + 56, 0x2000);
        WriteUInt32(optionalHeaderOffset + 60, 0x200);
        WriteUInt16(optionalHeaderOffset + 68, 3);
        WriteUInt32(optionalHeaderOffset + 108, 16);

        const int dataDirectoryOffset = optionalHeaderOffset + 112;
        WriteUInt32(dataDirectoryOffset + 8, 0x1000);
        WriteUInt32(dataDirectoryOffset + 12, 0x40);

        const int sectionHeaderOffset = 0x188;
        Encoding.ASCII.GetBytes(".idata\0\0").CopyTo(bytes, sectionHeaderOffset);
        WriteUInt32(sectionHeaderOffset + 8, 0x200);
        WriteUInt32(sectionHeaderOffset + 12, 0x1000);
        WriteUInt32(sectionHeaderOffset + 16, 0x200);
        WriteUInt32(sectionHeaderOffset + 20, 0x200);
        WriteUInt32(sectionHeaderOffset + 36, 0x40000040);

        WriteUInt32(0x200, 0x1040);
        WriteUInt32(0x20C, malformed ? 0x3000u : 0x10A0u);
        WriteUInt32(0x210, 0x1058);

        WriteUInt32(0x214, 0x1070);
        WriteUInt32(0x220, 0x10C8);
        WriteUInt32(0x224, 0x1088);

        WriteUInt64(0x240, 0x10B0);
        WriteUInt64(0x248, 0x800000000000007B);
        WriteUInt64(0x250, 0x0);

        WriteUInt64(0x258, 0x10B0);
        WriteUInt64(0x260, 0x800000000000007B);
        WriteUInt64(0x268, 0x0);

        WriteUInt64(0x270, 0x10E0);
        WriteUInt64(0x278, 0x0);

        WriteUInt64(0x288, 0x10E0);
        WriteUInt64(0x290, 0x0);

        Encoding.ASCII.GetBytes("kernel32.dll\0").CopyTo(bytes, 0x2A0);
        Encoding.ASCII.GetBytes("ntdll.dll\0").CopyTo(bytes, 0x2C8);
        Encoding.ASCII.GetBytes("GetProcAddress\0").CopyTo(bytes, 0x2B2);
        Encoding.ASCII.GetBytes("NtClose\0").CopyTo(bytes, 0x2E2);

        return bytes;

        void WriteUInt16(int offset, ushort value) =>
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)), value);

        void WriteUInt32(int offset, uint value) =>
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)), value);

        void WriteUInt64(int offset, ulong value) =>
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset, sizeof(ulong)), value);
    }

    private sealed class ImportTestBinaryRegistry(params NativeImage[] images) : INativeBinaryRegistry
    {
        private readonly Dictionary<string, NativeImage> _images = images.ToDictionary(image => image.Handle.Value, StringComparer.OrdinalIgnoreCase);

        public NativeResult<NativeImage> Load(string path, string? expectedBuildId = null) =>
            throw new NotSupportedException();

        public bool TryGet(string imageHandle, out NativeImage? image)
        {
            var found = _images.TryGetValue(imageHandle, out var resolved);
            image = resolved;
            return found;
        }

        public IReadOnlyList<NativeImage> List() => [.. _images.Values];
    }
}
