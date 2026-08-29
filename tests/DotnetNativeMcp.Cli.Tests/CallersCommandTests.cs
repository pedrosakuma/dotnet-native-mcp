using System.Buffers.Binary;
using System.Text;
using DotnetNativeMcp.Cli.Output;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Security;
using DotnetNativeMcp.Core.Symbols;
using DotnetNativeMcp.Core.Xref;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Cli.Tests;

public sealed class CallersCommandTests
{
    [Fact]
    public void FindCallers_SameImageAddress_ReturnsCallerRow()
    {
        var image = CreateSameImageCaller();
        var registry = new StaticRegistry((CanonicalPath(ImagePath), image));

        var result = CallersCommandFactory.FindCallers(
            registry,
            new NativeCallGraphCache(),
            new SourceResolver(),
            PathAccessPolicy.Permissive,
            ImagePath,
            "0x40000a",
            []);

        result.IsError.Should().BeFalse(result.Error?.Message ?? string.Empty);
        result.Data!.TotalCallers.Should().Be(1);
        result.Data.Callers.Should().ContainSingle();
        result.Data.Callers[0].SourceAddressHex.Should().Be("0000000000400000");
        result.Data.Callers[0].CallerImagePath.Should().Be(image.FilePath);
        result.Data.Callers[0].IsCrossImage.Should().BeFalse();
    }

