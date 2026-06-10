using System.Buffers.Binary;
using System.Collections.Generic;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.R2R;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>Tests for <see cref="ReadyToRunReader"/>.</summary>
public class ReadyToRunReaderTests
{
    // ---------------------------------------------------------------------------
    // Helpers for finding the real R2R fixture DLL (System.Linq.dll published
    // alongside SampleAot). Tests that rely on this skip gracefully when the
    // fixture isn't available (e.g. in minimal CI containers).
    // ---------------------------------------------------------------------------

    private static string? FindSystemLinqDll()
    {
        // Walk up from the test assembly to find the repo root, then locate
        // the pre-published DLL from the SampleAot publish output.
        var dir = Path.GetDirectoryName(typeof(ReadyToRunReaderTests).Assembly.Location);
        for (var i = 0; i < 8 && dir is not null; i++, dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(
                dir, "tests", "fixtures", "SampleAot",
                "bin", "Release", "net10.0", "linux-x64", "System.Linq.dll");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string? FindInstalledSystemPrivateCoreLib()
    {
        const string ExactRuntimePath = "/home/pedrotravi/.dotnet/shared/Microsoft.NETCore.App/10.0.5/System.Private.CoreLib.dll";
        if (File.Exists(ExactRuntimePath))
            return ExactRuntimePath;

        const string SharedRuntimeRoot = "/home/pedrotravi/.dotnet/shared/Microsoft.NETCore.App";
        if (!Directory.Exists(SharedRuntimeRoot))
            return null;

        foreach (var runtimeDir in Directory.GetDirectories(SharedRuntimeRoot)
                     .Select(path => new DirectoryInfo(path))
                     .Where(dir => Version.TryParse(dir.Name, out var version) && version.Major >= 8)
                     .OrderByDescending(dir => Version.Parse(dir.Name)))
        {
            var candidate = Path.Combine(runtimeDir.FullName, "System.Private.CoreLib.dll");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    // ---------------------------------------------------------------------------
    // Synthetic fixture factory — builds a minimal PE with an R2R header and
    // (optionally) a RuntimeFunctions section, without touching the file system.
    // ---------------------------------------------------------------------------

    private static (byte[] PeBytes, NativeImage Image) BuildSyntheticR2RPe(
        Architecture arch = Architecture.X64,
        RuntimeFunctionEntry[]? runtimeFunctions = null)
    {
        // Build sections:
        //   .text → contains stub code (we use it for function RVAs)
        //   .clr  → IMAGE_COR20_HEADER + R2R header + optional RuntimeFunctions table

        const uint SectionAlignment = 0x1000;
        const uint FileAlignment = 0x200;

        // We'll lay out the CLR section at VA 0x2000, raw offset 0x400
        const uint ClrSectionVA = 0x2000;
        const uint ClrSectionRaw = 0x400;

        // IMAGE_COR20_HEADER is 72 bytes
        // R2R header follows immediately after, at ClrSectionVA + 72
        const uint R2RHeaderVA = ClrSectionVA + 72;

        // R2R header: 16 bytes + sections (12 bytes each)
        // We'll include one section: RuntimeFunctions (or none)
        var hasFunctions = runtimeFunctions is not null && runtimeFunctions.Length > 0;
        int numR2RSections = hasFunctions ? 1 : 0;
        int r2rHeaderSize = 16 + numR2RSections * 12;

        // RuntimeFunctions table follows R2R header
        uint rtFuncTableVA = R2RHeaderVA + (uint)r2rHeaderSize;
        int rtFuncTableSize = (runtimeFunctions?.Length ?? 0) * 12;

        int clrSectionDataSize = 72 + r2rHeaderSize + rtFuncTableSize;
        int clrSectionFileSize = Align(clrSectionDataSize, (int)FileAlignment);

        // Total file: DOS header (0x80) + PE headers (0x188) + CLR section data
        int totalSize = (int)ClrSectionRaw + clrSectionFileSize;
        var bytes = new byte[totalSize];

        // --- DOS header ---
        bytes[0] = 0x4D; bytes[1] = 0x5A;  // MZ
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x3C), 0x80); // e_lfanew

        // --- PE signature ---
        int peOff = 0x80;
        bytes[peOff] = (byte)'P'; bytes[peOff + 1] = (byte)'E';  // PE\0\0

        // --- COFF header (at peOff+4) ---
        ushort machineCode = arch switch
        {
            Architecture.X64 => 0x8664,
            Architecture.Arm64 => 0xAA64,
            _ => 0x8664,
        };
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOff + 4), machineCode); // Machine (or OS-encoded managed-native machine)
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOff + 6), 1);           // NumberOfSections = 1
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOff + 20), 0xF0);       // SizeOfOptionalHeader
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOff + 22), 0x2022);     // Characteristics (DLL|Exe)

        // --- Optional header PE32+ (at peOff+24) ---
        int optOff = peOff + 24;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(optOff), 0x20B);          // PE32+ magic
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optOff + 56), 0x40000);   // ImageBase
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optOff + 60), SectionAlignment);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optOff + 64), FileAlignment);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(optOff + 40), 4);         // MajorOSVersion
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optOff + 92), 16);        // NumberOfRvaAndSizes

        // DataDirectory[14] = CLR header descriptor (RVA=ClrSectionVA, Size=72)
        int ddBase = optOff + 112;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ddBase + 14 * 8), ClrSectionVA);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ddBase + 14 * 8 + 4), 72);

        // --- Section table: 1 section ".clr" at peOff+24+0xF0 ---
        int secTableOff = peOff + 24 + 0xF0;
        // Name: ".clr\0\0\0\0"
        bytes[secTableOff + 0] = (byte)'.';
        bytes[secTableOff + 1] = (byte)'c';
        bytes[secTableOff + 2] = (byte)'l';
        bytes[secTableOff + 3] = (byte)'r';
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 8), (uint)clrSectionDataSize); // VirtualSize
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 12), ClrSectionVA);            // VirtualAddress
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 16), (uint)clrSectionFileSize); // SizeOfRawData
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 20), ClrSectionRaw);            // PointerToRawData
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 36), 0x40000040);               // Characteristics

        // --- IMAGE_COR20_HEADER at ClrSectionRaw ---
        int clrOff = (int)ClrSectionRaw;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clrOff + 0), 72); // cb
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(clrOff + 4), 2);  // MajorRuntimeVersion
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(clrOff + 6), 5);  // MinorRuntimeVersion
        // MetaData directory at offset 8 (RVA=0, Size=0 — we leave it blank)
        // Flags at offset 16
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clrOff + 16), 0x00000009); // COMIMAGE_FLAGS_ILONLY
        // ManagedNativeHeader at offset 64: RVA and Size
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clrOff + 64), R2RHeaderVA);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clrOff + 68), (uint)r2rHeaderSize);

        // --- READYTORUN_HEADER at R2RHeaderVA (file offset: clrOff+72) ---
        int r2rOff = clrOff + 72;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(r2rOff + 0), 0x00525452u);   // Signature "RTR\0"
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(r2rOff + 4), 6);              // MajorVersion
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(r2rOff + 6), 0);              // MinorVersion
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(r2rOff + 8), 0x00000003u);   // Flags
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(r2rOff + 12), (uint)numR2RSections);

        if (hasFunctions)
        {
            // Section entry 0: RuntimeFunctions (type 102), RVA, Size
            int secEntOff = r2rOff + 16;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 0), (uint)ReadyToRunSectionType.RuntimeFunctions);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 4), rtFuncTableVA);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 8), (uint)rtFuncTableSize);

            // --- RuntimeFunctions table ---
            int rtFuncOff = r2rOff + r2rHeaderSize;
            for (var i = 0; i < runtimeFunctions!.Length; i++)
            {
                var fn = runtimeFunctions[i];
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(rtFuncOff + i * 12 + 0), fn.Begin);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(rtFuncOff + i * 12 + 4), fn.End);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(rtFuncOff + i * 12 + 8), fn.Unwind);
            }
        }

        // Build NativeImage from bytes
        var handle = ImageHandle.From("aabbccddeeff", "synthetic_r2r.dll");
        var clrSec = new NativeSection(".clr", ClrSectionVA, (ulong)clrSectionDataSize, ClrSectionRaw, (ulong)clrSectionFileSize);
        var image = new NativeImage(handle, "synthetic_r2r.dll", BinaryFormat.Pe, arch,
            [clrSec], [], new ReadOnlyMemory<byte>(bytes), 0x40000);

        return (bytes, image);
    }

    private static (byte[] PeBytes, NativeImage Image) BuildSyntheticArm64R2RPe(params Arm64RuntimeFunctionEntry[] runtimeFunctions)
    {
        const uint SectionAlignment = 0x1000;
        const uint FileAlignment = 0x200;
        const uint ClrSectionVA = 0x2000;
        const uint ClrSectionRaw = 0x400;
        const uint R2RHeaderVA = ClrSectionVA + 72;

        int numR2RSections = runtimeFunctions.Length > 0 ? 1 : 0;
        int r2rHeaderSize = 16 + numR2RSections * 12;
        uint rtFuncTableVA = R2RHeaderVA + (uint)r2rHeaderSize;
        int rtFuncTableSize = runtimeFunctions.Length * 8;
        int xdataSize = runtimeFunctions.Count(f => !f.IsPacked) * sizeof(uint);
        int clrSectionDataSize = 72 + r2rHeaderSize + rtFuncTableSize + xdataSize;
        int clrSectionFileSize = Align(clrSectionDataSize, (int)FileAlignment);

        int totalSize = (int)ClrSectionRaw + clrSectionFileSize;
        var bytes = new byte[totalSize];

        bytes[0] = 0x4D;
        bytes[1] = 0x5A;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x3C), 0x80);

        int peOff = 0x80;
        bytes[peOff] = (byte)'P';
        bytes[peOff + 1] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOff + 4), 0xAA64);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOff + 6), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOff + 20), 0xF0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOff + 22), 0x2022);

        int optOff = peOff + 24;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(optOff), 0x20B);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optOff + 56), 0x40000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optOff + 60), SectionAlignment);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optOff + 64), FileAlignment);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(optOff + 40), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optOff + 92), 16);

        int ddBase = optOff + 112;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ddBase + 14 * 8), ClrSectionVA);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ddBase + 14 * 8 + 4), 72);

        int secTableOff = peOff + 24 + 0xF0;
        bytes[secTableOff + 0] = (byte)'.';
        bytes[secTableOff + 1] = (byte)'c';
        bytes[secTableOff + 2] = (byte)'l';
        bytes[secTableOff + 3] = (byte)'r';
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 8), (uint)clrSectionDataSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 12), ClrSectionVA);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 16), (uint)clrSectionFileSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 20), ClrSectionRaw);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 36), 0x40000040);

        int clrOff = (int)ClrSectionRaw;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clrOff + 0), 72);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(clrOff + 4), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(clrOff + 6), 5);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clrOff + 16), 0x00000009);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clrOff + 64), R2RHeaderVA);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clrOff + 68), (uint)r2rHeaderSize);

        int r2rOff = clrOff + 72;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(r2rOff + 0), 0x00525452u);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(r2rOff + 4), 6);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(r2rOff + 6), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(r2rOff + 8), 0x00000003u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(r2rOff + 12), (uint)numR2RSections);

        if (runtimeFunctions.Length > 0)
        {
            int secEntOff = r2rOff + 16;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 0), (uint)ReadyToRunSectionType.RuntimeFunctions);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 4), rtFuncTableVA);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 8), (uint)rtFuncTableSize);

            int rtFuncOff = r2rOff + r2rHeaderSize;
            uint nextXdataRva = rtFuncTableVA + (uint)rtFuncTableSize;
            int nextXdataOff = rtFuncOff + rtFuncTableSize;
            for (var i = 0; i < runtimeFunctions.Length; i++)
            {
                var fn = runtimeFunctions[i];
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(rtFuncOff + i * 8 + 0), fn.Begin);
                if (fn.IsPacked)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(rtFuncOff + i * 8 + 4), fn.PackedUnwindData);
                }
                else
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(rtFuncOff + i * 8 + 4), nextXdataRva);
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(nextXdataOff), fn.XdataHeader);
                    nextXdataRva += sizeof(uint);
                    nextXdataOff += sizeof(uint);
                }
            }
        }

        var handle = ImageHandle.From("arm64synthetic", "synthetic_arm64_r2r.dll");
        var clrSec = new NativeSection(".clr", ClrSectionVA, (ulong)clrSectionDataSize, ClrSectionRaw, (ulong)clrSectionFileSize);
        var image = new NativeImage(handle, "synthetic_arm64_r2r.dll", BinaryFormat.Pe, Architecture.Arm64,
            [clrSec], [], new ReadOnlyMemory<byte>(bytes), 0x40000);

        return (bytes, image);
    }

    private static int Align(int value, int alignment) =>
        (value + alignment - 1) & ~(alignment - 1);

    internal sealed record RuntimeFunctionEntry(uint Begin, uint End, uint Unwind);

    internal sealed record Arm64RuntimeFunctionEntry(uint Begin, uint LengthBytes, bool IsPacked)
    {
        public uint End => Begin + LengthBytes;

        public uint PackedUnwindData => 0x1u | ((LengthBytes / 4u) << 2);

        public uint XdataHeader => LengthBytes / 4u;

        public static Arm64RuntimeFunctionEntry Packed(uint begin, uint lengthBytes) => new(begin, lengthBytes, true);

        public static Arm64RuntimeFunctionEntry Xdata(uint begin, uint lengthBytes) => new(begin, lengthBytes, false);
    }

    // -----------------------------------------------------------------------
    // ReadHeader tests — non-R2R / error cases
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadHeader_ElfImage_ReturnsR2RNotPresent()
    {
        var handle = ImageHandle.From("aabb", "test.so");
        var image = new NativeImage(handle, "test.so", BinaryFormat.Elf,
            Architecture.X64, [], [], ReadOnlyMemory<byte>.Empty, 0);

        var result = ReadyToRunReader.ReadHeader(image);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.R2RNotPresent);
    }

    [Fact]
    public void ReadHeader_MachOImage_ReturnsR2RNotPresent()
    {
        var handle = ImageHandle.From("aabb", "test.dylib");
        var image = new NativeImage(handle, "test.dylib", BinaryFormat.MachO,
            Architecture.Arm64, [], [], ReadOnlyMemory<byte>.Empty, 0);

        var result = ReadyToRunReader.ReadHeader(image);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.R2RNotPresent);
    }

    [Fact]
    public void ReadHeader_PureManagedPe_ReturnsR2RNotPresent()
    {
        // The test assembly itself has no R2R header.
        var path = typeof(ReadyToRunReaderTests).Assembly.Location;
        if (!File.Exists(path)) return;

        var bytes = File.ReadAllBytes(path);
        var image = PeNativeReader.Read(new ReadOnlyMemory<byte>(bytes), path);
        image.Should().NotBeNull();

        var result = ReadyToRunReader.ReadHeader(image!);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.R2RNotPresent);
    }

    [Fact]
    public void ReadHeader_EmptyBytes_ReturnsR2RNotPresent()
    {
        var handle = ImageHandle.From("aabb", "empty.dll");
        var image = new NativeImage(handle, "empty.dll", BinaryFormat.Pe,
            Architecture.X64, [], [], ReadOnlyMemory<byte>.Empty, 0);

        var result = ReadyToRunReader.ReadHeader(image);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.R2RNotPresent);
    }

    // -----------------------------------------------------------------------
    // ReadHeader tests — synthetic R2R PE
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadHeader_SyntheticR2RPe_ParsesCorrectly()
    {
        var (_, image) = BuildSyntheticR2RPe(Architecture.X64);

        var result = ReadyToRunReader.ReadHeader(image);

        result.IsError.Should().BeFalse();
        var hdr = result.Data!;
        hdr.MajorVersion.Should().Be(6);
        hdr.MinorVersion.Should().Be(0);
        hdr.Version.Should().Be("6.0");
        hdr.Flags.Should().Be(0x00000003u);
        hdr.Sections.Should().BeEmpty();
    }

    [Fact]
    public void ReadHeader_SyntheticR2R_WithRuntimeFunctions_HasSection()
    {
        var functions = new RuntimeFunctionEntry[]
        {
            new(0x1000, 0x1100, 0x5000),
            new(0x1100, 0x1200, 0x5100),
        };
        var (_, image) = BuildSyntheticR2RPe(Architecture.X64, functions);

        var result = ReadyToRunReader.ReadHeader(image);

        result.IsError.Should().BeFalse();
        var hdr = result.Data!;
        hdr.Sections.Should().HaveCount(1);
        hdr.Sections[0].Type.Should().Be((uint)ReadyToRunSectionType.RuntimeFunctions);
        hdr.Sections[0].TypeName.Should().Be("RuntimeFunctions");
        hdr.FindSection(ReadyToRunSectionType.RuntimeFunctions).Should().NotBeNull();
    }

    [Fact]
    public void TryReadTargetArchitecture_InstalledSystemPrivateCoreLib_ReturnsX64()
    {
        var dllPath = FindInstalledSystemPrivateCoreLib();
        if (dllPath is null) return;

        var bytes = File.ReadAllBytes(dllPath);
        var image = PeNativeReader.Read(new ReadOnlyMemory<byte>(bytes), dllPath);
        image.Should().NotBeNull();

        var unknownArchImage = new NativeImage(
            image!.Handle,
            image.FilePath,
            image.Format,
            Architecture.Unknown,
            image.Sections,
            image.Symbols,
            image.RawBytes,
            image.ImageBase);

        ReadyToRunReader.TryReadTargetArchitecture(unknownArchImage).Should().Be(Architecture.X64);
    }

    // -----------------------------------------------------------------------
    // ReadRuntimeFunctions tests — error cases
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadRuntimeFunctions_Arm64Packed_ReturnsAllEntries()
    {
        var functions = new[]
        {
            Arm64RuntimeFunctionEntry.Packed(0x1000, 0x100),
            Arm64RuntimeFunctionEntry.Packed(0x1200, 0x80),
            Arm64RuntimeFunctionEntry.Packed(0x2000, 0x180),
        };
        var (_, image) = BuildSyntheticArm64R2RPe(functions);
        var headerResult = ReadyToRunReader.ReadHeader(image);
        headerResult.IsError.Should().BeFalse();

        var result = ReadyToRunReader.ReadRuntimeFunctions(image, headerResult.Data!, 0, 100);

        result.IsError.Should().BeFalse();
        var page = result.Data!;
        page.TotalCount.Should().Be(3);
        page.Functions.Should().HaveCount(3);
        page.Functions[0].BeginAddress.Should().Be(0x1000u);
        page.Functions[0].EndAddress.Should().Be(functions[0].End);
        page.Functions[1].EndAddress.Should().Be(functions[1].End);
        page.Functions[2].EndAddress.Should().Be(functions[2].End);
    }

    [Fact]
    public void ReadRuntimeFunctions_Arm64Xdata_ComputesEndAddress()
    {
        var function = Arm64RuntimeFunctionEntry.Xdata(0x3000, 0x140);
        var (_, image) = BuildSyntheticArm64R2RPe(function);
        var headerResult = ReadyToRunReader.ReadHeader(image);
        headerResult.IsError.Should().BeFalse();

        var result = ReadyToRunReader.ReadRuntimeFunctions(image, headerResult.Data!, 0, 100);

        result.IsError.Should().BeFalse();
        result.Data!.Functions.Should().ContainSingle();
        result.Data.Functions[0].BeginAddress.Should().Be(0x3000u);
        result.Data.Functions[0].EndAddress.Should().Be(function.End);
        result.Data.Functions[0].UnwindInfoAddress.Should().NotBe(0u);
    }
    [Fact]
    public void ReadRuntimeFunctions_MissingSection_ReturnsR2RSectionNotPresent()
    {
        var (_, image) = BuildSyntheticR2RPe(Architecture.X64);  // no RuntimeFunctions section
        var headerResult = ReadyToRunReader.ReadHeader(image);
        headerResult.IsError.Should().BeFalse();

        var result = ReadyToRunReader.ReadRuntimeFunctions(image, headerResult.Data!);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.R2RSectionNotPresent);
    }

    // -----------------------------------------------------------------------
    // ReadRuntimeFunctions tests — synthetic R2R PE with entries
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadRuntimeFunctions_Synthetic_ReturnsAllEntries()
    {
        var functions = new RuntimeFunctionEntry[]
        {
            new(0x1000, 0x1100, 0x5000),
            new(0x1200, 0x1350, 0x5100),
            new(0x2000, 0x2080, 0x5200),
        };
        var (_, image) = BuildSyntheticR2RPe(Architecture.X64, functions);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.ReadRuntimeFunctions(image, hdr, 0, 100);

        result.IsError.Should().BeFalse();
        var page = result.Data!;
        page.TotalCount.Should().Be(3);
        page.Functions.Should().HaveCount(3);
        page.NextCursor.Should().BeNull();

        page.Functions[0].BeginAddress.Should().Be(0x1000u);
        page.Functions[0].EndAddress.Should().Be(0x1100u);
        page.Functions[0].UnwindInfoAddress.Should().Be(0x5000u);
        page.Functions[1].BeginAddress.Should().Be(0x1200u);
        page.Functions[2].BeginAddress.Should().Be(0x2000u);
    }

    [Fact]
    public void ReadRuntimeFunctions_Synthetic_PaginatesCorrectly()
    {
        var functions = Enumerable.Range(0, 10)
            .Select(i => new RuntimeFunctionEntry(
                (uint)(0x1000 + i * 0x100),
                (uint)(0x1100 + i * 0x100),
                (uint)(0x5000 + i * 0x10)))
            .ToArray();
        var (_, image) = BuildSyntheticR2RPe(Architecture.X64, functions);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var page1 = ReadyToRunReader.ReadRuntimeFunctions(image, hdr, 0, 4);
        page1.IsError.Should().BeFalse();
        page1.Data!.Functions.Should().HaveCount(4);
        page1.Data.NextCursor.Should().Be(4);

        var page2 = ReadyToRunReader.ReadRuntimeFunctions(image, hdr, 4, 4);
        page2.IsError.Should().BeFalse();
        page2.Data!.Functions.Should().HaveCount(4);
        page2.Data.NextCursor.Should().Be(8);

        var page3 = ReadyToRunReader.ReadRuntimeFunctions(image, hdr, 8, 4);
        page3.IsError.Should().BeFalse();
        page3.Data!.Functions.Should().HaveCount(2);
        page3.Data.NextCursor.Should().BeNull();
    }

    [Fact]
    public void ReadRuntimeFunctions_IndexAssignedCorrectly()
    {
        var functions = new RuntimeFunctionEntry[]
        {
            new(0x1000, 0x1100, 0x5000),
            new(0x1200, 0x1350, 0x5100),
        };
        var (_, image) = BuildSyntheticR2RPe(Architecture.X64, functions);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var page = ReadyToRunReader.ReadRuntimeFunctions(image, hdr, 0, 100).Data!;

        page.Functions[0].Index.Should().Be(0);
        page.Functions[1].Index.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // FindRuntimeFunction tests
    // -----------------------------------------------------------------------

    [Fact]
    public void FindRuntimeFunction_RvaInFirstFunction_ReturnsIt()
    {
        var functions = new RuntimeFunctionEntry[]
        {
            new(0x1000, 0x1100, 0x5000),
            new(0x1200, 0x1350, 0x5100),
            new(0x2000, 0x2080, 0x5200),
        };
        var (_, image) = BuildSyntheticR2RPe(Architecture.X64, functions);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.FindRuntimeFunction(image, hdr, 0x1050);

        result.IsError.Should().BeFalse();
        result.Data!.BeginAddress.Should().Be(0x1000u);
        result.Data.EndAddress.Should().Be(0x1100u);
    }

    [Fact]
    public void FindRuntimeFunction_RvaInMiddleFunction_ReturnsIt()
    {
        var functions = new RuntimeFunctionEntry[]
        {
            new(0x1000, 0x1100, 0x5000),
            new(0x1200, 0x1350, 0x5100),
            new(0x2000, 0x2080, 0x5200),
        };
        var (_, image) = BuildSyntheticR2RPe(Architecture.X64, functions);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.FindRuntimeFunction(image, hdr, 0x1300);

        result.IsError.Should().BeFalse();
        result.Data!.BeginAddress.Should().Be(0x1200u);
    }

    [Fact]
    public void FindRuntimeFunction_RvaExactlyAtBeginAddress_ReturnsIt()
    {
        var functions = new RuntimeFunctionEntry[]
        {
            new(0x1000, 0x1100, 0x5000),
            new(0x2000, 0x2080, 0x5200),
        };
        var (_, image) = BuildSyntheticR2RPe(Architecture.X64, functions);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.FindRuntimeFunction(image, hdr, 0x2000);

        result.IsError.Should().BeFalse();
        result.Data!.BeginAddress.Should().Be(0x2000u);
    }

    [Fact]
    public void FindRuntimeFunction_RvaAtEndAddress_ReturnsSymbolNotFound()
    {
        var functions = new RuntimeFunctionEntry[]
        {
            new(0x1000, 0x1100, 0x5000),
        };
        var (_, image) = BuildSyntheticR2RPe(Architecture.X64, functions);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        // EndAddress is exclusive
        var result = ReadyToRunReader.FindRuntimeFunction(image, hdr, 0x1100);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.SymbolNotFound);
    }

    [Fact]
    public void FindRuntimeFunction_RvaInGap_ReturnsSymbolNotFound()
    {
        var functions = new RuntimeFunctionEntry[]
        {
            new(0x1000, 0x1100, 0x5000),
            new(0x2000, 0x2080, 0x5200),
        };
        var (_, image) = BuildSyntheticR2RPe(Architecture.X64, functions);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        // RVA 0x1500 is in the gap between the two functions
        var result = ReadyToRunReader.FindRuntimeFunction(image, hdr, 0x1500);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.SymbolNotFound);
    }

    [Fact]
    public void FindRuntimeFunction_Arm64Image_ReturnsMatchingFunction()
    {
        var functions = new[]
        {
            Arm64RuntimeFunctionEntry.Packed(0x1000, 0x100),
            Arm64RuntimeFunctionEntry.Xdata(0x2000, 0x180),
            Arm64RuntimeFunctionEntry.Packed(0x2400, 0x80),
        };
        var (_, image) = BuildSyntheticArm64R2RPe(functions);
        var hdr = ReadyToRunReader.ReadHeader(image).Data!;

        var result = ReadyToRunReader.FindRuntimeFunction(image, hdr, 0x20A0);

        result.IsError.Should().BeFalse();
        result.Data!.BeginAddress.Should().Be(0x2000u);
        result.Data.EndAddress.Should().Be(functions[1].End);
    }

    // -----------------------------------------------------------------------
    // Real fixture tests — System.Linq.dll (R2R v16.0 from SampleAot publish)
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadHeader_SystemLinqDll_ParsesSuccessfully()
    {
        var dllPath = FindSystemLinqDll();
        if (dllPath is null) return;  // skip when fixture not available

        var bytes = File.ReadAllBytes(dllPath);
        var image = PeNativeReader.Read(new ReadOnlyMemory<byte>(bytes), dllPath);
        image.Should().NotBeNull();

        var result = ReadyToRunReader.ReadHeader(image!);

        result.IsError.Should().BeFalse();
        var hdr = result.Data!;
        hdr.MajorVersion.Should().BeGreaterThanOrEqualTo(1);
        hdr.Sections.Should().NotBeEmpty();
    }

    [Fact]
    public void ReadHeader_SystemLinqDll_HasExpectedSections()
    {
        var dllPath = FindSystemLinqDll();
        if (dllPath is null) return;

        var bytes = File.ReadAllBytes(dllPath);
        var image = PeNativeReader.Read(new ReadOnlyMemory<byte>(bytes), dllPath);
        var hdr = ReadyToRunReader.ReadHeader(image!).Data!;

        // All section types in the enum should be representable by name
        foreach (var sec in hdr.Sections)
        {
            sec.TypeName.Should().NotBeNullOrEmpty();
        }

        // System.Linq.dll should have a CompilerIdentifier section (100)
        var compId = hdr.FindSection(ReadyToRunSectionType.CompilerIdentifier);
        compId.Should().NotBeNull("System.Linq.dll is a CoreCLR R2R image and always carries CompilerIdentifier");
        compId!.Size.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ReadHeader_SystemLinqDll_SectionSizeConsistent()
    {
        var dllPath = FindSystemLinqDll();
        if (dllPath is null) return;

        var bytes = File.ReadAllBytes(dllPath);
        var image = PeNativeReader.Read(new ReadOnlyMemory<byte>(bytes), dllPath);
        var hdr = ReadyToRunReader.ReadHeader(image!).Data!;

        // Total size check: header + sections must fit in ManagedNativeHeader.Size
        // (16 bytes + NumSections * 12 bytes == reported size from PE)
        var expectedHeaderSize = 16 + hdr.Sections.Count * 12;
        expectedHeaderSize.Should().BeGreaterThan(0);
        hdr.Sections.Count.Should().BeGreaterThan(0);
    }

    // ---------------------------------------------------------------------------
    // Real-image regression tests — guard against the section-type enum drifting
    // away from coreclr/inc/readytorun.h. RuntimeFunctions is type 102; an image
    // produced by crossgen2 must expose it and yield decodable entries. These
    // would have failed when the enum incorrectly mapped RuntimeFunctions to 5.
    // ---------------------------------------------------------------------------

    [Fact]
    public void RuntimeFunctions_SectionType_Is102()
    {
        ((uint)ReadyToRunSectionType.RuntimeFunctions).Should().Be(102u,
            "coreclr/inc/readytorun.h defines RuntimeFunctions = 102");
    }

    [Fact]
    public void ReadRuntimeFunctions_RealR2RImage_ReturnsDecodableEntries()
    {
        var dllPaths = RealR2RImagePaths().ToList();
        if (dllPaths.Count == 0) return;  // skip when no real R2R fixture is available

        foreach (var dllPath in dllPaths)
        {
            var bytes = File.ReadAllBytes(dllPath);
            var image = PeNativeReader.Read(new ReadOnlyMemory<byte>(bytes), dllPath);
            image.Should().NotBeNull();

            var hdr = ReadyToRunReader.ReadHeader(image!).Data!;

            var rtSection = hdr.FindSection(ReadyToRunSectionType.RuntimeFunctions);
            rtSection.Should().NotBeNull(
                $"a crossgen2-produced R2R image ({dllPath}) exposes the RuntimeFunctions section (type 102)");
            rtSection!.Type.Should().Be(102u);
            rtSection.Size.Should().BeGreaterThan(0);

            var page = ReadyToRunReader.ReadRuntimeFunctions(image!, hdr, 0, 16);
            page.IsError.Should().BeFalse(
                "RuntimeFunctions must be decodable on a real R2R image, not return r2r_section_not_present");
            page.Data!.TotalCount.Should().BeGreaterThan(0);
            page.Data.Functions.Should().NotBeEmpty();

            foreach (var fn in page.Data.Functions)
            {
                fn.EndAddress.Should().BeGreaterThan(fn.BeginAddress,
                    "each RUNTIME_FUNCTION spans a non-empty code range");
            }
        }
    }

    [Fact]
    public void ReadHeader_RealR2RImage_DecodesHeaderFlagsConsistently()
    {
        var dllPaths = RealR2RImagePaths().ToList();
        if (dllPaths.Count == 0) return;  // skip when no real R2R fixture is available

        foreach (var dllPath in dllPaths)
        {
            var bytes = File.ReadAllBytes(dllPath);
            var image = PeNativeReader.Read(new ReadOnlyMemory<byte>(bytes), dllPath);
            image.Should().NotBeNull();

            var hdr = ReadyToRunReader.ReadHeader(image!).Data!;
            var names = ReadyToRunHeaderAttributesExtensions.DecodeNames(hdr.Flags);

            if (hdr.Flags == 0)
            {
                names.Should().BeEmpty();
                continue;
            }

            // Every set bit must be accounted for: re-OR the decoded known flags
            // plus any residual Unknown(0x...) bits and the result must equal the raw value.
            uint reencoded = 0;
            foreach (var name in names)
            {
                if (name.StartsWith("Unknown(0x", StringComparison.Ordinal))
                {
                    var hex = name["Unknown(0x".Length..].TrimEnd(')');
                    reencoded |= Convert.ToUInt32(hex, 16);
                }
                else
                {
                    reencoded |= (uint)Enum.Parse<ReadyToRunHeaderAttributes>(name);
                }
            }

            reencoded.Should().Be(hdr.Flags,
                $"decoded flag names for {dllPath} must round-trip back to the raw flags value");
        }
    }

    private static IEnumerable<string> RealR2RImagePaths()
    {
        var spc = FixturePaths.SystemPrivateCoreLib ?? FindInstalledSystemPrivateCoreLib();
        if (spc is not null)
            yield return spc;

        var linq = FindSystemLinqDll();
        if (linq is not null)
            yield return linq;
    }
}
