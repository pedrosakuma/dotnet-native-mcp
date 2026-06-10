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
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 0), (uint)ReadyToRunSectionType.RuntimeFunctions);
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

    // Synthetic x64 R2R PE carrying a single ImportSections (type 101) entry.
    private static NativeImage BuildSyntheticR2RWithImportSection()
    {
        const uint FileAlignment = 0x200;
        const uint ClrSectionVA = 0x2000;
        const uint ClrSectionRaw = 0x400;
        const uint R2RHeaderVA = ClrSectionVA + 72;
        const int ImportEntrySize = 20;

        int r2rHeaderSize = 16 + 12;  // header + 1 section entry
        uint importTableVA = R2RHeaderVA + (uint)r2rHeaderSize;
        int importTableSize = ImportEntrySize;  // one entry

        int clrSectionDataSize = 72 + r2rHeaderSize + importTableSize;
        int clrSectionFileSize = Align(clrSectionDataSize, (int)FileAlignment);
        int totalSize = (int)ClrSectionRaw + clrSectionFileSize;
        var bytes = new byte[totalSize];

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
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(r2rOff + 12), 1u);

        int secEntOff = r2rOff + 16;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 0), (uint)ReadyToRunSectionType.ImportSections);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 4), importTableVA);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 8), (uint)importTableSize);

        int tableOff = r2rOff + r2rHeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(tableOff + 0), 0x4000u);   // SectionRva
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(tableOff + 4), 0x80u);     // SectionSize
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(tableOff + 8), 0x0005);    // Flags: Eager | PCode
        bytes[tableOff + 10] = 2;   // Type: StubDispatch
        bytes[tableOff + 11] = 8;   // EntrySize
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(tableOff + 12), 0x5000u);  // SignaturesRva
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(tableOff + 16), 0x6000u);  // AuxiliaryDataRva

        var handle = ImageHandle.From("aabbccdd02", "r2r_import.dll");
        var clrSec = new NativeSection(".clr", ClrSectionVA, (ulong)clrSectionDataSize, ClrSectionRaw, (ulong)clrSectionFileSize);
        return new NativeImage(handle, "r2r_import.dll", BinaryFormat.Pe, Architecture.X64,
            [clrSec], [], new ReadOnlyMemory<byte>(bytes), 0x40000);
    }

    // Synthetic x64 R2R PE carrying a single string-payload section (zero-terminated UTF-8).
    private static NativeImage BuildSyntheticR2RWithStringSection(uint sectionType, string value)
    {
        const uint FileAlignment = 0x200;
        const uint ClrSectionVA = 0x2000;
        const uint ClrSectionRaw = 0x400;
        const uint R2RHeaderVA = ClrSectionVA + 72;

        int r2rHeaderSize = 16 + 12;  // header + 1 section entry
        uint payloadVA = R2RHeaderVA + (uint)r2rHeaderSize;
        byte[] payload = [.. System.Text.Encoding.UTF8.GetBytes(value), 0];
        int payloadSize = payload.Length;

        int clrSectionDataSize = 72 + r2rHeaderSize + payloadSize;
        int clrSectionFileSize = Align(clrSectionDataSize, (int)FileAlignment);
        int totalSize = (int)ClrSectionRaw + clrSectionFileSize;
        var bytes = new byte[totalSize];

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
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(r2rOff + 12), 1u);

        int secEntOff = r2rOff + 16;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 0), sectionType);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 4), payloadVA);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 8), (uint)payloadSize);

        int tableOff = r2rOff + r2rHeaderSize;
        payload.CopyTo(bytes.AsSpan(tableOff));

        var handle = ImageHandle.From("aabbccddee03", "r2r_idstr.dll");
        var clrSec = new NativeSection(".clr", ClrSectionVA, (ulong)clrSectionDataSize, ClrSectionRaw, (ulong)clrSectionFileSize);
        return new NativeImage(handle, "r2r_idstr.dll", BinaryFormat.Pe, Architecture.X64,
            [clrSec], [], new ReadOnlyMemory<byte>(bytes), 0x40000);
    }

    // Synthetic x64 R2R PE carrying a single raw-bytes payload section.
    private static NativeImage BuildSyntheticR2RWithRawSection(
        uint sectionType, byte[] payload, string fileName, string handleHex)
    {
        const uint FileAlignment = 0x200;
        const uint ClrSectionVA = 0x2000;
        const uint ClrSectionRaw = 0x400;
        const uint R2RHeaderVA = ClrSectionVA + 72;

        int r2rHeaderSize = 16 + 12;  // header + 1 section entry
        uint payloadVA = R2RHeaderVA + (uint)r2rHeaderSize;
        int payloadSize = payload.Length;

        int clrSectionDataSize = 72 + r2rHeaderSize + payloadSize;
        int clrSectionFileSize = Align(clrSectionDataSize, (int)FileAlignment);
        int totalSize = (int)ClrSectionRaw + clrSectionFileSize;
        var bytes = new byte[totalSize];

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
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(r2rOff + 12), 1u);

        int secEntOff = r2rOff + 16;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 0), sectionType);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 4), payloadVA);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secEntOff + 8), (uint)payloadSize);

        int tableOff = r2rOff + r2rHeaderSize;
        payload.CopyTo(bytes.AsSpan(tableOff));

        var handle = ImageHandle.From(handleHex, fileName);
        var clrSec = new NativeSection(".clr", ClrSectionVA, (ulong)clrSectionDataSize, ClrSectionRaw, (ulong)clrSectionFileSize);
        return new NativeImage(handle, fileName, BinaryFormat.Pe, Architecture.X64,
            [clrSec], [], new ReadOnlyMemory<byte>(bytes), 0x40000);
    }

    private static NativeImage BuildSyntheticArm64R2RImage(params Arm64RuntimeFunctionEntry[] runtimeFunctions)
    {
        const uint ClrSectionVA = 0x2000;
        const uint ClrSectionRaw = 0x400;
        const uint R2RHeaderVA = ClrSectionVA + 72;
        const uint FileAlignment = 0x200;

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
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optOff + 60), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optOff + 64), FileAlignment);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(optOff + 40), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optOff + 92), 16);

        int ddBase = optOff + 112;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ddBase + 14 * 8), ClrSectionVA);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ddBase + 14 * 8 + 4), 72);

        int secTableOff = peOff + 24 + 0xF0;
        bytes[secTableOff] = (byte)'.';
        bytes[secTableOff + 1] = (byte)'c';
        bytes[secTableOff + 2] = (byte)'l';
        bytes[secTableOff + 3] = (byte)'r';
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 8), (uint)clrSectionDataSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 12), ClrSectionVA);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 16), (uint)clrSectionFileSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 20), ClrSectionRaw);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secTableOff + 36), 0x40000040u);

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

        var handle = ImageHandle.From("arm64r2r", "arm64_r2r.dll");
        var clrSec = new NativeSection(".clr", ClrSectionVA, (ulong)clrSectionDataSize, ClrSectionRaw, (ulong)clrSectionFileSize);
        return new NativeImage(handle, "arm64_r2r.dll", BinaryFormat.Pe, Architecture.Arm64,
            [clrSec], [], new ReadOnlyMemory<byte>(bytes), 0x40000);
    }

    private static int Align(int value, int alignment) =>
        (value + alignment - 1) & ~(alignment - 1);

    private sealed record Arm64RuntimeFunctionEntry(uint Begin, uint LengthBytes, bool IsPacked)
    {
        public uint PackedUnwindData => 0x1u | ((LengthBytes / 4u) << 2);

        public uint XdataHeader => LengthBytes / 4u;

        public static Arm64RuntimeFunctionEntry Packed(uint begin, uint lengthBytes) => new(begin, lengthBytes, true);

        public static Arm64RuntimeFunctionEntry Xdata(uint begin, uint lengthBytes) => new(begin, lengthBytes, false);
    }

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

    [Fact]
    public void GetR2RHeader_SyntheticR2R_DecodesHeaderFlags()
    {
        // The synthetic builder writes raw flags 0x00000003
        // (PlatformNeutralSource | SkipTypeValidation).
        var image = BuildSyntheticR2RImage();
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value);

        result.IsError.Should().BeFalse();
        result.Data!.Flags.Should().Be(0x00000003u);
        result.Data.FlagsHex.Should().Be("0x00000003");
        result.Data.FlagNames.Should().Equal("PlatformNeutralSource", "SkipTypeValidation");
        result.Summary.Should().Contain("PlatformNeutralSource");
    }

    [Fact]
    public void GetR2RHeader_RuntimeFunctionsOnly_ImportSectionsNullByDefault()
    {
        var image = BuildSyntheticR2RImage();
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value);

        result.IsError.Should().BeFalse();
        result.Data!.ImportSections.Should().BeNull();
    }

    [Fact]
    public void GetR2RHeader_IncludeImportSections_NoSection_ReturnsNull()
    {
        var image = BuildSyntheticR2RImage();  // only a RuntimeFunctions section
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value, includeImportSections: true);

        result.IsError.Should().BeFalse();
        result.Data!.ImportSections.Should().BeNull();
    }

    [Fact]
    public void GetR2RHeader_IncludeImportSections_DecodesEntries()
    {
        var image = BuildSyntheticR2RWithImportSection();
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value, includeImportSections: true);

        result.IsError.Should().BeFalse();
        result.Data!.ImportSections.Should().HaveCount(1);

        var entry = result.Data.ImportSections![0];
        entry.Index.Should().Be(0);
        entry.SectionRva.Should().Be("0x00004000");
        entry.SectionSize.Should().Be(0x80);
        entry.Type.Should().Be(2);
        entry.TypeName.Should().Be("StubDispatch");
        entry.EntrySize.Should().Be(8);
        entry.FlagNames.Should().Equal("Eager", "PCode");
        entry.SignaturesRva.Should().Be("0x00005000");
        entry.AuxiliaryDataRva.Should().Be("0x00006000");
        result.Summary.Should().Contain("1 import sections");
    }

    [Fact]
    public void GetR2RHeader_ImportSectionsPresent_NotIncluded_OffersHint()
    {
        var image = BuildSyntheticR2RWithImportSection();
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value);

        result.IsError.Should().BeFalse();
        result.Data!.ImportSections.Should().BeNull();
        result.Hints.Should().Contain(h =>
            h.NextTool == "get_r2r_header" &&
            h.SuggestedArguments != null &&
            h.SuggestedArguments.ContainsKey("includeImportSections"));
    }

    [Fact]
    public void GetR2RHeader_RuntimeFunctionsOnly_IdentificationStringsNull()
    {
        var image = BuildSyntheticR2RImage();  // only a RuntimeFunctions section
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value);

        result.IsError.Should().BeFalse();
        result.Data!.CompilerIdentifier.Should().BeNull();
        result.Data.OwnerCompositeExecutable.Should().BeNull();
    }

    [Fact]
    public void GetR2RHeader_CompilerIdentifierSection_DecodesString()
    {
        const string identifier = "Crossgen2 synthetic compiler";
        var image = BuildSyntheticR2RWithStringSection(
            (uint)ReadyToRunSectionType.CompilerIdentifier, identifier);
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value);

        result.IsError.Should().BeFalse();
        result.Data!.CompilerIdentifier.Should().Be(identifier);
        result.Data.OwnerCompositeExecutable.Should().BeNull();
    }

    [Fact]
    public void GetR2RHeader_OwnerCompositeExecutableSection_DecodesString()
    {
        const string owner = "composite.r2r.dll";
        var image = BuildSyntheticR2RWithStringSection(
            (uint)ReadyToRunSectionType.OwnerCompositeExecutable, owner);
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value);

        result.IsError.Should().BeFalse();
        result.Data!.OwnerCompositeExecutable.Should().Be(owner);
        result.Data.CompilerIdentifier.Should().BeNull();
    }

    [Fact]
    public void GetR2RHeader_ComponentAssemblies_NullByDefault()
    {
        var payload = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0), 0x1111u);
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ComponentAssemblies, payload, "r2r_comp_def.dll", "aabbccddee06");
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value);

        result.IsError.Should().BeFalse();
        result.Data!.ComponentAssemblies.Should().BeNull();
        result.Data.ManifestAssemblyMvids.Should().BeNull();
    }

    [Fact]
    public void GetR2RHeader_ComponentAssembliesPresent_NotIncluded_OffersHint()
    {
        var payload = new byte[16];
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ComponentAssemblies, payload, "r2r_comp_hint.dll", "aabbccddee07");
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value);

        result.IsError.Should().BeFalse();
        result.Hints.Should().Contain(h =>
            h.NextTool == "get_r2r_header" &&
            h.SuggestedArguments != null &&
            h.SuggestedArguments.ContainsKey("includeCompositeInfo"));
    }

    [Fact]
    public void GetR2RHeader_IncludeCompositeInfo_DecodesComponentAssemblies()
    {
        var payload = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0), 0xAAAAu);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), 0x10u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), 0xBBBBu);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), 0x20u);
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ComponentAssemblies, payload, "r2r_comp_dec.dll", "aabbccddee08");
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value, includeCompositeInfo: true);

        result.IsError.Should().BeFalse();
        result.Data!.ComponentAssemblies.Should().HaveCount(1);
        var entry = result.Data.ComponentAssemblies![0];
        entry.Index.Should().Be(0);
        entry.CorHeaderRva.Should().Be("0x0000AAAA");
        entry.CorHeaderSize.Should().Be(0x10u);
        entry.AssemblyHeaderRva.Should().Be("0x0000BBBB");
        entry.AssemblyHeaderSize.Should().Be(0x20u);
    }

    [Fact]
    public void GetR2RHeader_IncludeCompositeInfo_DecodesManifestAssemblyMvids()
    {
        var g = Guid.NewGuid();
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ManifestAssemblyMvids, g.ToByteArray(), "r2r_mvid.dll", "aabbccddee09");
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value, includeCompositeInfo: true);

        result.IsError.Should().BeFalse();
        result.Data!.ManifestAssemblyMvids.Should().ContainSingle()
            .Which.Should().Be(g.ToString("D"));
    }

    [Fact]
    public void GetR2RHeader_IncludeMethodEntryPoints_DecodesMapping()
    {
        // Hand-encoded single-element NativeArray: nElements=1, entryIndexSize=0,
        // one block whose leaf payload is id = 3 << 1 = 6 (runtime function 3, no fixups).
        var blob = new byte[] { 0x08, 0x01, 0x00, 0x0C };
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.MethodDefEntryPoints, blob, "r2r_mep.dll", "aabbccddee0a");
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value, includeMethodEntryPoints: true);

        result.IsError.Should().BeFalse();
        var mep = result.Data!.MethodEntryPoints;
        mep.Should().NotBeNull();
        mep!.MethodCount.Should().Be(1u);
        mep.ReturnedCount.Should().Be(1);
        mep.Truncated.Should().BeFalse();
        mep.Entries.Should().ContainSingle();
        mep.Entries[0].Rid.Should().Be(1);
        mep.Entries[0].RuntimeFunctionIndex.Should().Be(3);
        mep.Entries[0].HasFixups.Should().BeFalse();
        result.Summary.Should().Contain("1 method entry points");
    }

    [Fact]
    public void GetR2RHeader_MethodEntryPointsPresent_NotIncluded_OffersHint()
    {
        var blob = new byte[] { 0x08, 0x01, 0x00, 0x0C };
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.MethodDefEntryPoints, blob, "r2r_mep_hint.dll", "aabbccddee0b");
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value);

        result.IsError.Should().BeFalse();
        result.Data!.MethodEntryPoints.Should().BeNull();
        result.Hints.Should().Contain(h =>
            h.NextTool == "get_r2r_header" &&
            h.SuggestedArguments != null &&
            h.SuggestedArguments.ContainsKey("includeMethodEntryPoints"));
    }

    [Fact]
    public void GetR2RHeader_IncludeAvailableTypes_DecodesTokens()
    {
        // Single-bucket hashtable (shift 0): TypeDef RID 5 and ExportedType RID 3.
        // Layout: [header][start][end] then per entry [lowHash][signed delta] and a payload.
        var blob = new byte[] { 0x00, 0x02, 0x06, 0x00, 0x06, 0x00, 0x04, 0x14, 0x0E };
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.AvailableTypes, blob, "r2r_at.dll", "aabbccddee0c");
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value, includeAvailableTypes: true);

        result.IsError.Should().BeFalse();
        var at = result.Data!.AvailableTypes;
        at.Should().NotBeNull();
        at!.ReturnedCount.Should().Be(2);
        at.Truncated.Should().BeFalse();
        at.Types.Should().HaveCount(2);
        at.Types[0].MetadataToken.Should().Be("0x02000005");
        at.Types[0].IsExportedType.Should().BeFalse();
        at.Types[1].MetadataToken.Should().Be("0x27000003");
        at.Types[1].IsExportedType.Should().BeTrue();
        result.Summary.Should().Contain("2 available types");
    }

    [Fact]
    public void GetR2RHeader_AvailableTypesPresent_NotIncluded_OffersHint()
    {
        var blob = new byte[] { 0x00, 0x02, 0x06, 0x00, 0x06, 0x00, 0x04, 0x14, 0x0E };
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.AvailableTypes, blob, "r2r_at_hint.dll", "aabbccddee0d");
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value);

        result.IsError.Should().BeFalse();
        result.Data!.AvailableTypes.Should().BeNull();
        result.Hints.Should().Contain(h =>
            h.NextTool == "get_r2r_header" &&
            h.SuggestedArguments != null &&
            h.SuggestedArguments.ContainsKey("includeAvailableTypes"));
    }

    [Fact]
    public void GetR2RHeader_IncludeInfoMaps_DecodesEnclosingTypeMap()
    {
        // 3 types: rid 2 nested in rid 1, others top-level. u16 count then u16 entries.
        var blob = new byte[] { 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 };
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.EnclosingTypeMap, blob, "r2r_etm.dll", "aabbccddee0e");
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value, includeInfoMaps: true);

        result.IsError.Should().BeFalse();
        var em = result.Data!.InfoMaps!.EnclosingTypeMap;
        em.Should().NotBeNull();
        em!.TypeDefCount.Should().Be(3);
        em.NestedTypes.Should().ContainSingle();
        em.NestedTypes[0].NestedTypeToken.Should().Be("0x02000002");
        em.NestedTypes[0].EnclosingTypeToken.Should().Be("0x02000001");
        result.Summary.Should().Contain("info maps decoded");
    }

    [Fact]
    public void GetR2RHeader_IncludeInfoMaps_DecodesMethodIsGenericMap()
    {
        // 10 methods, generic at index 0 (rid 1) and 9 (rid 10): int32 count then MSB-first bits.
        var blob = new byte[] { 0x0A, 0x00, 0x00, 0x00, 0x80, 0x40 };
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.MethodIsGenericMap, blob, "r2r_mgm.dll", "aabbccddee0f");
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value, includeInfoMaps: true);

        result.IsError.Should().BeFalse();
        var mm = result.Data!.InfoMaps!.MethodIsGenericMap;
        mm.Should().NotBeNull();
        mm!.MethodDefCount.Should().Be(10);
        mm.GenericMethodCount.Should().Be(2);
        mm.GenericMethodTokens.Should().Equal("0x06000001", "0x0600000A");
    }

    [Fact]
    public void GetR2RHeader_IncludeInfoMaps_DecodesTypeGenericInfoMap()
    {
        // 3 types: rid1 non-generic, rid2 generic 1-arg+variance (0x9), rid3 generic 2-arg+constraints (0x6).
        var blob = new byte[] { 0x03, 0x00, 0x00, 0x00, 0x09, 0x60 };
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.TypeGenericInfoMap, blob, "r2r_tgm.dll", "aabbccddee10");
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value, includeInfoMaps: true);

        result.IsError.Should().BeFalse();
        var tm = result.Data!.InfoMaps!.TypeGenericInfoMap;
        tm.Should().NotBeNull();
        tm!.GenericTypeCount.Should().Be(2);
        tm.GenericTypes[0].TypeToken.Should().Be("0x02000002");
        tm.GenericTypes[0].GenericArgCount.Should().Be(1);
        tm.GenericTypes[0].HasVariance.Should().BeTrue();
        tm.GenericTypes[1].TypeToken.Should().Be("0x02000003");
        tm.GenericTypes[1].GenericArgCount.Should().Be(2);
        tm.GenericTypes[1].HasConstraints.Should().BeTrue();
    }

    [Fact]
    public void GetR2RHeader_InfoMapsPresent_NotIncluded_OffersHint()
    {
        var blob = new byte[] { 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 };
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.EnclosingTypeMap, blob, "r2r_im_hint.dll", "aabbccddee11");
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value);

        result.IsError.Should().BeFalse();
        result.Data!.InfoMaps.Should().BeNull();
        result.Hints.Should().Contain(h =>
            h.NextTool == "get_r2r_header" &&
            h.SuggestedArguments != null &&
            h.SuggestedArguments.ContainsKey("includeInfoMaps"));
    }

    [Fact]
    public void GetR2RHeader_IncludeManifestMetadata_LocatesEcmaBlob()
    {
        var blob = BuildEcmaMetadataRoot("v4.0.30319", ("#~", 0x6Cu, 0x40u), ("#Strings", 0xACu, 0x20u));
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ManifestMetadata, blob, "r2r_mm.dll", "aabbccddee13");
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value, includeManifestMetadata: true);

        result.IsError.Should().BeFalse();
        var mm = result.Data!.ManifestMetadata;
        mm.Should().NotBeNull();
        mm!.Version.Should().Be("v4.0.30319");
        mm.Size.Should().Be((uint)blob.Length);
        mm.Streams.Select(s => s.Name).Should().Equal("#~", "#Strings");
        mm.Streams[0].Offset.Should().Be("0x0000006C");
        result.Summary.Should().Contain("manifest metadata located");
    }

    [Fact]
    public void GetR2RHeader_ManifestMetadataPresent_NotIncluded_OffersHint()
    {
        var blob = BuildEcmaMetadataRoot("v4.0.30319", ("#~", 0x6Cu, 0x40u));
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.ManifestMetadata, blob, "r2r_mm_hint.dll", "aabbccddee14");
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value);

        result.IsError.Should().BeFalse();
        result.Data!.ManifestMetadata.Should().BeNull();
        result.Hints.Should().Contain(h =>
            h.NextTool == "get_r2r_header" &&
            h.SuggestedArguments != null &&
            h.SuggestedArguments.ContainsKey("includeManifestMetadata"));
    }

    [Fact]
    public void GetR2RHeader_IncludeHotColdMap_DecodesPairs()
    {
        var blob = BuildHotColdMap((5u, 4u), (9u, 7u));
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.HotColdMap, blob, "r2r_hcm.dll", "aabbccddee15");
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value, includeHotColdMap: true);

        result.IsError.Should().BeFalse();
        var hcm = result.Data!.HotColdMap;
        hcm.Should().NotBeNull();
        hcm!.PairCount.Should().Be(2);
        hcm.Truncated.Should().BeFalse();
        hcm.Pairs.Should().HaveCount(2);
        hcm.Pairs[0].ColdRuntimeFunctionIndex.Should().Be(5u);
        hcm.Pairs[0].HotRuntimeFunctionIndex.Should().Be(4u);
        result.Summary.Should().Contain("2 hot/cold pairs");
    }

    [Fact]
    public void GetR2RHeader_HotColdMapPresent_NotIncluded_OffersHint()
    {
        var blob = BuildHotColdMap((5u, 4u));
        var image = BuildSyntheticR2RWithRawSection(
            (uint)ReadyToRunSectionType.HotColdMap, blob, "r2r_hcm_hint.dll", "aabbccddee16");
        var tools = MakeTools(image);

        var result = tools.GetR2RHeader(image.Handle.Value);

        result.IsError.Should().BeFalse();
        result.Data!.HotColdMap.Should().BeNull();
        result.Hints.Should().Contain(h =>
            h.NextTool == "get_r2r_header" &&
            h.SuggestedArguments != null &&
            h.SuggestedArguments.ContainsKey("includeHotColdMap"));
    }

    private static byte[] BuildHotColdMap(params (uint Cold, uint Hot)[] pairs)
    {
        var blob = new byte[pairs.Length * 8];
        for (var i = 0; i < pairs.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(i * 8), pairs[i].Cold);
            BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(i * 8 + 4), pairs[i].Hot);
        }
        return blob;
    }

    private static byte[] BuildEcmaMetadataRoot(string version, params (string Name, uint Offset, uint Size)[] streams)
    {
        var versionBytes = System.Text.Encoding.UTF8.GetBytes(version);
        var versionLen = (versionBytes.Length + 1 + 3) & ~3;

        var headers = new List<byte[]>();
        foreach (var (name, offset, size) in streams)
        {
            var nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
            var nameLen = (nameBytes.Length + 1 + 3) & ~3;
            var h = new byte[8 + nameLen];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(h, offset);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(4), size);
            nameBytes.CopyTo(h.AsSpan(8));
            headers.Add(h);
        }

        var total = 16 + versionLen + 4 + headers.Sum(h => h.Length);
        var blob = new byte[total];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(blob, 0x424A5342);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(4), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(6), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(12), (uint)versionLen);
        versionBytes.CopyTo(blob.AsSpan(16));

        var pos = 16 + versionLen;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(pos + 2), (ushort)streams.Length);
        pos += 4;
        foreach (var h in headers)
        {
            h.CopyTo(blob.AsSpan(pos));
            pos += h.Length;
        }

        return blob;
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
    public void ListR2RRuntimeFunctions_Arm64_ReturnsEntries()
    {
        var image = BuildSyntheticArm64R2RImage(
            Arm64RuntimeFunctionEntry.Packed(0x1000, 0x100),
            Arm64RuntimeFunctionEntry.Xdata(0x2000, 0x180));
        var tools = MakeTools(image);

        var result = tools.ListR2RRuntimeFunctions(image.Handle.Value);

        result.IsError.Should().BeFalse();
        result.Data!.Functions.Should().HaveCount(2);
        result.Data.Functions[0].BeginAddress.Should().Be("0x00001000");
        result.Data.Functions[0].EndAddress.Should().Be("0x00001100");
        result.Data.Functions[1].BeginAddress.Should().Be("0x00002000");
        result.Data.Functions[1].EndAddress.Should().Be("0x00002180");
    }

    [Fact]
    public void ListR2RRuntimeFunctions_Arm64RvaLookup_ReturnsMatchingFunction()
    {
        var image = BuildSyntheticArm64R2RImage(
            Arm64RuntimeFunctionEntry.Packed(0x1000, 0x100),
            Arm64RuntimeFunctionEntry.Xdata(0x2000, 0x180),
            Arm64RuntimeFunctionEntry.Packed(0x2400, 0x80));
        var tools = MakeTools(image);

        var result = tools.ListR2RRuntimeFunctions(image.Handle.Value, rva: "20A0");

        result.IsError.Should().BeFalse();
        result.Data!.IsLookup.Should().BeTrue();
        result.Data.Functions.Should().ContainSingle();
        result.Data.Functions[0].BeginAddress.Should().Be("0x00002000");
        result.Data.Functions[0].EndAddress.Should().Be("0x00002180");
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

        public DotnetNativeMcp.Core.NativeResult<string> RegisterHint(string path, string? buildId = null) => DotnetNativeMcp.Core.NativeResult.Ok("registered", path);

        public bool TryGet(string imageHandle, out NativeImage? image)
        {
            var found = _images.TryGetValue(imageHandle, out var resolved);
            image = resolved;
            return found;
        }

        public IReadOnlyList<NativeImage> List() => [.. _images.Values];
    }
}
