using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetNativeMcp.Core.Imaging;

namespace DotnetNativeMcp.Core.Xref;

/// <summary>
/// Persistent on-disk cache for the native call-graph xref index produced by
/// <see cref="NativeCallGraphBuilder"/>. Cache files live under
/// <c>~/.cache/dotnet-native-mcp/&lt;build-id&gt;.xref</c> (or
/// <c>$XDG_CACHE_HOME/dotnet-native-mcp/</c> on Linux when the env var is set).
///
/// <para>
/// Each file is prefixed with a 4-byte ASCII magic (<c>NXR3</c>) followed by a
/// 4-byte little-endian version integer. On magic or version mismatch the file is
/// treated as a cache miss and the index is rebuilt. The previous <c>NXR2</c> format
/// is intentionally incompatible and will be discarded on first access.
/// </para>
///
/// <para>
/// The JSON payload contains three sections: <c>sameImage</c> (same-image xref index
/// keyed by target VA hex), <c>crossRefs</c> (cross-image entries keyed by
/// <c>callerBuildId:targetLib:targetSymbol</c>, written lazily on first cross-image query),
/// and <c>machO</c> (lazy Mach-O stub/export metadata used for cross-image resolution).
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
    // NXR3: bumped from NXR2 to invalidate caches that do not carry Mach-O metadata.
    private static readonly byte[] MagicBytes = [(byte)'N', (byte)'X', (byte)'R', (byte)'3'];
    private const int FormatVersion = 3;
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
    /// Also populates <paramref name="crossRefs"/> and <paramref name="machO"/> when present.
    /// Returns <see langword="false"/> on any read error, missing file, magic mismatch,
    /// version mismatch, or JSON parse failure — all treated as a cache miss.
    /// </summary>
    public static bool TryRead(
        string cachePath,
        out IReadOnlyDictionary<ulong, IReadOnlyList<CallSite>>? index,
        out Dictionary<string, IReadOnlyList<CrossImageCallSite>>? crossRefs,
        out MachOCrossImageMetadata? machO)
    {
        index = null;
        crossRefs = null;
        machO = null;
        try
        {
            if (!File.Exists(cachePath))
                return false;

            var bytes = SecureCacheFile.TryReadCapped(cachePath, ResourceLimits.MaxXrefCacheBytes);
            if (bytes is null)
                return false;
            if (bytes.Length < HeaderSize)
                return false;

            if (bytes[0] != MagicBytes[0] || bytes[1] != MagicBytes[1] ||
                bytes[2] != MagicBytes[2] || bytes[3] != MagicBytes[3])
                return false;

            var version = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
            if (version != FormatVersion)
                return false;

            var dto = JsonSerializer.Deserialize<CacheDto>(bytes.AsSpan(HeaderSize), SerializerOptions);
            if (dto is null)
                return false;

            var result = new Dictionary<ulong, IReadOnlyList<CallSite>>(dto.SameImage.Count);
            foreach (var (keyHex, sites) in dto.SameImage)
            {
                if (!ulong.TryParse(keyHex, System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out var key))
                    return false;

                result[key] = Array.ConvertAll(sites, s => (CallSite)s);
            }

            index = result;

            if (dto.CrossRefs is { Count: > 0 })
            {
                crossRefs = new Dictionary<string, IReadOnlyList<CrossImageCallSite>>(dto.CrossRefs.Count);
                foreach (var (key, sites) in dto.CrossRefs)
                    crossRefs[key] = Array.ConvertAll(sites, s => (CrossImageCallSite)s);
            }

            if (dto.MachO is not null)
            {
                var stubTargets = new Dictionary<ulong, string>(dto.MachO.StubTargets?.Count ?? 0);
                if (dto.MachO.StubTargets is not null)
                {
                    foreach (var (stubVaHex, symbolName) in dto.MachO.StubTargets)
                    {
                        if (!ulong.TryParse(stubVaHex, System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out var stubVa))
                        {
                            return false;
                        }

                        stubTargets[stubVa] = symbolName;
                    }
                }

                var exports = new Dictionary<string, ulong>(dto.MachO.Exports?.Count ?? 0, StringComparer.Ordinal);
                if (dto.MachO.Exports is not null)
                {
                    foreach (var (symbolName, exportHex) in dto.MachO.Exports)
                    {
                        if (!ulong.TryParse(exportHex, System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out var exportVa))
                        {
                            return false;
                        }

                        exports[symbolName] = exportVa;
                    }
                }

                machO = new MachOCrossImageMetadata(stubTargets, exports);
            }

            return true;
        }
        catch
        {
            index = null;
            crossRefs = null;
            machO = null;
            return false;
        }
    }

    public static bool TryRead(
        string cachePath,
        out IReadOnlyDictionary<ulong, IReadOnlyList<CallSite>>? index,
        out Dictionary<string, IReadOnlyList<CrossImageCallSite>>? crossRefs)
        => TryRead(cachePath, out index, out crossRefs, out _);

    /// <summary>
    /// Backward-compatible overload that ignores cross-refs (same-image only).
    /// </summary>
    public static bool TryRead(
        string cachePath,
        out IReadOnlyDictionary<ulong, IReadOnlyList<CallSite>>? index)
        => TryRead(cachePath, out index, out _, out _);

    /// <summary>
    /// Atomically writes the same-image xref index plus any cross-image refs to
    /// <paramref name="cachePath"/>. Existing cross-refs not in <paramref name="crossRefs"/>
    /// are discarded. Write failures are swallowed.
    /// </summary>
    public static void Write(
        string cachePath,
        IReadOnlyDictionary<ulong, IReadOnlyList<CallSite>> index,
        IReadOnlyDictionary<string, IReadOnlyList<CrossImageCallSite>>? crossRefs = null,
        MachOCrossImageMetadata? machO = null)
    {
        try
        {
            var sameImageDto = new Dictionary<string, CallSiteDto[]>(index.Count);
            foreach (var (key, sites) in index)
            {
                var keyHex = key.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
                sameImageDto[keyHex] = Array.ConvertAll([.. sites], s => (CallSiteDto)s);
            }

            Dictionary<string, CrossCallSiteDto[]>? crossRefsDto = null;
            if (crossRefs is { Count: > 0 })
            {
                crossRefsDto = new Dictionary<string, CrossCallSiteDto[]>(crossRefs.Count);
                foreach (var (key, sites) in crossRefs)
                    crossRefsDto[key] = Array.ConvertAll([.. sites], s => (CrossCallSiteDto)s);
            }

            MachOCacheDto? machODto = null;
            if (machO is not null && (machO.StubTargets.Count > 0 || machO.Exports.Count > 0))
            {
                var stubTargetsDto = new Dictionary<string, string>(machO.StubTargets.Count);
                foreach (var (stubVa, symbolName) in machO.StubTargets)
                    stubTargetsDto[stubVa.ToString("x16", System.Globalization.CultureInfo.InvariantCulture)] = symbolName;

                var exportsDto = new Dictionary<string, string>(machO.Exports.Count, StringComparer.Ordinal);
                foreach (var (symbolName, exportVa) in machO.Exports)
                    exportsDto[symbolName] = exportVa.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);

                machODto = new MachOCacheDto(stubTargetsDto, exportsDto);
            }

            var dto = new CacheDto(sameImageDto, crossRefsDto, machODto);
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(dto, SerializerOptions);

            var allBytes = new byte[HeaderSize + jsonBytes.Length];
            MagicBytes.CopyTo(allBytes, 0);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(allBytes.AsSpan(4), FormatVersion);
            jsonBytes.CopyTo(allBytes, HeaderSize);

            if (allBytes.LongLength > ResourceLimits.MaxXrefCacheBytes)
                return;

            SecureCacheFile.WriteAtomic(cachePath, allBytes);
        }
        catch
        {
            // Cache write failures are best-effort; never surface to callers.
        }
    }

    /// <summary>
    /// Builds the cross-ref cache key for a given caller build-id, target library, and symbol.
    /// </summary>
    /// <param name="callerBuildId">Build-id hex of the calling image.</param>
    /// <param name="targetLibrary">SONAME / DLL name, or <c>null</c> for anonymous imports.</param>
    /// <param name="targetSymbol">Exported symbol name.</param>
    public static string MakeCrossRefKey(string callerBuildId, string? targetLibrary, string targetSymbol)
        => $"{callerBuildId}:{targetLibrary ?? string.Empty}:{targetSymbol}";

    private sealed record CacheDto(
        [property: JsonPropertyName("sameImage")] Dictionary<string, CallSiteDto[]> SameImage,
        [property: JsonPropertyName("crossRefs")] Dictionary<string, CrossCallSiteDto[]>? CrossRefs,
        [property: JsonPropertyName("machO")] MachOCacheDto? MachO);

    private sealed record MachOCacheDto(
        [property: JsonPropertyName("stubTargets")] Dictionary<string, string>? StubTargets,
        [property: JsonPropertyName("exports")] Dictionary<string, string>? Exports);

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

    private sealed record CrossCallSiteDto(
        string SourceAddressHex,
        string? CallerSymbol,
        string? CallerDemangled,
        string Mnemonic,
        string Operands,
        string RawBytes,
        string CallerImageBuildId,
        string CallerImagePath)
    {
        public static explicit operator CrossImageCallSite(CrossCallSiteDto d) =>
            new(d.SourceAddressHex, d.CallerSymbol, d.CallerDemangled, d.Mnemonic, d.Operands,
                d.RawBytes, d.CallerImageBuildId, d.CallerImagePath);

        public static explicit operator CrossCallSiteDto(CrossImageCallSite s) =>
            new(s.SourceAddressHex, s.CallerSymbol, s.CallerDemangled, s.Mnemonic, s.Operands,
                s.RawBytes, s.CallerImageBuildId, s.CallerImagePath);
    }
}
