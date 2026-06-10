using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.R2R;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Differential ("oracle") harness for the ReadyToRun RuntimeFunctions reader.
///
/// In a crossgen2 R2R image the RuntimeFunctions section (type 102) <em>is</em> the PE exception
/// data directory (.pdata) — same RVA, same size. That gives two fully independent paths to the
/// same table: <see cref="ReadyToRunReader"/> reaches it through the CLR managed-native header →
/// R2R signature → section table → type 102, while a battle-tested PE reader reaches it through
/// the optional-header data directories. This harness asserts the two paths agree, which guards
/// against the section-type enum drifting away from <c>coreclr/inc/readytorun.h</c> (the bug where
/// RuntimeFunctions was mapped to type 5 and the tool silently returned r2r_section_not_present on
/// every real image).
///
/// * Location is cross-checked against the external <c>llvm-readobj --file-headers</c> oracle.
/// * x64 entry decoding is cross-checked against an in-process, independent
///   <see cref="PEReader"/>-based decode of the same directory bytes.
///
/// Both tests no-op (pass) when the R2R fixture is unbuilt or, for the external check, when
/// <c>llvm-readobj</c> is unavailable. See docs/differential-testing.md.
/// </summary>
public class R2RRuntimeFunctionsDifferentialTests
{
    [Fact]
    public void RuntimeFunctionsSection_Location_MatchesLlvmReadobjExceptionDirectory()
    {
        var path = FixturePaths.SystemPrivateCoreLib;
        if (path is null) return; // fixture unbuilt → skip

        var image = PeNativeReader.Read(new ReadOnlyMemory<byte>(File.ReadAllBytes(path)), path);
        image.Should().NotBeNull();

        var hdr = ReadyToRunReader.ReadHeader(image!).Data!;
        var rtSection = hdr.FindSection(ReadyToRunSectionType.RuntimeFunctions);
        rtSection.Should().NotBeNull("a crossgen2 R2R image exposes the RuntimeFunctions section (type 102)");

        var oracle = LlvmReadobjOracle.TryReadPeExceptionDirectory(path);
        if (oracle is null) return; // llvm-readobj unavailable → skip

        rtSection!.VirtualAddress.Should().Be((uint)oracle.Value.Rva,
            "the R2R RuntimeFunctions section must coincide with the PE exception data directory");
        rtSection.Size.Should().Be((uint)oracle.Value.Size,
            "the R2R RuntimeFunctions section size must match the PE exception data directory size");
    }

    [Fact]
    public void RuntimeFunctions_X64Entries_MatchIndependentPeReaderDecode()
    {
        var path = FixturePaths.SystemPrivateCoreLib;
        if (path is null) return; // fixture unbuilt → skip

        var bytes = File.ReadAllBytes(path);
        var image = PeNativeReader.Read(new ReadOnlyMemory<byte>(bytes), path);
        image.Should().NotBeNull();

        // The x64 RUNTIME_FUNCTION decode (3 × uint32) only applies to x64 images.
        if (ReadyToRunReader.TryReadTargetArchitecture(image!) != Architecture.X64)
            return;

        var hdr = ReadyToRunReader.ReadHeader(image!).Data!;

        const int Take = 32;
        var page = ReadyToRunReader.ReadRuntimeFunctions(image!, hdr, 0, Take);
        page.IsError.Should().BeFalse();
        page.Data!.Functions.Should().NotBeEmpty();

        var expected = DecodeX64RuntimeFunctionsViaPeReader(bytes, page.Data.Functions.Count);
        expected.Should().NotBeNull("the independent PEReader path must locate the exception directory");

        for (var i = 0; i < page.Data.Functions.Count; i++)
        {
            var ours = page.Data.Functions[i];
            var (begin, end, unwind) = expected![i];

            ours.BeginAddress.Should().Be(begin, $"BeginAddress of RUNTIME_FUNCTION #{i} must match the independent decode");
            ours.EndAddress.Should().Be(end, $"EndAddress of RUNTIME_FUNCTION #{i} must match the independent decode");
            ours.UnwindInfoAddress.Should().Be(unwind, $"UnwindInfoAddress of RUNTIME_FUNCTION #{i} must match the independent decode");
        }
    }

    /// <summary>
    /// Independently locates the PE exception data directory via <see cref="PEReader"/> (a code path
    /// that never touches the R2R section table) and decodes the first <paramref name="count"/> x64
    /// RUNTIME_FUNCTION rows (begin, end, unwindInfo — each a little-endian uint32).
    /// </summary>
    private static List<(uint Begin, uint End, uint Unwind)>? DecodeX64RuntimeFunctionsViaPeReader(byte[] bytes, int count)
    {
        using var ms = new MemoryStream(bytes, writable: false);
        using var pe = new PEReader(ms);

        var dir = pe.PEHeaders.PEHeader?.ExceptionTableDirectory;
        if (dir is null || dir.Value.RelativeVirtualAddress == 0 || dir.Value.Size == 0)
            return null;

        var fileOffset = RvaToFileOffset(pe, (uint)dir.Value.RelativeVirtualAddress);
        if (fileOffset is null)
            return null;

        const int EntrySize = 12;
        var available = dir.Value.Size / EntrySize;
        var take = Math.Min(count, available);

        var result = new List<(uint, uint, uint)>(take);
        for (var i = 0; i < take; i++)
        {
            var off = fileOffset.Value + i * EntrySize;
            var begin = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(off));
            var end = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(off + 4));
            var unwind = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(off + 8));
            result.Add((begin, end, unwind));
        }

        return result;
    }

    private static int? RvaToFileOffset(PEReader pe, uint rva)
    {
        foreach (var section in pe.PEHeaders.SectionHeaders)
        {
            var start = (uint)section.VirtualAddress;
            var end = start + (uint)section.VirtualSize;
            if (rva >= start && rva < end)
                return section.PointerToRawData + (int)(rva - start);
        }

        return null;
    }
}
