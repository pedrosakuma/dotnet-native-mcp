using System.Collections.Concurrent;
using DotnetNativeMcp.Core.Imaging;

namespace DotnetNativeMcp.Core.Xref;

/// <summary>
/// In-memory xref cache: one full call-graph index per loaded image.
/// Keyed by the image handle string. Thread-safe; the first
/// caller per image pays the build cost; subsequent callers get the cached result.
/// </summary>
public sealed class NativeCallGraphCache
{
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<ulong, IReadOnlyList<CallSite>>> _cache = new();

    /// <summary>
    /// Returns the xref index for <paramref name="image"/>, building it on the first call.
    /// </summary>
    public IReadOnlyDictionary<ulong, IReadOnlyList<CallSite>> GetOrBuild(NativeImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return _cache.GetOrAdd(image.Handle.Value, _ => NativeCallGraphBuilder.Build(image));
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
