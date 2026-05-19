using System.Collections.Concurrent;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;

namespace DotnetNativeMcp.Core.Imaging;

/// <summary>Thread-safe registry of loaded <see cref="NativeImage"/> instances.</summary>
public interface INativeBinaryRegistry
{
    /// <summary>Loads a binary from disk (or returns the cached instance if already loaded).</summary>
    NativeResult<NativeImage> Load(string path, string? expectedBuildId = null);

    /// <summary>Attempts to retrieve a previously loaded image by handle string.</summary>
    bool TryGet(string imageHandle, out NativeImage? image);

    /// <summary>Returns all currently loaded images.</summary>
    IReadOnlyList<NativeImage> List();
}

/// <summary>
/// Singleton implementation of <see cref="INativeBinaryRegistry"/>.
/// Caches images by both <see cref="Identity.ImageHandle"/> and absolute file path.
/// </summary>
public sealed class NativeBinaryRegistry : INativeBinaryRegistry
{
    private readonly ConcurrentDictionary<string, NativeImage> _byHandle = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, NativeImage> _byPath = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public NativeResult<NativeImage> Load(string path, string? expectedBuildId = null)
    {
        var absPath = path.Length > 0 ? Path.GetFullPath(path) : path;

        // Fast-path: already cached by path
        if (_byPath.TryGetValue(absPath, out var cached))
        {
            // Re-verify build id if the caller provided one
            if (expectedBuildId is not null &&
                !string.Equals(cached.Handle.BuildIdHex, expectedBuildId, StringComparison.OrdinalIgnoreCase))
            {
                // Evict stale entry and reload
                _byPath.TryRemove(absPath, out _);
                _byHandle.TryRemove(cached.Handle.Value, out _);
            }
            else
            {
                return NativeResult.Ok(
                    $"Returned cached image '{Path.GetFileName(absPath)}'. Handle: {cached.Handle.Value}.",
                    cached);
            }
        }

        var result = NativeImageLoader.Load(absPath, expectedBuildId);
        if (!result.IsError)
        {
            var image = result.Data!;
            _byHandle[image.Handle.Value] = image;
            _byPath[absPath] = image;
        }
        return result;
    }

    /// <inheritdoc />
    public bool TryGet(string imageHandle, out NativeImage? image) =>
        _byHandle.TryGetValue(imageHandle, out image);

    /// <inheritdoc />
    public IReadOnlyList<NativeImage> List() => [.. _byHandle.Values];
}
