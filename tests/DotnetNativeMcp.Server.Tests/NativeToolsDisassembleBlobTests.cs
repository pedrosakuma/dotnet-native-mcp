using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Disassembly;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using DotnetNativeMcp.Core.Xref;
using DotnetNativeMcp.Server.Tools;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Server.Tests;

/// <summary>
/// Tests for the rawBlob=true mode of the <c>disassemble</c> tool.
/// </summary>
public class NativeToolsDisassembleBlobTests : IDisposable
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static NativeTools MakeTools() =>
        new NativeTools(new EmptyRegistry(), new NativeCallGraphCache(), new SourceResolver());

    private readonly List<string> _tempFiles = [];

    private string WriteTempBlob(byte[] bytes)
    {
        var path = Path.Combine(
            Path.GetDirectoryName(typeof(NativeToolsDisassembleBlobTests).Assembly.Location)!,
            "scratch",
            $"blob-test-{Guid.NewGuid():N}.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        _tempFiles.Add(path);
        return path;
    }

    private string WriteTempText(string fileName, string content)
    {
        var path = Path.Combine(
            Path.GetDirectoryName(typeof(NativeToolsDisassembleBlobTests).Assembly.Location)!,
            "scratch",
            $"{Guid.NewGuid():N}-{fileName}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { }
        GC.SuppressFinalize(this);
    }

    // x64 blob: MOV eax,1 (B8 01 00 00 00) + CALL rel32 (E8 00 00 00 00) + RET (C3)
    // Total: 10 bytes.
    private static readonly byte[] X64Blob =
    [
        0xB8, 0x01, 0x00, 0x00, 0x00, // mov eax, 1
        0xE8, 0x00, 0x00, 0x00, 0x00, // call +5 (rel32=0 → next instr)
        0xC3,                          // ret
    ];

    // ARM64 blob: BL +4 (94000001) + RET (D65F03C0)
    private static readonly byte[] Arm64Blob =
    [
        0x01, 0x00, 0x00, 0x94, // bl +4
        0xC0, 0x03, 0x5F, 0xD6, // ret
    ];

    private static readonly byte[] NopBlob = Enumerable.Repeat((byte)0x90, 32).ToArray();

    // ── happy path: x64 blob ──────────────────────────────────────────────────

    [Fact]
    public void DisassembleBlob_X64_HappyPath_ReturnsMnemonicsAtCorrectAddresses()
    {
        const ulong baseAddress = 0x00007FFF_12340000UL;
        var blobPath = WriteTempBlob(X64Blob);
        var tools = MakeTools();

        var result = tools.Disassemble(
            imagePath: blobPath,
            size: X64Blob.Length,
            architecture: "X64",
            baseAddress: baseAddress,
            rawBlob: true);

        result.IsError.Should().BeFalse(result.Error?.Message ?? string.Empty);
        var instrs = result.Data!;
        instrs.Should().HaveCount(3);

        instrs[0].Mnemonic.Should().Be("mov");
        instrs[1].Mnemonic.Should().Be("call");
        instrs[2].Mnemonic.Should().Be("ret");

        // Addresses must be relative to baseAddress.
        ulong addr0 = Convert.ToUInt64(instrs[0].AddressHex, 16);
        ulong addr1 = Convert.ToUInt64(instrs[1].AddressHex, 16);
        ulong addr2 = Convert.ToUInt64(instrs[2].AddressHex, 16);

        addr0.Should().Be(baseAddress);
        addr1.Should().Be(baseAddress + 5);   // 5-byte MOV
        addr2.Should().Be(baseAddress + 10);  // 5-byte CALL
    }

    [Fact]
    public void DisassembleBlob_X64_RvaOffset_StartsAtOffset()
    {
        const ulong baseAddress = 0x10000000UL;
        // Skip the MOV and start at the CALL (offset 5).
        // baseAddress = VA of byte 0 of the blob; first decoded instruction is at baseAddress+5.
        var blobPath = WriteTempBlob(X64Blob);
        var tools = MakeTools();

        var result = tools.Disassemble(
            imagePath: blobPath,
            rva: 5,
            size: X64Blob.Length - 5,
            architecture: "x64",
            baseAddress: baseAddress,
            rawBlob: true);

        result.IsError.Should().BeFalse(result.Error?.Message ?? string.Empty);
        result.Data!.Should().HaveCount(2);
        result.Data![0].Mnemonic.Should().Be("call");

        // First instruction must be at baseAddress + 5.
        ulong instrAddr = Convert.ToUInt64(result.Data![0].AddressHex, 16);
        instrAddr.Should().Be(baseAddress + 5);
    }

    [Fact]
    public void DisassembleBlob_WithIlMap_AnnotatesEachInstructionRange()
    {
        const ulong baseAddress = 0x20000000UL;
        var blobPath = WriteTempBlob(NopBlob);
        var ilMapPath = WriteTempText("blob.ilmap", """
            # comment
            7	noinfo
            0	prolog
            2	0
            5	a
            """);
        var tools = MakeTools();

        var result = tools.Disassemble(
            imagePath: blobPath,
            size: NopBlob.Length,
            architecture: "x64",
            baseAddress: baseAddress,
            ilMapPath: ilMapPath,
            maxInstructions: 8,
            rawBlob: true);

        result.IsError.Should().BeFalse(result.Error?.Message ?? string.Empty);
        result.Data!.Should().HaveCount(8);
        result.Data!.Select(instruction => instruction.IlOffset).Should().Equal(
            "prolog",
            "prolog",
            "0",
            "0",
            "0",
            "a",
            "a",
            "noinfo");
    }

    // ── happy path: ARM64 blob ────────────────────────────────────────────────

    [Fact]
    public void DisassembleBlob_Arm64_HappyPath_ReturnsMnemonics()
    {
        const ulong baseAddress = 0x0000FFFF_80001000UL;
        var blobPath = WriteTempBlob(Arm64Blob);
        var tools = MakeTools();

        var result = tools.Disassemble(
            imagePath: blobPath,
            size: Arm64Blob.Length,
            architecture: "Arm64",
            baseAddress: baseAddress,
            rawBlob: true);

        result.IsError.Should().BeFalse(result.Error?.Message ?? string.Empty);
        var instrs = result.Data!;
        instrs[0].Mnemonic.Should().Be("bl");
        instrs[1].Mnemonic.Should().Be("ret");
    }

    // ── resolveSource silently ignored ────────────────────────────────────────

    [Fact]
    public void DisassembleBlob_ResolveSourceTrue_IsIgnoredNoError()
    {
        var blobPath = WriteTempBlob(X64Blob);
        var tools = MakeTools();

        var result = tools.Disassemble(
            imagePath: blobPath,
            size: X64Blob.Length,
            architecture: "x64",
            baseAddress: 0x1000UL,
            resolveSource: true,
            rawBlob: true);

        result.IsError.Should().BeFalse(result.Error?.Message ?? string.Empty);
        result.Data!.Should().NotBeEmpty();
        // SourceLocation must always be null for raw-blob results.
        result.Data!.Should().AllSatisfy(i => i.Source.Should().BeNull());
    }

    // ── missing required params ───────────────────────────────────────────────

    [Fact]
    public void DisassembleBlob_MissingSize_ReturnsRawBlobMissingSize()
    {
        var blobPath = WriteTempBlob(X64Blob);
        var tools = MakeTools();

        var result = tools.Disassemble(
            imagePath: blobPath,
            architecture: "x64",
            baseAddress: 0x1000UL,
            rawBlob: true);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.RawBlobMissingSize);
    }

    [Fact]
    public void DisassembleBlob_MissingArchitecture_ReturnsRawBlobMissingArchitecture()
    {
        var blobPath = WriteTempBlob(X64Blob);
        var tools = MakeTools();

        var result = tools.Disassemble(
            imagePath: blobPath,
            size: X64Blob.Length,
            baseAddress: 0x1000UL,
            rawBlob: true);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.RawBlobMissingArchitecture);
    }

    [Fact]
    public void DisassembleBlob_MissingBaseAddress_ReturnsRawBlobMissingBaseAddress()
    {
        var blobPath = WriteTempBlob(X64Blob);
        var tools = MakeTools();

        var result = tools.Disassemble(
            imagePath: blobPath,
            size: X64Blob.Length,
            architecture: "x64",
            rawBlob: true);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.RawBlobMissingBaseAddress);
    }

    // ── imageHandle + rawBlob=true rejected ───────────────────────────────────

    [Fact]
    public void DisassembleBlob_HandleAndRawBlob_ReturnsInvalidArgument()
    {
        var tools = MakeTools();

        var result = tools.Disassemble(
            imageHandle: "some-handle",
            size: X64Blob.Length,
            architecture: "x64",
            baseAddress: 0x1000UL,
            rawBlob: true);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void DisassembleBlob_IlMapWithoutRawBlob_ReturnsInvalidArgument()
    {
        var blobPath = WriteTempBlob(X64Blob);
        var ilMapPath = WriteTempText("blob.ilmap", "0\t0\n");
        var tools = MakeTools();

        var result = tools.Disassemble(
            imagePath: blobPath,
            rva: 0,
            size: X64Blob.Length,
            architecture: "x64",
            ilMapPath: ilMapPath);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
        result.Error.Message.Should().Contain("ilMapPath");
    }

    // ── file not found ────────────────────────────────────────────────────────

    [Fact]
    public void DisassembleBlob_FileNotFound_ReturnsBinaryNotFound()
    {
        var tools = MakeTools();

        var result = tools.Disassemble(
            imagePath: "/no/such/blob.bin",
            size: 64,
            architecture: "x64",
            baseAddress: 0x1000UL,
            rawBlob: true);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.BinaryNotFound);
    }

    // ── out-of-range offset ───────────────────────────────────────────────────

    [Fact]
    public void DisassembleBlob_OffsetPlusSizeExceedsBlob_ReturnsAddressOutOfRange()
    {
        var blobPath = WriteTempBlob(X64Blob);
        var tools = MakeTools();

        var result = tools.Disassemble(
            imagePath: blobPath,
            rva: 0,
            size: X64Blob.Length + 1000, // larger than the blob
            architecture: "x64",
            baseAddress: 0x1000UL,
            rawBlob: true);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.AddressOutOfRange);
    }

    // ── unknown architecture ──────────────────────────────────────────────────

    [Fact]
    public void DisassembleBlob_UnknownArchitecture_ReturnsDisassemblyUnsupported()
    {
        var blobPath = WriteTempBlob(X64Blob);
        var tools = MakeTools();

        var result = tools.Disassemble(
            imagePath: blobPath,
            size: X64Blob.Length,
            architecture: "mips",
            baseAddress: 0x1000UL,
            rawBlob: true);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.DisassemblyUnsupported);
    }

    // ── registry stub ─────────────────────────────────────────────────────────

    private sealed class EmptyRegistry : INativeBinaryRegistry
    {
        public NativeResult<NativeImage> Load(string path, string? expectedBuildId = null) =>
            throw new NotSupportedException();

        public DotnetNativeMcp.Core.NativeResult<string> RegisterHint(string path, string? buildId = null) => DotnetNativeMcp.Core.NativeResult.Ok("registered", path);

        public bool TryGet(string imageHandle, out NativeImage? image)
        {
            image = null;
            return false;
        }

        public IReadOnlyList<NativeImage> List() => [];
    }
}
