using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Xref;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class NativeCallGraphBuilderTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static NativeImage MakeImage(byte[] code, Architecture arch = Architecture.X64, ulong imageBase = 0x400000)
    {
        var handle = Identity.ImageHandle.From("testcg", "test.so");
        var section = new NativeSection(".text", 0, (ulong)code.Length, 0, (ulong)code.Length);
        return new NativeImage(handle, "test.so", BinaryFormat.Elf, arch,
            [section], [], new ReadOnlyMemory<byte>(code), imageBase);
    }

    // ---------------------------------------------------------------------------
    // ARM64 → empty index
    // ---------------------------------------------------------------------------

    [Fact]
    public void Build_Arm64_ReturnsEmptyIndex()
    {
        var image = MakeImage([0x00, 0x00, 0x00, 0x00], arch: Architecture.Arm64);
        var index = NativeCallGraphBuilder.Build(image);

        index.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // NOP/RET sequence → no branch instructions → empty index
    // ---------------------------------------------------------------------------

    [Fact]
    public void Build_NopRet_ReturnsEmptyIndex()
    {
        // 0x90 = NOP, 0xC3 = RET — neither is a branch
        var image = MakeImage([0x90, 0xC3]);
        var index = NativeCallGraphBuilder.Build(image);

        index.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // Single CALL instruction → one entry in index
    // ---------------------------------------------------------------------------

    [Fact]
    public void Build_SingleCall_AddsCallSiteToIndex()
    {
        // E8 00 00 00 00 = CALL rel32 +0 (calls the byte immediately after this instruction)
        // The instruction is at VA = imageBase + 0 = 0x400000
        // target = 0x400000 + 5 + 0 = 0x400005
        var code = new byte[] { 0xE8, 0x00, 0x00, 0x00, 0x00, 0x90 };
        var image = MakeImage(code);
        var index = NativeCallGraphBuilder.Build(image);

        // One target should be in the index
        index.Should().HaveCount(1);

        var targetVa = 0x400005UL;
        index.Should().ContainKey(targetVa);

        var callers = index[targetVa];
        callers.Should().ContainSingle();

        var site = callers[0];
        site.Mnemonic.Should().Be("call");
        site.RawBytes.Should().Be("e800000000");
        site.SourceAddressHex.Should().Be("0000000000400000");
    }

    // ---------------------------------------------------------------------------
    // JMP instruction → also indexed
    // ---------------------------------------------------------------------------

    [Fact]
    public void Build_UnconditionalJmp_AddsCallSiteToIndex()
    {
        // EB 03 = JMP +3 (rel8), jumps to offset 5 from instruction start
        // target = imageBase + 0 + 2 + 3 = 0x400005
        var code = new byte[] { 0xEB, 0x03, 0x90, 0x90, 0x90, 0xC3 };
        var image = MakeImage(code);
        var index = NativeCallGraphBuilder.Build(image);

        index.Should().HaveCount(1);

        var targetVa = 0x400005UL;
        index.Should().ContainKey(targetVa);

        var site = index[targetVa][0];
        site.Mnemonic.Should().Be("jmp");
    }

    // ---------------------------------------------------------------------------
    // Conditional branch → also indexed
    // ---------------------------------------------------------------------------

    [Fact]
    public void Build_ConditionalBranch_AddsCallSiteToIndex()
    {
        // 74 03 = JE +3 (rel8 conditional), jumps to offset 5 from instruction start
        var code = new byte[] { 0x74, 0x03, 0x90, 0x90, 0x90, 0xC3 };
        var image = MakeImage(code);
        var index = NativeCallGraphBuilder.Build(image);

        index.Should().NotBeEmpty();

        var targetVa = 0x400005UL;
        index.Should().ContainKey(targetVa);

        var site = index[targetVa][0];
        site.Mnemonic.Should().Be("je");
    }

    // ---------------------------------------------------------------------------
    // Multiple callers of the same target
    // ---------------------------------------------------------------------------

    [Fact]
    public void Build_TwoCallsSameTarget_BothInIndex()
    {
        // Two CALL +0 instructions calling the byte at offset 5/10.
        // First:  E8 00 00 00 00 at offset 0  → target = imageBase + 5 = 0x400005
        // Second: E8 F9 FF FF FF at offset 5  → target = imageBase + 5 = 0x400005 (calls back to +5)
        // Actually let's build two calls that target the same address more explicitly.
        // Let's put a NOP at offset 10 (0x40000A) and call it twice:
        // call rel32 at 0: target = 0x400000+5+5 = 0x40000A  → E8 05 00 00 00
        // call rel32 at 5: target = 0x400000+10+0 = 0x40000A → E8 00 00 00 00
        // then NOP+RET at offset 10
        var code = new byte[]
        {
            0xE8, 0x05, 0x00, 0x00, 0x00,  // offset 0: CALL 0x40000A
            0xE8, 0x00, 0x00, 0x00, 0x00,  // offset 5: CALL 0x40000A
            0x90, 0xC3,                    // offset 10: NOP, RET
        };
        var image = MakeImage(code);
        var index = NativeCallGraphBuilder.Build(image);

        var targetVa = 0x40000AUL;
        index.Should().ContainKey(targetVa);
        index[targetVa].Should().HaveCount(2);
    }

    // ---------------------------------------------------------------------------
    // Section without .text name → not scanned (default code section filter)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Build_NonCodeSection_SkipsSection()
    {
        // E8 00 00 00 00 = CALL rel32
        var code = new byte[] { 0xE8, 0x00, 0x00, 0x00, 0x00, 0x90 };
        var handle = Identity.ImageHandle.From("noncg", "data.so");
        var section = new NativeSection(".rodata", 0, (ulong)code.Length, 0, (ulong)code.Length);
        var image = new NativeImage(handle, "data.so", BinaryFormat.Elf, Architecture.X64,
            [section], [], new ReadOnlyMemory<byte>(code), 0x400000);

        var index = NativeCallGraphBuilder.Build(image);

        // .rodata is not a code section; no branches should be indexed.
        index.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // FindCallers integration with SampleAot fixture
    // ---------------------------------------------------------------------------

    [Fact]
    public void Build_SampleAot_FindsCallersForSomeSymbol()
    {
        var fixturePath = FixturePaths.SampleAot;
        if (fixturePath is null || !File.Exists(fixturePath))
            return; // AOT toolchain not available; skip

        var loaded = NativeImageLoader.Load(fixturePath);
        loaded.IsError.Should().BeFalse();

        var image = loaded.Data!;

        // Pick the first function-type symbol and look for callers.
        // A real AOT binary will have at least one symbol that is called.
        var index = NativeCallGraphBuilder.Build(image);

        // The index should be non-empty for a real AOT binary.
        index.Should().NotBeEmpty();

        // At least one target in the index should have one or more callers.
        index.Values.Any(callers => callers.Count >= 1).Should().BeTrue();
    }
}
