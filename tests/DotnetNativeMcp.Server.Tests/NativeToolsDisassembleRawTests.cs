using System.Reflection.PortableExecutable;
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

public class NativeToolsDisassembleRawTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static NativeTools MakeTools(params NativeImage[] images) =>
        new NativeTools(new TestBinaryRegistry(images), new NativeCallGraphCache(), new SourceResolver());

    private static NativeTools EmptyTools() => MakeTools();

    // ── validation: imagePath without rva/size ────────────────────────────────

    [Fact]
    public void Disassemble_ImagePathWithoutRvaOrSize_ReturnsInvalidArgument()
    {
        var tools = EmptyTools();

        var result = tools.Disassemble(
            imagePath: "/some/file.dll");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void Disassemble_ImagePathWithRvaButNoSize_ReturnsInvalidArgument()
    {
        var tools = EmptyTools();

        var result = tools.Disassemble(
            imagePath: "/some/file.dll",
            rva: 0x1000);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // ── validation: imageHandle + imagePath together ──────────────────────────

    [Fact]
    public void Disassemble_HandleAndPathTogether_ReturnsInvalidArgument()
    {
        var tools = EmptyTools();

        var result = tools.Disassemble(
            imageHandle: "some-handle",
            imagePath: "/some/file.dll",
            rva: 0x1000,
            size: 64);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // ── validation: neither supplied ──────────────────────────────────────────

    [Fact]
    public void Disassemble_NeitherHandleNorPath_ReturnsInvalidArgument()
    {
        var tools = EmptyTools();

        var result = tools.Disassemble();

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // ── imagePath: out of range ───────────────────────────────────────────────

    [Fact]
    public void Disassemble_ImagePath_RvaPlusSizeExceedsFile_ReturnsAddressOutOfRange()
    {
        var tools = EmptyTools();
        var selfPath = typeof(NativeToolsDisassembleRawTests).Assembly.Location;
        if (!File.Exists(selfPath)) return;

        var result = tools.Disassemble(
            imagePath: selfPath,
            rva: 0x7FFF0000,
            size: 64);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.AddressOutOfRange);
    }

    // ── imagePath: happy path on SampleAot ELF ───────────────────────────────

    [Fact]
    public void Disassemble_ImagePath_SampleAot_ReturnsInstructions()
    {
        var fixturePath = FindSampleAot();
        if (fixturePath is null) return;

        var tools = EmptyTools();

        // .text section: VA=0x58c0 (imageBase=0 → RVA=VA)
        var result = tools.Disassemble(
            imagePath: fixturePath,
            rva: 0x58c0,
            size: 64,
            maxInstructions: 32);

        result.IsError.Should().BeFalse(result.Error?.Message ?? string.Empty);
        result.Data.Should().NotBeEmpty();
    }

    // ── imagePath: managed PE ────────────────────────────────────────────────

    [Fact]
    public void Disassemble_ImagePath_ManagedPe_ReturnsInstructions()
    {
        // Use the test assembly itself — a plain managed PE that load_native_binary rejects.
        var selfPath = typeof(NativeToolsDisassembleRawTests).Assembly.Location;
        if (!File.Exists(selfPath)) return;

        // Dynamically resolve the .text section start RVA so the test stays
        // portable across builds.
        int textRva;
        try
        {
            using var ms = new System.IO.MemoryStream(File.ReadAllBytes(selfPath));
            using var pe = new PEReader(ms, PEStreamOptions.Default);
            var text = pe.PEHeaders.SectionHeaders.FirstOrDefault(s => s.Name == ".text");
            if (text.Name is null) return; // no .text section — skip
            textRva = text.VirtualAddress;
        }
        catch
        {
            return;
        }

        var tools = EmptyTools();

        var result = tools.Disassemble(
            imagePath: selfPath,
            rva: textRva,
            size: 64,
            maxInstructions: 10);

        // Should succeed — the raw path does NOT reject managed PEs.
        result.IsError.Should().BeFalse(result.Error?.Message ?? string.Empty);
        result.Data.Should().NotBeEmpty();
    }

    // ── imageHandle: existing path still works ───────────────────────────────

    [Fact]
    public void Disassemble_ImageHandle_ExistingPath_StillWorks()
    {
        // Build a minimal synthetic NativeImage and exercise the registered-handle path.
        var code = new byte[] { 0x90, 0xC3 }; // NOP, RET
        var handle = ImageHandle.From("test-disasm", "test.so");
        var section = new NativeSection(".text", 0, (ulong)code.Length, 0, (ulong)code.Length);
        var image = new NativeImage(
            handle, "test.so", BinaryFormat.Elf, Architecture.X64,
            [section], [], new ReadOnlyMemory<byte>(code), 0);

        var tools = MakeTools(image);

        var result = tools.Disassemble(
            imageHandle: handle.Value,
            address: "0");

        result.IsError.Should().BeFalse();
        result.Data.Should().HaveCount(2);
        result.Data![0].Mnemonic.Should().Be("nop");
        result.Data[1].Mnemonic.Should().Be("ret");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string? FindSampleAot()
    {
        var dir = Path.GetDirectoryName(typeof(NativeToolsDisassembleRawTests).Assembly.Location) ?? ".";
        var candidate = Path.Combine(dir, "fixtures", "SampleAot", "SampleAot");
        return File.Exists(candidate) ? candidate : null;
    }

    private sealed class TestBinaryRegistry(params NativeImage[] images) : INativeBinaryRegistry
    {
        private readonly Dictionary<string, NativeImage> _images =
            images.ToDictionary(img => img.Handle.Value, StringComparer.OrdinalIgnoreCase);

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
