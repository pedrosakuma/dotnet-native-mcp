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
/// </summary>
public sealed class NativeCallGraphCache
{
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<ulong, IReadOnlyList<CallSite>>> _cache = new();

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
}
