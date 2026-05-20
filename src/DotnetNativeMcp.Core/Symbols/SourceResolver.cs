using System.Collections.Concurrent;
using DotnetNativeMcp.Core.Imaging;

namespace DotnetNativeMcp.Core.Symbols;

public sealed class SourceResolver
{
    private readonly ConcurrentDictionary<string, ImageDebugData> _cache = new();

    private sealed class ImageDebugData(IReadOnlyList<DwarfLineReader.LineRow> dwarfRows, SourceLinkResolver? sourceLinkResolver)
    {
        public readonly IReadOnlyList<DwarfLineReader.LineRow> DwarfRows = dwarfRows;
        public readonly SourceLinkResolver? SourceLink = sourceLinkResolver;
    }

    public SourceLocation? TrySourceFor(NativeImage image, ulong va)
    {
        var data = _cache.GetOrAdd(image.Handle.Value, _ => Load(image));
        var row = DwarfLineReader.FindRow(data.DwarfRows, va);
        if (row is null) return null;
        string? url = data.SourceLink?.ResolveUrl(row.Value.File);
        return new SourceLocation(row.Value.File, row.Value.Line, null, url);
    }

    private static ImageDebugData Load(NativeImage image)
    {
        IReadOnlyList<DwarfLineReader.LineRow> rows = [];
        SourceLinkResolver? sourceLink = null;

        try { if (image.Format == BinaryFormat.Elf) rows = DwarfLineReader.Read(image); } catch { }

        // 1. Try sibling .pdb file first.
        try
        {
            var pdbPath = Path.ChangeExtension(image.FilePath, ".pdb");
            sourceLink = SourceLinkResolver.TryLoad(pdbPath);
        }
        catch { }

        // 2. Fall back to embedded PDB extraction (managed PE with <DebugType>embedded</DebugType>).
        if (sourceLink is null)
        {
            try
            {
                sourceLink = TryLoadSourceLinkFromEmbeddedPdb(image);
            }
            catch { }
        }

        return new ImageDebugData(rows, sourceLink);
    }

    /// <summary>
    /// Extracts an embedded PDB from the image bytes, caches it to disk, and loads
    /// a <see cref="SourceLinkResolver"/> from the resulting bytes.
    /// </summary>
    private static SourceLinkResolver? TryLoadSourceLinkFromEmbeddedPdb(NativeImage image)
    {
        if (image.Format != BinaryFormat.Pe) return null;

        var buildId = image.Handle.BuildIdHex;
        var cachePath = EmbeddedPdbDiskCache.GetCachePath(buildId);

        byte[]? pdbBytes = null;

        // Try disk cache first.
        if (EmbeddedPdbDiskCache.IsEnabled)
            pdbBytes = EmbeddedPdbDiskCache.TryRead(cachePath);

        if (pdbBytes is null)
        {
            pdbBytes = EmbeddedPdbExtractor.TryExtractFromPe(image.RawBytes);
            if (pdbBytes is not null && EmbeddedPdbDiskCache.IsEnabled)
                EmbeddedPdbDiskCache.Write(cachePath, pdbBytes);
        }

        return pdbBytes is not null
            ? SourceLinkResolver.TryLoadFromBytes(pdbBytes)
            : null;
    }
}
