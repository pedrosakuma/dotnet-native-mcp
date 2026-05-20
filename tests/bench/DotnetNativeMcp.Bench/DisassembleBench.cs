using BenchmarkDotNet.Attributes;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Disassembly;
using DotnetNativeMcp.Core.Imaging;

namespace DotnetNativeMcp.Bench;

/// <summary>
/// Benchmarks <c>disassemble</c> over a 64-instruction window
/// for each fixture input.
/// </summary>
[MemoryDiagnoser]
public class DisassembleBench
{
    [Params("SampleAot", "SystemPrivateCoreLib")]
    public string Input { get; set; } = "SampleAot";

    private NativeImage? _image;
    private ulong _startRva;

    [GlobalSetup]
    public void Setup()
    {
        var path = Input switch
        {
            "SampleAot" => BenchFixturePaths.SampleAot,
            "SystemPrivateCoreLib" => BenchFixturePaths.SystemPrivateCoreLib,
            _ => null,
        };

        if (path is null || !File.Exists(path))
            return;

        var result = NativeImageLoader.Load(path);
        if (result.IsError || result.Data is null)
            return;

        _image = result.Data;

        // Use the first executable section's start RVA so we always land in valid code.
        var execSection = _image.Sections.FirstOrDefault(s =>
            s.Name is ".text" or "__text" or "code");
        _startRva = execSection?.VirtualAddress ?? 0;
    }

    [Benchmark(Description = "Disassemble 64 instructions")]
    public int Disassemble()
    {
        if (_image is null)
            return 0;

        var result = IcedDisassembler.Disassemble(_image, _startRva, maxInstructions: 64);
        return result.IsError ? 0 : result.Data!.Count;
    }
}
