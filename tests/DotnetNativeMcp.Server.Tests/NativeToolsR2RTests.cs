using System.Buffers.Binary;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.R2R;
using DotnetNativeMcp.Core.Symbols;
using DotnetNativeMcp.Core.Xref;
using DotnetNativeMcp.Server.Tools;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Server.Tests;

public class NativeToolsR2RTests
{
    // -----------------------------------------------------------------------
    // Synthetic PE factory (duplicates test-side construction to keep tests
    // self-contained; real R2R parsing is unit-tested in Core.Tests).
    // -----------------------------------------------------------------------

    private static NativeImage BuildSyntheticR2RImage(
        Architecture arch = Architecture.X64,
        bool includeRuntimeFunctions = true,
        int functionCount = 3)
    {
        const uint ClrSectionVA = 0x2000;
        const uint ClrSectionRaw = 0x400;
        const uint R2RHeaderVA = ClrSectionVA + 72;
        const uint FileAlignment = 0x200;

        int numR2RSections = includeRuntimeFunctions ? 1 : 0;
        int r2rHeaderSize = 16 + numR2RSections * 12;

        uint rtFuncTableVA = R2RHeaderVA + (uint)r2rHeaderSize;
        int rtFuncTableSize = functionCount * 12;
        int clrSectionDataSize = 72 + r2rHeaderSize + (includeRuntimeFunctions ? rtFuncTableSize : 0);
        int clrSectionFileSize = Align(clrSectionDataSize, (int)FileAlignment);

        int totalSize = (int)ClrSectionRaw + clrSectionFileSize;
        var bytes = new byte[totalSize];

        // DOS header
        bytes[0] = 0x4D; bytes[1] = 0x5A;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x3C), 0x80);

        int peOff = 0x80;
        bytes[peOff] = (byte)'P'; bytes[peOff + 1] = (byte)'E';
        ushort machineCode = arch == Architecture.Arm64 ? (ushort)0xAA64 : (ushort)0x8664;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOff + 4), machineCode);
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

        if (includeRuntimeFunctions)
        {
            int secEntOff = r2rOff + 16;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 0), 5u);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 4), rtFuncTableVA);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 8), (uint)rtFuncTableSize);

            int rtFuncOff = r2rOff + r2rHeaderSize;
            for (var i = 0; i < functionCount; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(rtFuncOff + i * 12 + 0), (uint)(0x1000 + i * 0x100));
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(rtFuncOff + i * 12 + 4), (uint)(0x1100 + i * 0x100));
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(rtFuncOff + i * 12 + 8), (uint)(0x5000 + i * 0x10));
            }
        }

        var handle = ImageHandle.From("aabbccdd", "r2r.dll");
        var clrSec = new NativeSection(".clr", ClrSectionVA, (ulong)clrSectionDataSize, ClrSectionRaw, (ulong)clrSectionFileSize);
        return new NativeImage(handle, "r2r.dll", BinaryFormat.Pe, arch,
            [clrSec], [], new ReadOnlyMemory<byte>(bytes), 0x40000);
    }

    private static int Align(int value, int alignment) =>
        (value + alignment - 1) & ~(alignment - 1);

    private static NativeTools MakeTools(params NativeImage[] images) =>
        new NativeTools(new TestBinaryRegistry(images), new NativeCallGraphCache(), new SourceResolver());

    // -----------------------------------------------------------------------
    // GetR2RHeader tests
    // -----------------------------------------------------------------------

    [Fact]
    public void GetR2RHeader_UnknownHandle_ReturnsBinaryNotFound()
    {
        var tools = MakeTools();
        var result = tools.GetR2RHeader("no-such-handle");
        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.BinaryNotFound);
    }

    [Fact]
    public void GetR2RHeader_ElfImage_ReturnsR2RNotPresent()
    {
        var handle = ImageHandle.From("aabb", "test.so");
        var image = new NativeImage(handle, "test.so", BinaryFormat.Elf,
            Architecture.X64, [], [], ReadOnlyMemory<byte>.Empty, 0);
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.R2RNotPresent);
    }

    [Fact]
    public void GetR2RHeader_SyntheticR2R_ReturnsHeader()
    {
        var image = BuildSyntheticR2RImage(includeRuntimeFunctions: true, functionCount: 2);
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value);

        result.IsError.Should().BeFalse();
        result.Data!.Version.Should().Be("6.0");
        result.Data.SectionCount.Should().Be(1);
        result.Data.HasRuntimeFunctions.Should().BeTrue();
        result.Data.Sections.Should().HaveCount(1);
        result.Data.Sections[0].TypeName.Should().Be("RuntimeFunctions");
    }

    [Fact]
    public void GetR2RHeader_SyntheticR2R_NoRuntimeFunctions_HasRuntimeFunctionsFalse()
    {
        var image = BuildSyntheticR2RImage(includeRuntimeFunctions: false);
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value);

        result.IsError.Should().BeFalse();
        result.Data!.HasRuntimeFunctions.Should().BeFalse();
        result.Data.SectionCount.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // ListR2RRuntimeFunctions tests
    // -----------------------------------------------------------------------

    [Fact]
    public void ListR2RRuntimeFunctions_UnknownHandle_ReturnsBinaryNotFound()
    {
        var tools = MakeTools();
        var result = tools.ListR2RRuntimeFunctions("no-such-handle");
        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.BinaryNotFound);
    }

    [Fact]
    public void ListR2RRuntimeFunctions_SyntheticR2R_ReturnsPaginatedEntries()
    {
        var image = BuildSyntheticR2RImage(functionCount: 5);
        var tools = MakeTools(image);

        var result = tools.ListR2RRuntimeFunctions(image.Handle.Value, pageSize: 3);

        result.IsError.Should().BeFalse();
        result.Data!.Functions.Should().HaveCount(3);
        result.Data.TotalCount.Should().Be(5);
        result.Data.NextCursor.Should().Be(3);
        result.Data.IsLookup.Should().BeFalse();
    }

    [Fact]
    public void ListR2RRuntimeFunctions_RvaLookup_ReturnsMatchingFunction()
    {
        var image = BuildSyntheticR2RImage(functionCount: 3);
        var tools = MakeTools(image);

        // Function 0 covers [0x1000, 0x1100)
        var result = tools.ListR2RRuntimeFunctions(image.Handle.Value, rva: "1050");

        result.IsError.Should().BeFalse();
        result.Data!.IsLookup.Should().BeTrue();
        result.Data.Functions.Should().HaveCount(1);
        result.Data.Functions[0].BeginAddress.Should().Be("0x00001000");
    }

    [Fact]
    public void ListR2RRuntimeFunctions_RvaLookupWithPrefix_ReturnsMatchingFunction()
    {
        var image = BuildSyntheticR2RImage(functionCount: 3);
        var tools = MakeTools(image);

        var result = tools.ListR2RRuntimeFunctions(image.Handle.Value, rva: "0x1050");

        result.IsError.Should().BeFalse();
        result.Data!.Functions[0].BeginAddress.Should().Be("0x00001000");
    }

    [Fact]
    public void ListR2RRuntimeFunctions_InvalidRva_ReturnsInvalidArgument()
    {
        var image = BuildSyntheticR2RImage(functionCount: 1);
        var tools = MakeTools(image);

        var result = tools.ListR2RRuntimeFunctions(image.Handle.Value, rva: "not-a-hex");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void ListR2RRuntimeFunctions_NoRuntimeFunctionsSection_ReturnsR2RSectionNotPresent()
    {
        var image = BuildSyntheticR2RImage(includeRuntimeFunctions: false);
        var tools = MakeTools(image);

        var result = tools.ListR2RRuntimeFunctions(image.Handle.Value);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.R2RSectionNotPresent);
    }

    [Fact]
    public void ListR2RRuntimeFunctions_Arm64_ReturnsR2RArchUnsupported()
    {
        var image = BuildSyntheticR2RImage(Architecture.Arm64, includeRuntimeFunctions: true);
        var tools = MakeTools(image);

        var result = tools.ListR2RRuntimeFunctions(image.Handle.Value);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.R2RArchUnsupported);
    }

    // -----------------------------------------------------------------------
    // TestBinaryRegistry helper (same pattern as other Server tests)
    // -----------------------------------------------------------------------

    private sealed class TestBinaryRegistry(params NativeImage[] images) : INativeBinaryRegistry
    {
        private readonly Dictionary<string, NativeImage> _images =
            images.ToDictionary(i => i.Handle.Value, StringComparer.OrdinalIgnoreCase);

        public NativeResult<NativeImage> Load(string path, string? expectedBuildId = null) =>
            throw new NotSupportedException();

        public void RegisterHint(string path, string? buildId = null) { }

        public bool TryGet(string imageHandle, out NativeImage? image)
        {
            var found = _images.TryGetValue(imageHandle, out var resolved);
            image = resolved;
            return found;
        }

        public IReadOnlyList<NativeImage> List() => [.. _images.Values];
    }
}
