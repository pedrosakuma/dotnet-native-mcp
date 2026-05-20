using DotnetNativeMcp.Core.Disassembly;
using DotnetNativeMcp.Core.Errors;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class RawDisassemblerTests
{
    // ── missing file ──────────────────────────────────────────────────────────

    [Fact]
    public void Disassemble_FileNotFound_ReturnsBinaryNotFound()
    {
        var result = RawDisassembler.Disassemble(
            "/no/such/file.dll", rva: 0, size: 64,
            arch: null, baseAddress: null,
            maxInstructions: 10);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.BinaryNotFound);
    }

    // ── out of range ──────────────────────────────────────────────────────────

    [Fact]
    public void Disassemble_RvaOutOfRange_ReturnsAddressOutOfRange()
    {
        var fixturePath = FixturePaths.SampleAot;
        if (fixturePath is null) return;

        // RVA 0xFFFFFF00 is well beyond the end of the file.
        var result = RawDisassembler.Disassemble(
            fixturePath, rva: 0x7FFF0000, size: 64,
            arch: null, baseAddress: null,
            maxInstructions: 10);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.AddressOutOfRange);
    }

    // ── happy path on SampleAot ELF ──────────────────────────────────────────

    [Fact]
    public void Disassemble_SampleAotTextSection_ReturnsAtLeastOneInstruction()
    {
        var fixturePath = FixturePaths.SampleAot;
        if (fixturePath is null) return; // fixture not built on this platform

        // .text section: VA=0x58c0, file-offset=0x58c0 (imageBase=0 → RVA=VA)
        const int textRva = 0x58c0;
        const int codeSize = 64;

        var result = RawDisassembler.Disassemble(
            fixturePath, rva: textRva, size: codeSize,
            arch: null, baseAddress: null,
            maxInstructions: 32);

        result.IsError.Should().BeFalse(result.Error?.Message ?? string.Empty);
        result.Data.Should().NotBeEmpty();
    }

    // ── arch override ─────────────────────────────────────────────────────────

    [Fact]
    public void Disassemble_WithExplicitArchOverride_DecodesCorrectly()
    {
        var fixturePath = FixturePaths.SampleAot;
        if (fixturePath is null) return;

        const int textRva = 0x58c0;
        const int codeSize = 32;

        var result = RawDisassembler.Disassemble(
            fixturePath, rva: textRva, size: codeSize,
            arch: Imaging.Architecture.X64, baseAddress: null,
            maxInstructions: 10);

        result.IsError.Should().BeFalse();
        result.Data.Should().NotBeEmpty();
    }
}
