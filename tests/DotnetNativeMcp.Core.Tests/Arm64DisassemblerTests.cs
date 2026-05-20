using Arm64 = AsmArm64;
using DotnetNativeMcp.Core.Disassembly;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Xref;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Unit tests for <see cref="Arm64Disassembler"/>.
/// </summary>
public sealed class Arm64DisassemblerTests
{
    // ARM64 instruction encodings (little-endian bytes):
    // NOP        = 1F 20 03 D5  (D503201F)
    // RET        = C0 03 5F D6  (D65F03C0)
    // MOVZ x0,#1 = 20 00 80 D2  (D2800020)
    // BL   +4    = 01 00 00 94  (94000001)  — calls PC+4
    // B    +4    = 01 00 00 14  (14000001)  — jumps to PC+4
    // CBZ  x0,+16 = 40 00 00 B4  (B4000040)

    private const ulong SectionRva = 0x1000;

    private static NativeImage MakeArm64Image(byte[] code, ulong imageBase = 0x400000)
    {
        var handle = ImageHandle.From("arm64test", "test.elf");
        // Section at RVA 0x1000, file offset 0 in the raw bytes array.
        var section = new NativeSection(".text", SectionRva, (ulong)code.Length, 0, (ulong)code.Length);
        return new NativeImage(
            handle,
            "test.elf",
            BinaryFormat.Elf,
            Architecture.Arm64,
            [section],
            [],
            new ReadOnlyMemory<byte>(code),
            imageBase);
    }

    // ── Disassemble ───────────────────────────────────────────────────

    [Fact]
    public void Disassemble_Nop_ReturnsNopInstruction()
    {
        byte[] code = [0x1F, 0x20, 0x03, 0xD5]; // NOP
        var image = MakeArm64Image(code);

        var result = DotnetNativeMcp.Core.Disassembly.Arm64Disassembler.Disassemble(image, SectionRva, 10);

        (!result.IsError).Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data![0].Mnemonic.Should().Be("nop");
    }

    [Fact]
    public void Disassemble_Ret_StopsAfterReturn()
    {
        byte[] code =
        [
            0x1F, 0x20, 0x03, 0xD5, // NOP
            0xC0, 0x03, 0x5F, 0xD6, // RET
            0x1F, 0x20, 0x03, 0xD5, // NOP — must not appear
        ];
        var image = MakeArm64Image(code);

        var result = DotnetNativeMcp.Core.Disassembly.Arm64Disassembler.Disassemble(image, SectionRva, 10);

        (!result.IsError).Should().BeTrue();
        result.Data.Should().HaveCount(2);
        result.Data![1].Mnemonic.Should().Be("ret");
    }

    [Fact]
    public void Disassemble_BL_IncludesCrossRef()
    {
        byte[] code =
        [
            0x01, 0x00, 0x00, 0x94, // BL +4
            0x1F, 0x20, 0x03, 0xD5, // NOP
            0xC0, 0x03, 0x5F, 0xD6, // RET
        ];
        var image = MakeArm64Image(code);

        var result = DotnetNativeMcp.Core.Disassembly.Arm64Disassembler.Disassemble(image, SectionRva, 10);

        (!result.IsError).Should().BeTrue();
        result.Data![0].Mnemonic.Should().Be("bl");
        result.Data![0].CrossRef.Should().NotBeNull();
    }

    [Fact]
    public void Disassemble_B_IncludesCrossRef()
    {
        byte[] code =
        [
            0x01, 0x00, 0x00, 0x14, // B +4
            0x1F, 0x20, 0x03, 0xD5, // NOP
            0xC0, 0x03, 0x5F, 0xD6, // RET
        ];
        var image = MakeArm64Image(code);

        var result = DotnetNativeMcp.Core.Disassembly.Arm64Disassembler.Disassemble(image, SectionRva, 10);

        (!result.IsError).Should().BeTrue();
        result.Data![0].Mnemonic.Should().Be("b");
        result.Data![0].CrossRef.Should().NotBeNull();
    }

    [Fact]
    public void Disassemble_CBZ_IncludesCrossRef()
    {
        byte[] code =
        [
            0x40, 0x00, 0x00, 0xB4, // CBZ x0, +16
            0x1F, 0x20, 0x03, 0xD5, // NOP
            0xC0, 0x03, 0x5F, 0xD6, // RET
        ];
        var image = MakeArm64Image(code);

        var result = DotnetNativeMcp.Core.Disassembly.Arm64Disassembler.Disassemble(image, SectionRva, 10);

        (!result.IsError).Should().BeTrue();
        result.Data![0].Mnemonic.Should().Be("cbz");
        result.Data![0].CrossRef.Should().NotBeNull();
    }

    [Fact]
    public void Disassemble_BytesHex_IsCorrect()
    {
        byte[] code = [0x1F, 0x20, 0x03, 0xD5]; // NOP
        var image = MakeArm64Image(code, imageBase: 0);

        var result = DotnetNativeMcp.Core.Disassembly.Arm64Disassembler.Disassemble(image, SectionRva, 1);

        (!result.IsError).Should().BeTrue();
        result.Data![0].Bytes.Should().Be("1f2003d5");
    }

