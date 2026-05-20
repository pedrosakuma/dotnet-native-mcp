using BenchmarkDotNet.Attributes;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Xref;

namespace DotnetNativeMcp.Bench;

/// <summary>
/// Benchmarks for <c>find_native_callers</c> across three cache tiers:
/// <list type="bullet">
///   <item><description>Cold — fresh in-memory cache + no disk cache file.</description></item>
///   <item><description>WarmL2 — fresh in-memory cache, disk cache pre-populated.</description></item>
///   <item><description>WarmL1 — steady-state repeated call against in-memory cache.</description></item>
/// </list>
/// </summary>
[MemoryDiagnoser]
public class FindNativeCallersBench
{
    [Params("SampleAot", "SystemPrivateCoreLib")]
    public string Input { get; set; } = "SampleAot";

    private NativeImage? _image;
    private ulong _targetVa;
    private string? _diskCachePath;

    // Warm-L1 steady-state cache — kept across iterations.
    private NativeCallGraphCache? _l1Cache;

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
        {
            // Skip: fixture not present. Benchmarks will no-op via null guard.
            return;
        }

        var result = NativeImageLoader.Load(path);
        if (result.IsError || result.Data is null)
            return;

        _image = result.Data;

        _diskCachePath = NativeCallGraphDiskCache.GetCachePath(_image.Handle.BuildIdHex);

        // Build the call graph to warm the disk cache (L2) and select a target VA
        // that has at least one caller so the lookup is non-trivial.
        var warmupCache = new NativeCallGraphCache();
        var graph = warmupCache.GetOrBuild(_image);

        // Prefer a VA with callers; fall back to the first entry in the graph,
        // then to ImageBase so the benchmark always runs even on degenerate inputs.
        _targetVa = graph.FirstOrDefault(kvp => kvp.Value.Count > 0).Key;
        if (_targetVa == 0 && graph.Count > 0)
            _targetVa = graph.Keys.First();
        if (_targetVa == 0)
            _targetVa = _image.ImageBase;

        // Pre-warm in-memory cache (L1) used for steady-state tier.
        _l1Cache = new NativeCallGraphCache();
        _l1Cache.GetOrBuild(_image);
    }

    [GlobalCleanup]
    public static void Cleanup()
    {
        // Leave disk cache in place — it is the user's regular cache.
    }

    // -----------------------------------------------------------------------
    // Cold: no in-memory, no disk cache.
    // -----------------------------------------------------------------------

    [Benchmark(Description = "Cold (no L1, no L2)")]
    public IReadOnlyList<CallSite> Cold()
    {
        if (_image is null)
            return [];

        // Delete disk cache to force a full scan.
        if (_diskCachePath is not null && File.Exists(_diskCachePath))
            File.Delete(_diskCachePath);

        var cache = new NativeCallGraphCache();
        return cache.FindCallers(_image, _targetVa);
    }

    // -----------------------------------------------------------------------
    // Warm L2: fresh in-memory cache, disk cache present.
    // -----------------------------------------------------------------------

    [Benchmark(Description = "WarmL2 (no L1, disk cache)")]
    public IReadOnlyList<CallSite> WarmL2()
    {
        if (_image is null)
            return [];

        // Disk cache should already exist from GlobalSetup warmup.
        var cache = new NativeCallGraphCache();
        return cache.FindCallers(_image, _targetVa);
    }

    // -----------------------------------------------------------------------
    // Warm L1: steady-state in-memory cache.
    // -----------------------------------------------------------------------

    [Benchmark(Description = "WarmL1 (in-memory)")]
    public IReadOnlyList<CallSite> WarmL1()
    {
        if (_image is null || _l1Cache is null)
            return [];

        return _l1Cache.FindCallers(_image, _targetVa);
    }
}
