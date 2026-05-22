using System.Collections.Concurrent;
using DotnetNativeMcp.Core.Imaging;

namespace DotnetNativeMcp.Core.Xref;

/// <summary>
/// Two-level xref cache for native call-graph indexes.
/// <list type="bullet">
///   <item><description>L1: in-process <see cref="ConcurrentDictionary{TKey,TValue}"/> — O(1) on subsequent calls within the same session.</description></item>
///   <item><description>L2: persistent on-disk cache under <c>~/.cache/dotnet-native-mcp/&lt;build-id&gt;.xref</c> — survives across sessions so large NativeAOT binaries pay the scan cost only once.
///     Controlled by <see cref="NativeCallGraphDiskCache"/>; set <c>DOTNET_NATIVE_MCP_XREF_CACHE=0</c> to disable.</description></item>
/// </list>
/// Cross-image call-graph entries are built lazily on first query and stored in the callee
/// image's <c>.xref</c> cache file under a <c>crossRefs</c> section.
/// Set <c>DOTNET_NATIVE_MCP_CROSS_XREF=0</c> to disable cross-image scanning entirely.
/// </summary>
public sealed class NativeCallGraphCache
{
    /// <summary>Same-image call graph: imageHandle → (targetVA → callers).</summary>
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<ulong, IReadOnlyList<CallSite>>> _cache = new();

    /// <summary>
    /// Cross-image results already computed this session, keyed by
    /// <c>{calleeHandle}|{crossRefKey}</c> to avoid rescanning on repeated queries.
    /// </summary>
    private readonly ConcurrentDictionary<string, IReadOnlyList<CrossImageCallSite>> _crossCache = new();

    /// <summary>Lazy Mach-O stub/export metadata keyed by image handle.</summary>
    private readonly ConcurrentDictionary<string, MachOCrossImageMetadata> _machOCache = new();

