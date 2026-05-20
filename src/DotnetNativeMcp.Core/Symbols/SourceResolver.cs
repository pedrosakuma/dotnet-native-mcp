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
        try { var pdbPath = Path.ChangeExtension(image.FilePath, ".pdb"); sourceLink = SourceLinkResolver.TryLoad(pdbPath); } catch { }
        return new ImageDebugData(rows, sourceLink);
    }
}