    [Fact]
    public void Disassemble_AddressHex_MatchesVA()
    {
        byte[] code = [0x1F, 0x20, 0x03, 0xD5]; // NOP
        var image = MakeArm64Image(code, imageBase: 0x400000);

        var result = DotnetNativeMcp.Core.Disassembly.Arm64Disassembler.Disassemble(image, SectionRva, 1);

        // VA = imageBase + sectionRva = 0x400000 + 0x1000 = 0x401000
        (!result.IsError).Should().BeTrue();
        result.Data![0].AddressHex.Should().Be("0000000000401000");
    }

    [Fact]
    public void Disassemble_MaxInstructions_IsRespected()
    {
        var code = Enumerable.Range(0, 10)
            .SelectMany(_ => new byte[] { 0x1F, 0x20, 0x03, 0xD5 }) // 10 NOPs
            .ToArray();
        var image = MakeArm64Image(code);

        var result = DotnetNativeMcp.Core.Disassembly.Arm64Disassembler.Disassemble(image, SectionRva, 3);

        (!result.IsError).Should().BeTrue();
        result.Data.Should().HaveCount(3);
    }

    [Fact]
    public void Disassemble_OutOfRangeRva_ReturnsError()
    {
        byte[] code = [0x1F, 0x20, 0x03, 0xD5];
        var image = MakeArm64Image(code);

        var result = DotnetNativeMcp.Core.Disassembly.Arm64Disassembler.Disassemble(image, 0xFFFF, 10);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be("address_out_of_range");
    }

    // ── IsBranchOrCall ────────────────────────────────────────────────

    [Theory]
    [InlineData(0x94000001U)] // BL +4
    [InlineData(0x14000001U)] // B +4
    [InlineData(0xB4000040U)] // CBZ x0, +16
    public void IsBranchOrCall_BranchInstructions_ReturnsTrue(uint raw)
    {
        var instr = Arm64.Arm64Instruction.Decode(raw);
        DotnetNativeMcp.Core.Disassembly.Arm64Disassembler.IsBranchOrCall(instr).Should().BeTrue();
    }

    [Theory]
    [InlineData(0xD503201FU)] // NOP
    [InlineData(0xD65F03C0U)] // RET
    [InlineData(0xD2800020U)] // MOVZ x0, #1
    public void IsBranchOrCall_NonBranchInstructions_ReturnsFalse(uint raw)
    {
        var instr = Arm64.Arm64Instruction.Decode(raw);
        DotnetNativeMcp.Core.Disassembly.Arm64Disassembler.IsBranchOrCall(instr).Should().BeFalse();
    }

    // ── ExtractLabelTarget ────────────────────────────────────────────

    [Fact]
    public void ExtractLabelTarget_BL_PlusFour_ReturnsIpPlusFour()
    {
        var instr = Arm64.Arm64Instruction.Decode(0x94000001U); // BL +4
        ulong instrIp = 0x401000;

        var target = DotnetNativeMcp.Core.Disassembly.Arm64Disassembler.ExtractLabelTarget(instr, instrIp);

        target.Should().Be(instrIp + 4);
    }

    [Fact]
    public void ExtractLabelTarget_B_PlusFour_ReturnsIpPlusFour()
    {
        var instr = Arm64.Arm64Instruction.Decode(0x14000001U); // B +4
        ulong instrIp = 0x401000;

        var target = DotnetNativeMcp.Core.Disassembly.Arm64Disassembler.ExtractLabelTarget(instr, instrIp);

        target.Should().Be(instrIp + 4);
    }

    [Fact]
    public void ExtractLabelTarget_Nop_ReturnsNull()
    {
        var instr = Arm64.Arm64Instruction.Decode(0xD503201FU); // NOP
        DotnetNativeMcp.Core.Disassembly.Arm64Disassembler.ExtractLabelTarget(instr, 0x401000).Should().BeNull();
    }

    // ── ScanSection ───────────────────────────────────────────────────

    [Fact]
    public void ScanSection_NoBranches_EmptyIndex()
    {
        byte[] code =
        [
            0x1F, 0x20, 0x03, 0xD5, // NOP
            0xC0, 0x03, 0x5F, 0xD6, // RET
        ];
        var image = MakeArm64Image(code);
        var index = new Dictionary<ulong, List<CallSite>>();

        DotnetNativeMcp.Core.Disassembly.Arm64Disassembler.ScanSection(image, image.Sections[0], index);

        index.Should().BeEmpty();
    }

    [Fact]
    public void ScanSection_BL_RecordsCallSite()
    {
        byte[] code =
        [
            0x01, 0x00, 0x00, 0x94, // BL +4
            0xC0, 0x03, 0x5F, 0xD6, // RET
        ];
        var image = MakeArm64Image(code);
        var index = new Dictionary<ulong, List<CallSite>>();

        DotnetNativeMcp.Core.Disassembly.Arm64Disassembler.ScanSection(image, image.Sections[0], index);

        index.Should().HaveCount(1);
        index.Values.First()[0].Mnemonic.Should().Be("bl");
    }
}
