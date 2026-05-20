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

    private static IReadOnlyDictionary<ulong, IReadOnlyList<CallSite>> BuildWithDiskCache(NativeImage image)
    {
        if (NativeCallGraphDiskCache.IsEnabled)
        {
            var cachePath = NativeCallGraphDiskCache.GetCachePath(image.Handle.BuildIdHex);
            if (NativeCallGraphDiskCache.TryRead(cachePath, out var cached) && cached is not null)
                return cached;

            var built = NativeCallGraphBuilder.Build(image);
            NativeCallGraphDiskCache.Write(cachePath, built);
            return built;
        }

        return NativeCallGraphBuilder.Build(image);
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
    /// for call sites that resolve via PLT/IAT to the requested symbol.
    /// Results are cached per (callee, caller, symbol) tuple and persisted to disk lazily.
    /// </summary>
    /// <param name="calleeImage">The image that exports the target symbol.</param>
    /// <param name="targetSymbolName">The exported symbol name to search for in other images.</param>
    /// <param name="targetLibrary">Optional library qualifier (SONAME/DLL name). Pass <c>null</c> to match all.</param>
    /// <param name="registry">Registry of all loaded images.</param>
    /// <returns>All cross-image call sites found, possibly empty.</returns>
    public IReadOnlyList<CrossImageCallSite> FindCrossImageCallers(
        NativeImage calleeImage,
        string targetSymbolName,
        string? targetLibrary,
        INativeBinaryRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(calleeImage);
        ArgumentNullException.ThrowIfNull(targetSymbolName);
        ArgumentNullException.ThrowIfNull(registry);

        if (!IsCrossXrefEnabled)
            return [];

        var results = new List<CrossImageCallSite>();

        var cachePath = NativeCallGraphDiskCache.IsEnabled
            ? NativeCallGraphDiskCache.GetCachePath(calleeImage.Handle.BuildIdHex)
            : null;

        // Load any existing cross-refs from the disk cache for this callee image.
        Dictionary<string, IReadOnlyList<CrossImageCallSite>>? persistedCrossRefs = null;
        if (cachePath is not null)
            NativeCallGraphDiskCache.TryRead(cachePath, out _, out persistedCrossRefs);

        var newCrossRefs = new Dictionary<string, IReadOnlyList<CrossImageCallSite>>();
        var anyNewEntries = false;

        foreach (var callerImage in registry.List())
        {
            if (string.Equals(callerImage.Handle.Value, calleeImage.Handle.Value, StringComparison.OrdinalIgnoreCase))
                continue;

            var crossRefKey = NativeCallGraphDiskCache.MakeCrossRefKey(
                callerImage.Handle.BuildIdHex, targetLibrary, targetSymbolName);

            var sessionKey = $"{calleeImage.Handle.Value}|{crossRefKey}";

            // L1: in-process cache hit.
            if (_crossCache.TryGetValue(sessionKey, out var cached))
            {
                results.AddRange(cached);
                newCrossRefs[crossRefKey] = cached;
                continue;
            }

            // L2: disk cache hit — the cross-ref key already encodes the caller build-id,
            // so any stored value (including an empty list) is valid for the current caller.
            if (persistedCrossRefs is not null &&
                persistedCrossRefs.TryGetValue(crossRefKey, out var persisted))
            {
                _crossCache[sessionKey] = persisted;
                results.AddRange(persisted);
                newCrossRefs[crossRefKey] = persisted;
                continue;
            }

            // Not cached — scan the caller image.
            var callerGraph = GetOrBuild(callerImage);
            var found = CrossImageCallGraphScanner.FindCallers(
                callerImage, callerGraph, targetSymbolName, targetLibrary);

            _crossCache[sessionKey] = found;
            newCrossRefs[crossRefKey] = found;
            anyNewEntries = true;

            results.AddRange(found);
        }

        // Persist new cross-ref entries lazily alongside the callee's same-image cache.
        // Merge with any previously persisted cross-refs so entries for callers that are
        // no longer loaded this session are not silently dropped.
        if (anyNewEntries && cachePath is not null && NativeCallGraphDiskCache.IsEnabled)
        {
            var sameImage = _cache.TryGetValue(calleeImage.Handle.Value, out var si) ? si
                : new Dictionary<ulong, IReadOnlyList<CallSite>>();

            var mergedCrossRefs = new Dictionary<string, IReadOnlyList<CrossImageCallSite>>();
            if (persistedCrossRefs is not null)
                foreach (var (k, v) in persistedCrossRefs)
                    mergedCrossRefs[k] = v;
            foreach (var (k, v) in newCrossRefs)
                mergedCrossRefs[k] = v;

            NativeCallGraphDiskCache.Write(cachePath, sameImage, mergedCrossRefs);
        }

        return results;
    }
}
