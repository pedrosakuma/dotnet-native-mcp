using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using DotnetNativeMcp.Core.Disassembly;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.R2R;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class RawDisassemblerTests
{
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

    [Fact]
    public void Disassemble_RvaOutOfRange_ReturnsAddressOutOfRange()
    {
        var fixturePath = FixturePaths.SampleAot;
        if (fixturePath is null) return;

        var result = RawDisassembler.Disassemble(
            fixturePath, rva: 0x7FFF0000, size: 64,
            arch: null, baseAddress: null,
            maxInstructions: 10);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.AddressOutOfRange);
    }

    [Fact]
    public void Disassemble_SampleAotTextSection_ReturnsAtLeastOneInstruction()
    {
        var fixturePath = FixturePaths.SampleAot;
        if (fixturePath is null) return;

        var result = RawDisassembler.Disassemble(
            fixturePath, rva: 0x58c0, size: 64,
            arch: null, baseAddress: null,
            maxInstructions: 32);

        result.IsError.Should().BeFalse(result.Error?.Message ?? string.Empty);
        result.Data.Should().NotBeEmpty();
    }

    [Fact]
    public void Disassemble_ReadyToRunSystemPrivateCoreLib_InfersX64Architecture()
    {
        var (fixturePath, rva) = FindModernSystemPrivateCoreLibDisassemblyTarget();
        if (fixturePath is null || rva is null) return;

        var result = RawDisassembler.Disassemble(
            fixturePath, rva.Value, size: 64,
            arch: null, baseAddress: null,
            maxInstructions: 16);

        result.IsError.Should().BeFalse(result.Error?.Message ?? string.Empty);
        result.Data.Should().NotBeEmpty();
        result.Data!.Should().OnlyContain(instruction => instruction.Mnemonic != "unknown");
    }

    [Fact]
    public void Disassemble_NonR2RManagedPe_PreservesUnsupportedError()
    {
        var selfPath = typeof(RawDisassemblerTests).Assembly.Location;
        var patchedPath = CreatePatchedCeeAssembly(selfPath, "raw-disassembler-cee.dll");
        var textRva = patchedPath is null ? null : FindTextSectionRva(patchedPath);
        if (patchedPath is null || textRva is null) return;

        var result = RawDisassembler.Disassemble(
            patchedPath, textRva.Value, size: 64,
            arch: null, baseAddress: null,
            maxInstructions: 10);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.DisassemblyUnsupported);
        result.Error.Message.Should().Contain("Unknown");
    }

    [Fact]
    public void Disassemble_WithExplicitArchOverride_DecodesCorrectly()
    {
        var fixturePath = FixturePaths.SampleAot;
        if (fixturePath is null) return;

        var result = RawDisassembler.Disassemble(
            fixturePath, rva: 0x58c0, size: 32,
            arch: Architecture.X64, baseAddress: null,
            maxInstructions: 10);

        result.IsError.Should().BeFalse();
        result.Data.Should().NotBeEmpty();
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

        var fixturePath = FixturePaths.SystemPrivateCoreLib;
        return fixturePath is null
            ? (null, null)
            : (fixturePath, FindFirstReadyToRunMethodRva(fixturePath) ?? FindTextSectionRva(fixturePath));
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
}
