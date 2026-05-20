namespace DotnetNativeMcp.Core.Symbols;

/// <summary>
/// Persistent disk cache for extracted embedded PDB bytes.
///
/// <para>
/// Cache files live under <c>~/.cache/dotnet-native-mcp/&lt;buildIdHex&gt;.pdb</c>
/// (or <c>$XDG_CACHE_HOME/dotnet-native-mcp/</c> when set). On first extraction the
/// bytes are written to a temp file next to the cache file and then atomically renamed
/// so concurrent processes never observe a partial write.
/// </para>
///
/// <para>
/// Set <c>DOTNET_NATIVE_MCP_PDB_EXTRACT_CACHE=0</c> to disable all disk I/O.
/// Cache misses are silent; write failures are swallowed so the server never fails
/// because of cache unavailability.
/// </para>
/// </summary>
public static class EmbeddedPdbDiskCache
{
    // Maximum PDB size accepted from cache (32 MiB — matches extractor decompression cap).
    private const long MaxPdbBytes = 32L * 1024 * 1024;
    /// <summary>
    /// Returns <see langword="true"/> when disk caching is enabled.
    /// Controlled by <c>DOTNET_NATIVE_MCP_PDB_EXTRACT_CACHE</c>: any value other than
    /// <c>"0"</c> (and an unset variable) means enabled.
    /// </summary>
    public static bool IsEnabled =>
        !string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_NATIVE_MCP_PDB_EXTRACT_CACHE"),
            "0",
            StringComparison.Ordinal);

    /// <summary>
    /// Derives the cache file path for the given build-id, honoring
    /// <c>XDG_CACHE_HOME</c> when set.
    /// </summary>
    public static string GetCachePath(string buildIdHex)
    {
        var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var cacheBase = string.IsNullOrEmpty(xdgCache)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache")
            : xdgCache;

        return Path.Combine(cacheBase, "dotnet-native-mcp", $"{buildIdHex}.pdb");
    }

    /// <summary>
    /// Tries to read cached PDB bytes from <paramref name="cachePath"/>.
    /// Returns <c>null</c> on any error, cache miss, or oversized file.
    /// </summary>
    public static byte[]? TryRead(string cachePath)
    {
        try
        {
            if (!File.Exists(cachePath)) return null;
            // Guard against unexpectedly large cache files before allocating.
            var info = new FileInfo(cachePath);
            if (info.Length > MaxPdbBytes || info.Length < 4) return null;
            var bytes = File.ReadAllBytes(cachePath);
            // Validate it looks like a portable PDB (BSJB magic).
            if (bytes.Length < 4 || BitConverter.ToUInt32(bytes, 0) != 0x424A5342u)
                return null;
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Writes <paramref name="pdbBytes"/> to <paramref name="cachePath"/> atomically
    /// (temp-file + rename). Silently swallows all errors.
    /// </summary>
    public static void Write(string cachePath, byte[] pdbBytes)
    {
        try
        {
            var dir = Path.GetDirectoryName(cachePath)!;
            Directory.CreateDirectory(dir);

            var tmp = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(tmp, pdbBytes);
            File.Move(tmp, cachePath, overwrite: true);
        }
        catch
        {
            // Best-effort; cache unavailability must never surface as an error.
        }
    }
}
