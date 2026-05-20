using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetNativeMcp.Core.Xref;

/// <summary>
/// Persistent on-disk cache for the native call-graph xref index produced by
/// <see cref="NativeCallGraphBuilder"/>. Cache files live under
/// <c>~/.cache/dotnet-native-mcp/&lt;build-id&gt;.xref</c> (or
/// <c>$XDG_CACHE_HOME/dotnet-native-mcp/</c> on Linux when the env var is set).
///
/// <para>
/// Each file is prefixed with a 4-byte ASCII magic (<c>NXR1</c>) followed by a
/// 4-byte little-endian version integer. On magic or version mismatch the file is
/// treated as a cache miss and the index is rebuilt.
/// </para>
///
/// <para>
/// Set <c>DOTNET_NATIVE_MCP_XREF_CACHE=0</c> to disable all disk I/O (useful in
/// CI or when the cache directory is not writable). Cache misses are silent; write
/// failures are swallowed so the server never fails because of cache unavailability.
/// </para>
/// </summary>
public static class NativeCallGraphDiskCache
{
    private static readonly byte[] MagicBytes = [(byte)'N', (byte)'X', (byte)'R', (byte)'1'];
    private const int FormatVersion = 1;
    private const int HeaderSize = 8; // 4 magic + 4 version

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Returns <see langword="true"/> when disk caching is enabled.
    /// Controlled by the <c>DOTNET_NATIVE_MCP_XREF_CACHE</c> environment variable:
    /// any value other than <c>"0"</c> (and an unset variable) means enabled.
    /// </summary>
    public static bool IsEnabled =>
        !string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE"),
            "0",
            StringComparison.Ordinal);

    /// <summary>
    /// Derives the cache file path for the given build-id.
    /// Honors <c>XDG_CACHE_HOME</c> when set; otherwise defaults to
    /// <c>~/.cache/dotnet-native-mcp/</c>.
    /// </summary>
    public static string GetCachePath(string buildIdHex)
    {
        var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var cacheBase = string.IsNullOrEmpty(xdgCache)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache")
            : xdgCache;

        return Path.Combine(cacheBase, "dotnet-native-mcp", $"{buildIdHex}.xref");
    }

    /// <summary>
    /// Attempts to read a previously cached xref index from <paramref name="cachePath"/>.
    /// Returns <see langword="false"/> on any read error, missing file, magic mismatch,
    /// version mismatch, or JSON parse failure — all treated as a cache miss with no
    /// exception surfaced to the caller.
    /// </summary>
    public static bool TryRead(
        string cachePath,
        out IReadOnlyDictionary<ulong, IReadOnlyList<CallSite>>? index)
    {
        index = null;
        try
        {
            if (!File.Exists(cachePath))
                return false;

            var bytes = File.ReadAllBytes(cachePath);
            if (bytes.Length < HeaderSize)
                return false;

            // Verify magic.
            if (bytes[0] != MagicBytes[0] || bytes[1] != MagicBytes[1] ||
                bytes[2] != MagicBytes[2] || bytes[3] != MagicBytes[3])
                return false;

            // Verify version.
            var version = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
            if (version != FormatVersion)
                return false;

            var json = bytes.AsSpan(HeaderSize);
            var dto = JsonSerializer.Deserialize<Dictionary<string, CallSiteDto[]>>(json, SerializerOptions);
            if (dto is null)
                return false;

            var result = new Dictionary<ulong, IReadOnlyList<CallSite>>(dto.Count);
            foreach (var (keyHex, sites) in dto)
            {
                if (!ulong.TryParse(keyHex, System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out var key))
                    return false;

                result[key] = Array.ConvertAll(sites, s => (CallSite)s);
            }

            index = result;
            return true;
        }
        catch
        {
            index = null;
            return false;
        }
    }

    /// <summary>
    /// Atomically writes the xref index to <paramref name="cachePath"/>.
    /// A temporary sibling file is written first, then renamed over the target so
    /// concurrent readers never observe a partial write. Write failures are swallowed.
    /// </summary>
    public static void Write(
        string cachePath,
        IReadOnlyDictionary<ulong, IReadOnlyList<CallSite>> index)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            // Convert ulong keys to lowercase hex strings for JSON portability.
            var dto = new Dictionary<string, CallSiteDto[]>(index.Count);
            foreach (var (key, sites) in index)
            {
                var keyHex = key.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
                dto[keyHex] = Array.ConvertAll([.. sites], s => (CallSiteDto)s);
            }

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(dto, SerializerOptions);

            var allBytes = new byte[HeaderSize + jsonBytes.Length];
            MagicBytes.CopyTo(allBytes, 0);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(allBytes.AsSpan(4), FormatVersion);
            jsonBytes.CopyTo(allBytes, HeaderSize);

            // Atomic write: tmp -> rename.
            var tmpPath = cachePath + "." + Environment.CurrentManagedThreadId.ToString(
                System.Globalization.CultureInfo.InvariantCulture) + ".tmp";
            File.WriteAllBytes(tmpPath, allBytes);
            File.Move(tmpPath, cachePath, overwrite: true);
        }
        catch
        {
            // Cache write failures are best-effort; never surface to callers.
        }
    }

    // DTO mirrors CallSite fields 1-to-1 for JSON serialization.
    private sealed record CallSiteDto(
        string SourceAddressHex,
        string? CallerSymbol,
        string? CallerDemangled,
        string Mnemonic,
        string Operands,
        string RawBytes)
    {
        public static explicit operator CallSite(CallSiteDto d) =>
            new(d.SourceAddressHex, d.CallerSymbol, d.CallerDemangled, d.Mnemonic, d.Operands, d.RawBytes);

        public static explicit operator CallSiteDto(CallSite s) =>
            new(s.SourceAddressHex, s.CallerSymbol, s.CallerDemangled, s.Mnemonic, s.Operands, s.RawBytes);
    }
}