    /// <summary>
    /// Returns <see langword="true"/> when cross-image scanning is permitted.
    /// Controlled by <c>DOTNET_NATIVE_MCP_CROSS_XREF</c>: set to <c>"0"</c> to disable.
    /// </summary>
    public static bool IsCrossXrefEnabled =>
        !string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_NATIVE_MCP_CROSS_XREF"),
            "0",
            StringComparison.Ordinal);

    /// <summary>
    /// Returns the xref index for <paramref name="image"/>, consulting the disk cache before
    /// scanning the binary. The result is stored in both L1 and L2 on a fresh build.
    /// </summary>
    public IReadOnlyDictionary<ulong, IReadOnlyList<CallSite>> GetOrBuild(NativeImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return _cache.GetOrAdd(image.Handle.Value, _ => BuildWithDiskCache(image));
    }

    public IReadOnlyDictionary<string, ulong> GetOrBuildMachOExports(NativeImage image)
        => GetOrBuildMachOMetadata(image).Exports;

    public IReadOnlyDictionary<ulong, string> GetOrBuildMachOStubTargets(NativeImage image)
        => GetOrBuildMachOMetadata(image).StubTargets;

    private MachOCrossImageMetadata GetOrBuildMachOMetadata(NativeImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Format != BinaryFormat.MachO)
            return MachOCrossImageMetadata.Empty;

        return _machOCache.GetOrAdd(image.Handle.Value, _ => BuildMachOMetadataWithDiskCache(image));
    }

    private IReadOnlyDictionary<ulong, IReadOnlyList<CallSite>> BuildWithDiskCache(NativeImage image)
    {
        if (!NativeCallGraphDiskCache.IsEnabled)
            return NativeCallGraphBuilder.Build(image);

        var cachePath = NativeCallGraphDiskCache.GetCachePath(image.Handle.BuildIdHex);
        if (NativeCallGraphDiskCache.TryRead(cachePath, out var cached, out _, out var machO) && cached is not null)
        {
            if (machO is not null)
                _machOCache.TryAdd(image.Handle.Value, machO);
            return cached;
        }

        var built = NativeCallGraphBuilder.Build(image);
        MachOCrossImageMetadata? builtMachO = null;
        if (image.Format == BinaryFormat.MachO)
        {
            builtMachO = MachOReader.BuildCrossImageMetadata(image);
            _machOCache[image.Handle.Value] = builtMachO;
        }

        NativeCallGraphDiskCache.Write(cachePath, built, null, builtMachO);
        return built;
    }

    private MachOCrossImageMetadata BuildMachOMetadataWithDiskCache(NativeImage image)
    {
        if (!NativeCallGraphDiskCache.IsEnabled)
            return MachOReader.BuildCrossImageMetadata(image);

        var cachePath = NativeCallGraphDiskCache.GetCachePath(image.Handle.BuildIdHex);
        if (NativeCallGraphDiskCache.TryRead(cachePath, out var sameImage, out var crossRefs, out var machO))
        {
            if (sameImage is not null)
                _cache.TryAdd(image.Handle.Value, sameImage);
            if (machO is not null)
                return machO;
            if (crossRefs is not null)
            {
                foreach (var (key, value) in crossRefs)
                    _crossCache.TryAdd($"{image.Handle.Value}|{key}", value);
            }
        }

        var built = MachOReader.BuildCrossImageMetadata(image);
        var sameImageToPersist = _cache.TryGetValue(image.Handle.Value, out var inMemorySameImage)
            ? inMemorySameImage
            : sameImage;

        if (sameImageToPersist is null)
        {
            sameImageToPersist = NativeCallGraphBuilder.Build(image);
            _cache.TryAdd(image.Handle.Value, sameImageToPersist);
        }

        NativeCallGraphDiskCache.Write(cachePath, sameImageToPersist, crossRefs, built);
        return built;
    }

    /// <summary>
    /// Looks up all <see cref="CallSite"/>s that target <paramref name="targetVa"/>.
    /// Returns an empty list when no callers are found.
    /// </summary>
    public IReadOnlyList<CallSite> FindCallers(NativeImage image, ulong targetVa)
    {
        var index = GetOrBuild(image);
        return index.TryGetValue(targetVa, out var callers) ? callers : [];
    }

    /// <summary>
    /// Scans all images in <paramref name="registry"/> (except <paramref name="calleeImage"/>)
    /// for call sites that resolve via PLT/IAT/stubs to the requested symbol.
    /// Results are cached per (callee, caller, symbol) tuple and persisted to disk lazily.
    /// </summary>
    /// <param name="calleeImage">The image that exports the target symbol.</param>
    /// <param name="targetSymbolName">The exported symbol name to search for in other images.</param>
    /// <param name="targetLibrary">Optional library qualifier (SONAME/DLL name). Pass <c>null</c> to match all.</param>
    /// <param name="registry">Registry of all loaded images.</param>
    /// <param name="maxResults">Hard cap on the aggregate cross-image result list; further callers are skipped once reached.</param>
    /// <returns>All cross-image call sites found, possibly empty.</returns>
    public IReadOnlyList<CrossImageCallSite> FindCrossImageCallers(
        NativeImage calleeImage,
        string targetSymbolName,
        string? targetLibrary,
        INativeBinaryRegistry registry,
        int maxResults = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(calleeImage);
        ArgumentNullException.ThrowIfNull(targetSymbolName);
        ArgumentNullException.ThrowIfNull(registry);

        if (!IsCrossXrefEnabled)
            return [];
        if (maxResults <= 0)
            return [];

        var results = new List<CrossImageCallSite>();

        var cachePath = NativeCallGraphDiskCache.IsEnabled
            ? NativeCallGraphDiskCache.GetCachePath(calleeImage.Handle.BuildIdHex)
            : null;

        IReadOnlyDictionary<ulong, IReadOnlyList<CallSite>>? persistedSameImage = null;
        Dictionary<string, IReadOnlyList<CrossImageCallSite>>? persistedCrossRefs = null;
        MachOCrossImageMetadata? calleeMachO = null;
        if (cachePath is not null)
        {
            NativeCallGraphDiskCache.TryRead(cachePath, out persistedSameImage, out persistedCrossRefs, out calleeMachO);
            if (persistedSameImage is not null)
                _cache.TryAdd(calleeImage.Handle.Value, persistedSameImage);
            if (calleeMachO is not null)
                _machOCache.TryAdd(calleeImage.Handle.Value, calleeMachO);
        }

        calleeMachO ??= calleeImage.Format == BinaryFormat.MachO ? GetOrBuildMachOMetadata(calleeImage) : null;

        var newCrossRefs = new Dictionary<string, IReadOnlyList<CrossImageCallSite>>();
        var anyNewEntries = false;

        foreach (var callerImage in registry.List())
        {
            if (results.Count >= maxResults)
                break;
            if (string.Equals(callerImage.Handle.Value, calleeImage.Handle.Value, StringComparison.OrdinalIgnoreCase))
                continue;

            var crossRefKey = NativeCallGraphDiskCache.MakeCrossRefKey(
                callerImage.Handle.BuildIdHex, targetLibrary, targetSymbolName);

            var sessionKey = $"{calleeImage.Handle.Value}|{crossRefKey}";

            if (_crossCache.TryGetValue(sessionKey, out var cached))
            {
                AddCappedRange(results, cached, maxResults);
                newCrossRefs[crossRefKey] = cached;
                continue;
            }

            if (persistedCrossRefs is not null &&
                persistedCrossRefs.TryGetValue(crossRefKey, out var persisted))
            {
                _crossCache[sessionKey] = persisted;
                AddCappedRange(results, persisted, maxResults);
                newCrossRefs[crossRefKey] = persisted;
                continue;
            }

            var callerGraph = GetOrBuild(callerImage);
            var callerMachO = callerImage.Format == BinaryFormat.MachO ? GetOrBuildMachOMetadata(callerImage) : null;
            IReadOnlyDictionary<string, ulong>? calleeExports = calleeMachO?.Exports;
            IReadOnlyDictionary<ulong, string>? callerStubs = callerMachO?.StubTargets;
            var remaining = Math.Max(0, maxResults - results.Count);
            var found = CrossImageCallGraphScanner.FindCallers(
                callerImage: callerImage,
                callerGraph: callerGraph,
                targetSymbolName: targetSymbolName,
                targetLibrary: targetLibrary,
                calleeMachOExports: calleeExports,
                callerMachOStubs: callerStubs,
                maxResults: remaining);

            // Only cache/persist results that ran to completion. When the scanner returned exactly the
            // remaining budget we may have truncated mid-stream, so don't poison the cache with a partial set.
            var possiblyTruncated = remaining != int.MaxValue && found.Count >= remaining;
            if (!possiblyTruncated)
            {
                _crossCache[sessionKey] = found;
                newCrossRefs[crossRefKey] = found;
                anyNewEntries = true;
            }

            AddCappedRange(results, found, maxResults);
        }

        if (anyNewEntries && cachePath is not null && NativeCallGraphDiskCache.IsEnabled)
        {
            var sameImage = _cache.TryGetValue(calleeImage.Handle.Value, out var si) ? si : persistedSameImage;
            if (sameImage is null)
            {
                sameImage = NativeCallGraphBuilder.Build(calleeImage);
                _cache.TryAdd(calleeImage.Handle.Value, sameImage);
            }

            var mergedCrossRefs = new Dictionary<string, IReadOnlyList<CrossImageCallSite>>();
            if (persistedCrossRefs is not null)
                foreach (var (k, v) in persistedCrossRefs)
                    mergedCrossRefs[k] = v;
            foreach (var (k, v) in newCrossRefs)
                mergedCrossRefs[k] = v;

            NativeCallGraphDiskCache.Write(cachePath, sameImage, mergedCrossRefs, calleeMachO);
        }

        return results;
    }

    private static void AddCappedRange(
        List<CrossImageCallSite> destination,
        IReadOnlyList<CrossImageCallSite> source,
        int maxResults)
    {
        var capacity = maxResults - destination.Count;
        if (capacity <= 0) return;
        if (source.Count <= capacity)
        {
            destination.AddRange(source);
            return;
        }
        for (var i = 0; i < capacity; i++)
            destination.Add(source[i]);
    }
}
