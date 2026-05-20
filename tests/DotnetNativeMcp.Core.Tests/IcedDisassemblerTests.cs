using DotnetNativeMcp.Core.Disassembly;
using DotnetNativeMcp.Core.Imaging;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class IcedDisassemblerTests
{
    private static NativeImage MakeImage(byte[] code, Architecture arch = Architecture.X64, ulong imageBase = 0)
    {
        var handle = Identity.ImageHandle.From("test", "test.so");
        // Single section covering the code at RVA 0.
        var section = new NativeSection(".text", 0, (ulong)code.Length, 0, (ulong)code.Length);
        return new NativeImage(handle, "test.so", BinaryFormat.Elf, arch,
            [section], [], new ReadOnlyMemory<byte>(code), imageBase);
    }

    [Fact]
    public void Disassemble_NopRet_ReturnsCorrectMnemonics()
    {
        // 0x90 = NOP, 0xC3 = RET
        var image = MakeImage([0x90, 0xC3]);
        var result = IcedDisassembler.Disassemble(image, rva: 0, maxInstructions: 10);

        result.IsError.Should().BeFalse();
        result.Data.Should().HaveCount(2);
        result.Data![0].Mnemonic.Should().Be("nop");
        result.Data[1].Mnemonic.Should().Be("ret");
    }

    [Fact]
    public void Disassemble_NopRet_BytesAreHex()
    {
        var image = MakeImage([0x90, 0xC3]);
        var result = IcedDisassembler.Disassemble(image, rva: 0, maxInstructions: 10);

        result.Data![0].Bytes.Should().Be("90");
        result.Data[1].Bytes.Should().Be("c3");
    }

    [Fact]
    public void Disassemble_NopRet_AddressesAreSequential()
    {
        var image = MakeImage([0x90, 0xC3], imageBase: 0x400000);
        var result = IcedDisassembler.Disassemble(image, rva: 0, maxInstructions: 10);

        // RVA 0 + imageBase 0x400000 = IP 0x400000
        result.Data![0].AddressHex.Should().Be("0000000000400000");
        result.Data[1].AddressHex.Should().Be("0000000000400001");
    }

    [Fact]
    public void Disassemble_ZeroMaxInstructions_ReturnsError()
    {
        var image = MakeImage([0x90]);
        var result = IcedDisassembler.Disassemble(image, rva: 0, maxInstructions: 0);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(Errors.ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void Disassemble_AddressOutOfRange_ReturnsError()
    {
        var image = MakeImage([0x90, 0xC3]);
        var result = IcedDisassembler.Disassemble(image, rva: 0x10000, maxInstructions: 10);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(Errors.ErrorKinds.AddressOutOfRange);
    }

    [Fact]
    public void Disassemble_Arm64_RoutesToArm64Decoder()
    {
        // NOP = 1F 20 03 D5
        var image = MakeImage([0x1F, 0x20, 0x03, 0xD5], arch: Architecture.Arm64);
        var result = IcedDisassembler.Disassemble(image, rva: 0, maxInstructions: 4);

        result.IsError.Should().BeFalse();
        result.Data.Should().HaveCount(1);
        result.Data![0].Mnemonic.Should().Be("nop");
    }

    [Fact]
    public void Disassemble_Call_ProducesCrossRef()
    {
        // E8 00 00 00 00 = CALL +5 (calls the next instruction, a common pattern)
        // C3 = RET
        var image = MakeImage([0xE8, 0x00, 0x00, 0x00, 0x00, 0xC3]);
        var result = IcedDisassembler.Disassemble(image, rva: 0, maxInstructions: 5);

        result.IsError.Should().BeFalse();
        var call = result.Data![0];
        call.Mnemonic.Should().Be("call");
        // Cross-ref may or may not resolve a symbol name, but the address should be populated.
        call.CrossRef.Should().NotBeNull();
    }
}
