using BenchmarkDotNet.Attributes;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Strings;

namespace DotnetNativeMcp.Bench;

/// <summary>
/// Benchmarks <c>extract_strings</c> end-to-end across all read-only data sections.
/// </summary>
[MemoryDiagnoser]
public class ExtractStringsBench
{
    private static readonly HashSet<string> RoDataSectionNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".rodata", ".rdata", ".data.rel.ro", "__const",
        };

    [Params("SampleAot", "SystemPrivateCoreLib")]
    public string Input { get; set; } = "SampleAot";

    private NativeImage? _image;
    private List<NativeSection> _sections = [];

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

        var roSections = _image.Sections
            .Where(s => RoDataSectionNames.Contains(s.Name))
            .ToList();

        // Fall back to .data when no read-only section was found.
        _sections = roSections.Count > 0
            ? roSections
            : _image.Sections.Where(s => s.Name is ".data").ToList();
    }

    [Benchmark(Description = "ExtractStrings end-to-end")]
    public int ExtractStrings()
    {
        if (_image is null || _sections.Count == 0)
            return 0;

        var total = 0;
        foreach (var section in _sections)
        {
            var bytes = _image.GetSectionBytes(section);
            foreach (var _ in StringExtractor.Extract(
                bytes.Span,
                section.VirtualAddress,
                section.Name,
                minLength: 6,
                ascii: true,
                utf16: true))
            {
                total++;
            }
        }

        return total;
    }
}
