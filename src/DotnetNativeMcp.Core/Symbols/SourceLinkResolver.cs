using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using DotnetNativeMcp.Core;

namespace DotnetNativeMcp.Core.Symbols;

public sealed class SourceLinkResolver
{
    // The official portable-PDB SourceLink GUID (Roslyn/ILC may use either variant).
    private static readonly Guid SourceLinkGuid        = new("CC110556-A091-4D38-9FEA-6C68D2148EA8");
    // NativeAOT ILC uses this GUID instead of the standard one.
    private static readonly Guid SourceLinkGuidNativeAot = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");
    private readonly IReadOnlyList<(string Prefix, string UrlTemplate)> _mappings;

    private SourceLinkResolver(IReadOnlyList<(string, string)> mappings) { _mappings = mappings; }

    public static SourceLinkResolver? TryLoad(string? pdbPath)
    {
        if (string.IsNullOrEmpty(pdbPath) || !File.Exists(pdbPath)) return null;
        try
        {
            var readResult = ResourceLimits.SafeReadAllBytes(pdbPath, ResourceLimits.MaxPdbBytes);
            if (readResult.IsError) return null;
            var bytes = readResult.Data!;
            if (bytes.Length < 4 || BitConverter.ToUInt32(bytes, 0) != 0x424A5342) return null;
            using var provider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(bytes, writable: false), MetadataStreamOptions.PrefetchMetadata);
            return TryLoad(provider.GetMetadataReader());
        }
        catch { return null; }
    }

    public static SourceLinkResolver? TryLoadFromBytes(byte[] pdbBytes)
    {
        try
        {
            if (pdbBytes.Length < 4 || BitConverter.ToUInt32(pdbBytes, 0) != 0x424A5342) return null;
            using var provider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(pdbBytes, writable: false), MetadataStreamOptions.PrefetchMetadata);
            return TryLoad(provider.GetMetadataReader());
        }
        catch { return null; }
    }

    private static SourceLinkResolver? TryLoad(MetadataReader reader)
    {
        foreach (var handle in reader.CustomDebugInformation)
        {
            var cdi = reader.GetCustomDebugInformation(handle);
            var kind = reader.GetGuid(cdi.Kind);
            if (kind != SourceLinkGuid && kind != SourceLinkGuidNativeAot) continue;
            var blob = reader.GetBlobBytes(cdi.Value);
            var json = Encoding.UTF8.GetString(blob);
            var mappings = ParseSourceLinkJson(json);
            if (mappings is { Count: > 0 }) return new SourceLinkResolver(mappings);
        }
        return null;
    }

    public string? ResolveUrl(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;
        var n = filePath.Replace('\\', '/');
        foreach (var (prefix, urlTemplate) in _mappings)
        {
            if (prefix.EndsWith('*'))
            {
                var stem = prefix[..^1];
                if (n.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
                    return urlTemplate.Replace("*", n[stem.Length..], StringComparison.Ordinal);
            }
            else if (string.Equals(n, prefix, StringComparison.OrdinalIgnoreCase)) return urlTemplate;
        }
        return null;
    }

    private static List<(string, string)>? ParseSourceLinkJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("documents", out var documents)) return null;
            var mappings = new List<(string, string)>();
            foreach (var prop in documents.EnumerateObject())
                mappings.Add((prop.Name.Replace('\\', '/'), prop.Value.GetString() ?? string.Empty));
            return mappings;
        }
        catch { return null; }
    }
}