    [Fact]
    public void FindCallers_WithCandidateImage_ReturnsCrossImageCaller()
    {
        var previousCacheSetting = Environment.GetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE", "0");

            var calleeImage = MakeCalleeImage();
            var callerImage = ElfReader.Read(new ReadOnlyMemory<byte>(BuildCrossImageCallerElf()), CanonicalPath(CallerImagePath))!;
            var registry = new StaticRegistry(
                (CanonicalPath(CalleeImagePath), calleeImage),
                (CanonicalPath(CallerImagePath), callerImage));

            var result = CallersCommandFactory.FindCallers(
                registry,
                new NativeCallGraphCache(),
                new SourceResolver(),
                PathAccessPolicy.Permissive,
                CalleeImagePath,
                "0x100",
                [CallerImagePath]);

            result.IsError.Should().BeFalse(result.Error?.Message ?? string.Empty);
            result.Data!.Callers.Should().ContainSingle();
            result.Data.Callers[0].IsCrossImage.Should().BeTrue();
            result.Data.Callers[0].CallerImagePath.Should().Be(CanonicalPath(CallerImagePath));
            result.Data.Callers[0].SourceAddressHex.Should().Be("0000000000002000");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE", previousCacheSetting);
        }
    }

    [Fact]
    public void FindCallers_InteriorAddress_DoesNotProduceCrossImageMatches()
    {
        var previousCacheSetting = Environment.GetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE", "0");

            var calleeImage = MakeCalleeImage();
            var callerImage = ElfReader.Read(new ReadOnlyMemory<byte>(BuildCrossImageCallerElf()), CanonicalPath(CallerImagePath))!;
            var registry = new StaticRegistry(
                (CanonicalPath(CalleeImagePath), calleeImage),
                (CanonicalPath(CallerImagePath), callerImage));

            var result = CallersCommandFactory.FindCallers(
                registry,
                new NativeCallGraphCache(),
                new SourceResolver(),
                PathAccessPolicy.Permissive,
                CalleeImagePath,
                "0x104",
                [CallerImagePath]);

            result.IsError.Should().BeFalse(result.Error?.Message ?? string.Empty);
            result.Data!.TotalCallers.Should().Be(0);
            result.Data.Callers.Should().BeEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE", previousCacheSetting);
        }
    }

    [Fact]
    public async Task FindCallers_TableOutput_RendersCallerColumns()
    {
        var image = CreateSameImageCaller();
        var registry = new StaticRegistry((CanonicalPath(ImagePath), image));
        var result = CallersCommandFactory.FindCallers(
            registry,
            new NativeCallGraphCache(),
            new SourceResolver(),
            PathAccessPolicy.Permissive,
            ImagePath,
            "0x40000a",
            []);

        var writer = new StringWriter();
        await new TableOutputWriter(writer).WriteAsync(result);

        var output = writer.ToString();
        output.Should().Contain("caller-address");
        output.Should().Contain("caller-symbol");
        output.Should().Contain("caller-image");
        output.Should().Contain("0x0000000000400000");
    }

    [Fact]
    public async Task FindCallers_JsonOutput_PreservesCallerPayload()
    {
        var image = CreateSameImageCaller();
        var registry = new StaticRegistry((CanonicalPath(ImagePath), image));
        var result = CallersCommandFactory.FindCallers(
            registry,
            new NativeCallGraphCache(),
            new SourceResolver(),
            PathAccessPolicy.Permissive,
            ImagePath,
            "0x40000a",
            []);

        var writer = new StringWriter();
        await new JsonOutputWriter(writer).WriteAsync(result);

        var output = writer.ToString();
        output.Should().Contain("\"targetAddressHex\"");
        output.Should().Contain("\"callers\"");
        output.Should().Contain("\"callerImagePath\"");
    }

    [Fact]
    public void FindCallers_PathPolicyDenial_ReturnsPathNotAllowedWithoutLoading()
    {
        var allowedRoot = Path.Combine(AppContext.BaseDirectory, "allowed-root-callers");
        var outsidePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "outside-root", "fixture.so"));
        Directory.CreateDirectory(allowedRoot);

        var registry = new StaticRegistry();
        var policy = new PathAccessPolicy([allowedRoot], enforcing: true);

        var result = CallersCommandFactory.FindCallers(
            registry,
            new NativeCallGraphCache(),
            new SourceResolver(),
            policy,
            outsidePath,
            "0x1000",
            []);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.PathNotAllowed);
        registry.LoadCalls.Should().Be(0);
    }

    private const string ImagePath = "/virtual/same-image.so";
    private const string CalleeImagePath = "/virtual/libcallee.so";
    private const string CallerImagePath = "/virtual/caller.so";

    private static NativeImage CreateSameImageCaller()
    {
        var code = new byte[]
        {
            0xE8, 0x05, 0x00, 0x00, 0x00,
            0x90, 0x90, 0x90, 0x90, 0x90,
            0xC3,
        };

        return new NativeImage(
            ImageHandle.From("testfc-cli", Path.GetFileName(ImagePath)),
            CanonicalPath(ImagePath),
            BinaryFormat.Elf,
            Architecture.X64,
            [new NativeSection(".text", 0, (ulong)code.Length, 0, (ulong)code.Length)],
            [new NativeSymbol(0, "my_target", "my_target", 10, 1, ".text", true)],
            new ReadOnlyMemory<byte>(code),
            0x400000);
    }

    private static NativeImage MakeCalleeImage()
    {
        var handle = ImageHandle.From("deadbeef01", Path.GetFileName(CalleeImagePath));
        var symbol = new NativeSymbol(0, "lib_func", "lib_func", 0x100, 16, ".text", true);
        var section = new NativeSection(".text", 0x100, 16, 0x100, 16);
        return new NativeImage(handle, CanonicalPath(CalleeImagePath), BinaryFormat.Elf, Architecture.X64, [section], [symbol], ReadOnlyMemory<byte>.Empty, 0);
    }

    private static string CanonicalPath(string path) => Path.GetFullPath(path);

    private static byte[] BuildCrossImageCallerElf()
    {
        const ulong pltSecVa = 0x1000UL;
        const ulong textVa = 0x2000UL;

        var rel32 = unchecked((int)(pltSecVa - (textVa + 5)));
        byte[] call = [0xE8, .. BitConverter.GetBytes(rel32)];

        byte[] dynstr = [0x00, 0x6C, 0x69, 0x62, 0x5F, 0x66, 0x75, 0x6E, 0x63, 0x00];

        var dynsym = new byte[48];
        BinaryPrimitives.WriteUInt32LittleEndian(dynsym.AsSpan(24), 1);
        dynsym[28] = 0x12;
        BinaryPrimitives.WriteUInt16LittleEndian(dynsym.AsSpan(30), 0);

        var relaPlt = new byte[24];
        BinaryPrimitives.WriteUInt64LittleEndian(relaPlt.AsSpan(0), 0x3018UL);
        BinaryPrimitives.WriteUInt64LittleEndian(relaPlt.AsSpan(8), (1UL << 32) | 7);

        byte[] pltSec = new byte[16];
        Array.Fill(pltSec, (byte)0x90);
        byte[] text = call;

        const int shStrNameDynstr = 1;
        const int shStrNameDynsym = 9;
        const int shStrNameRelaPlt = 17;
        const int shStrNamePltSec = 27;
        const int shStrNameText = 36;
        const int shStrNameShstrtab = 42;
        byte[] shstrtab = Encoding.ASCII.GetBytes(
            "\0.dynstr\0.dynsym\0.rela.plt\0.plt.sec\0.text\0.shstrtab\0");

        const int offDynstr = 0x0100;
        const int offDynsym = 0x0120;
        const int offRelaPlt = 0x0150;
        const int offPltSec = 0x0180;
        const int offText = 0x0200;
        const int offShstrtab = 0x0220;
        const int offShdr = 0x0280;

        const int shNum = 7;
        const int shEntSize = 64;
        const int totalSize = offShdr + shNum * shEntSize;

        var file = new byte[totalSize];

        file[0] = 0x7F; file[1] = (byte)'E'; file[2] = (byte)'L'; file[3] = (byte)'F';
        file[4] = 2;
        file[5] = 1;
        file[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(16), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(18), 0x3E);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(40), offShdr);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(52), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(58), shEntSize);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(60), shNum);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(62), 6);

        dynstr.CopyTo(file, offDynstr);
        dynsym.CopyTo(file, offDynsym);
        relaPlt.CopyTo(file, offRelaPlt);
        pltSec.CopyTo(file, offPltSec);
        text.CopyTo(file, offText);
        shstrtab.CopyTo(file, offShstrtab);

        void WriteShdr(int idx, uint nameOff, uint type, ulong flags, ulong addr,
            ulong off, ulong size, uint link, uint info, ulong align, ulong entSize)
        {
            var pos = offShdr + idx * shEntSize;
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(pos + 0), nameOff);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(pos + 4), type);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(pos + 8), flags);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(pos + 16), addr);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(pos + 24), off);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(pos + 32), size);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(pos + 40), link);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(pos + 44), info);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(pos + 48), align);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(pos + 56), entSize);
        }

        const uint SHT_NULL = 0;
        const uint SHT_PROGBITS = 1;
        const uint SHT_DYNSYM = 11;
        const uint SHT_STRTAB = 3;
        const uint SHT_RELA = 4;
        const ulong SHF_ALLOC = 2;
        const ulong SHF_EXECINSTR = 4;

        WriteShdr(0, 0, SHT_NULL, 0, 0, 0, 0, 0, 0, 0, 0);
        WriteShdr(1, shStrNameDynstr, SHT_STRTAB, SHF_ALLOC, 0, offDynstr, (ulong)dynstr.Length, 0, 0, 1, 0);
        WriteShdr(2, shStrNameDynsym, SHT_DYNSYM, SHF_ALLOC, 0, offDynsym, (ulong)dynsym.Length, 1, 1, 8, 24);
        WriteShdr(3, shStrNameRelaPlt, SHT_RELA, SHF_ALLOC, 0, offRelaPlt, (ulong)relaPlt.Length, 2, 4, 8, 24);
        WriteShdr(4, shStrNamePltSec, SHT_PROGBITS, SHF_ALLOC | SHF_EXECINSTR, pltSecVa, offPltSec, (ulong)pltSec.Length, 0, 0, 16, 16);
        WriteShdr(5, shStrNameText, SHT_PROGBITS, SHF_ALLOC | SHF_EXECINSTR, textVa, offText, (ulong)text.Length, 0, 0, 16, 0);
        WriteShdr(6, shStrNameShstrtab, SHT_STRTAB, 0, 0, offShstrtab, (ulong)shstrtab.Length, 0, 0, 1, 0);

        return file;
    }

    private sealed class StaticRegistry(params (string Path, NativeImage Image)[] images) : INativeBinaryRegistry
    {
        private readonly Dictionary<string, NativeImage> _byPath = images.ToDictionary(
            pair => pair.Path,
            pair => pair.Image,
            StringComparer.OrdinalIgnoreCase);

        public int LoadCalls { get; private set; }

        public DotnetNativeMcp.Core.NativeResult<NativeImage> Load(string path, string? expectedBuildId = null)
        {
            LoadCalls++;

            if (_byPath.TryGetValue(path, out var image))
            {
                return DotnetNativeMcp.Core.NativeResult.Ok("loaded", image);
            }

            return DotnetNativeMcp.Core.NativeResult.Fail<NativeImage>(ErrorKinds.BinaryNotFound, $"Binary not found: '{Path.GetFileName(path)}'.");
        }

        public DotnetNativeMcp.Core.NativeResult<string> RegisterHint(string path, string? buildId = null) =>
            DotnetNativeMcp.Core.NativeResult.Ok("registered", path);

        public bool TryGet(string imageHandle, out NativeImage? image)
        {
            image = _byPath.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.Handle.Value, imageHandle, StringComparison.OrdinalIgnoreCase));
            return image is not null;
        }

        public IReadOnlyList<NativeImage> List() => [.. _byPath.Values];
    }
}
