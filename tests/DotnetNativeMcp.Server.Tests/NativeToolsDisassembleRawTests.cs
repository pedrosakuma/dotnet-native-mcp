using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Disassembly;
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
    public void Disassemble_ImagePath_ReadyToRunSystemPrivateCoreLib_ReturnsInstructions()
    {
        var (fixturePath, rva) = FindModernSystemPrivateCoreLibDisassemblyTarget();
        if (fixturePath is null || rva is null) return;

        var tools = EmptyTools();

        var result = tools.Disassemble(
            imagePath: fixturePath,
            rva: rva.Value,
            size: 64,
            maxInstructions: 10);

        result.IsError.Should().BeFalse(result.Error?.Message ?? string.Empty);
        result.Data.Should().NotBeEmpty();
        result.Data!.Should().OnlyContain(instruction => instruction.Mnemonic != "unknown");
    }

    [Fact]
    public void Disassemble_ImagePath_NonR2RManagedPe_PreservesUnsupportedError()
    {
        var selfPath = typeof(NativeToolsDisassembleRawTests).Assembly.Location;
        var patchedPath = CreatePatchedCeeAssembly(selfPath, "native-tools-cee.dll");
        var textRva = patchedPath is null ? null : FindTextSectionRva(patchedPath);
        if (patchedPath is null || textRva is null) return;

        var tools = EmptyTools();

        var result = tools.Disassemble(
            imagePath: patchedPath,
            rva: textRva.Value,
            size: 64,
            maxInstructions: 10);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.DisassemblyUnsupported);
        result.Error.Message.Should().Contain("Unknown");
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

    private static (string? Path, int? Rva) FindModernSystemPrivateCoreLibDisassemblyTarget()
    {
        const string ExactRuntimePath = "/home/pedrotravi/.dotnet/shared/Microsoft.NETCore.App/10.0.5/System.Private.CoreLib.dll";
        const int ExactRuntimeRva = 0x23CCA0;

        if (File.Exists(ExactRuntimePath))
            return (ExactRuntimePath, ExactRuntimeRva);

        const string SharedRuntimeRoot = "/home/pedrotravi/.dotnet/shared/Microsoft.NETCore.App";
        if (Directory.Exists(SharedRuntimeRoot))
        {
            foreach (var runtimeDir in Directory.GetDirectories(SharedRuntimeRoot)
                         .Select(path => new DirectoryInfo(path))
                         .Where(dir => Version.TryParse(dir.Name, out var version) && version.Major >= 8)
                         .OrderByDescending(dir => Version.Parse(dir.Name)))
            {
                var candidate = Path.Combine(runtimeDir.FullName, "System.Private.CoreLib.dll");
                if (File.Exists(candidate))
                    return (candidate, FindFirstReadyToRunMethodRva(candidate) ?? FindTextSectionRva(candidate));
            }
        }

        var dir = Path.GetDirectoryName(typeof(NativeToolsDisassembleRawTests).Assembly.Location);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "DotnetNativeMcp.slnx")))
            {
                var candidate = Path.Combine(
                    dir,
                    "tests", "fixtures", "SampleAot",
                    "bin", "Release", "net10.0", "linux-x64",
                    "System.Private.CoreLib.dll");
                if (File.Exists(candidate))
                    return (candidate, FindFirstReadyToRunMethodRva(candidate) ?? FindTextSectionRva(candidate));
            }

            dir = Path.GetDirectoryName(dir);
        }

        return (null, null);
    }

    private static int? FindFirstReadyToRunMethodRva(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var image = PeNativeReader.Read(new ReadOnlyMemory<byte>(bytes), path);
        if (image is null)
            return null;

        var headerResult = ReadyToRunReader.ReadHeader(image);
        if (headerResult.IsError)
            return null;

        var runtimeFunctions = headerResult.Data!.FindSection(ReadyToRunSectionType.RuntimeFunctions);
        var fileOffset = runtimeFunctions is null ? null : image.RvaToFileOffset(runtimeFunctions.VirtualAddress);
        if (fileOffset is null || fileOffset.Value + 12 > bytes.Length)
            return null;

        return checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(fileOffset.Value)));
    }

    private static int? FindTextSectionRva(string path)
    {
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var peReader = new PEReader(stream, PEStreamOptions.Default);
            var textSection = peReader.PEHeaders.SectionHeaders.FirstOrDefault(section => section.Name == ".text");
            return textSection.Name is null ? null : textSection.VirtualAddress;
        }
        catch
        {
            return null;
        }
    }

    private static string? CreatePatchedCeeAssembly(string sourcePath, string fileName)
    {
        try
        {
            var bytes = File.ReadAllBytes(sourcePath);
            if (bytes.Length < 0x40)
                return null;

            var peOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x3C));
            if (peOffset <= 0 || peOffset + 6 > bytes.Length)
                return null;

            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOffset + 4), 0xFD1D);

            var scratchDir = Path.Combine(Path.GetDirectoryName(sourcePath)!, "scratch");
            Directory.CreateDirectory(scratchDir);

            var patchedPath = Path.Combine(scratchDir, fileName);
            File.WriteAllBytes(patchedPath, bytes);
            return patchedPath;
        }
        catch
        {
            return null;
        }
    }

    private sealed class TestBinaryRegistry(params NativeImage[] images) : INativeBinaryRegistry
    {
        private readonly Dictionary<string, NativeImage> _images =
            images.ToDictionary(img => img.Handle.Value, StringComparer.OrdinalIgnoreCase);

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
