using System.Collections.Concurrent;
using System.Globalization;

namespace DotnetNativeMcp.Core;

public sealed class NativeImageCatalog
{
    private readonly ConcurrentDictionary<string, NativeBinaryImage> _images = new(StringComparer.Ordinal);
    private int _nextId;

    public static NativeImageCatalog Shared { get; } = new();

    public string Register(
        string imageName,
        IReadOnlyCollection<NativeSymbol> symbols,
        IReadOnlyDictionary<string, long> sectionSizes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(sectionSizes);

        var handle = string.Create(
            CultureInfo.InvariantCulture,
            $"i:scaffold:{Sanitize(imageName)}:{Interlocked.Increment(ref _nextId)}");

        var image = new NativeBinaryImage(
            handle,
            imageName,
            symbols.ToDictionary(s => s.Name, s => s.Size, StringComparer.Ordinal),
            new Dictionary<string, long>(sectionSizes, StringComparer.Ordinal));

        if (!_images.TryAdd(handle, image))
        {
            throw new InvalidOperationException($"Image handle registration collision for '{handle}'.");
        }

        return handle;
    }

    public BinaryDiff Compare(string baselineImageHandle, string targetImageHandle, double thresholdPercent = 5.0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineImageHandle);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetImageHandle);
        ArgumentOutOfRangeException.ThrowIfLessThan(thresholdPercent, 0.0);

        if (string.Equals(baselineImageHandle, targetImageHandle, StringComparison.Ordinal))
        {
            return NativeBinaryDiffAnalyzer.Empty;
        }

        var baseline = ResolveImage(baselineImageHandle);
        var target = ResolveImage(targetImageHandle);
        return NativeBinaryDiffAnalyzer.Compare(baseline, target, thresholdPercent);
    }

    private NativeBinaryImage ResolveImage(string handle) =>
        _images.TryGetValue(handle, out var image)
            ? image
            : throw new KeyNotFoundException($"Image handle '{handle}' is unknown.");

    private static string Sanitize(string imageName)
    {
        var chars = imageName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars);
    }
}

internal static class NativeBinaryDiffAnalyzer
{
    internal static BinaryDiff Empty { get; } = new(
        Verdict: "no_change",
        AddedSymbols: Array.Empty<NativeSymbolSize>(),
        RemovedSymbols: Array.Empty<NativeSymbolSize>(),
        GrowthHotspots: Array.Empty<NativeSymbolDelta>(),
        ShrunkSymbols: Array.Empty<NativeSymbolDelta>(),
        SectionSizeDelta: new Dictionary<string, long>(StringComparer.Ordinal));

    internal static BinaryDiff Compare(
        NativeBinaryImage baseline,
        NativeBinaryImage target,
        double thresholdPercent)
    {
        var baselineSymbols = baseline.Symbols;
        var targetSymbols = target.Symbols;
        var baselineSections = baseline.Sections;
        var targetSections = target.Sections;

        var addedSymbols = targetSymbols
            .Where(kvp => !baselineSymbols.ContainsKey(kvp.Key))
            .Select(kvp => new NativeSymbolSize(kvp.Key, kvp.Value))
            .OrderByDescending(s => s.Size)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToArray();

        var removedSymbols = baselineSymbols
            .Where(kvp => !targetSymbols.ContainsKey(kvp.Key))
            .Select(kvp => new NativeSymbolSize(kvp.Key, kvp.Value))
            .OrderByDescending(s => s.Size)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToArray();

        var changed = baselineSymbols.Keys
            .Intersect(targetSymbols.Keys, StringComparer.Ordinal)
            .Select(name =>
            {
                var baselineSize = baselineSymbols[name];
                var targetSize = targetSymbols[name];
                var delta = targetSize - baselineSize;
                var deltaPercent = baselineSize == 0
                    ? (targetSize == 0 ? 0.0 : 100.0)
                    : (delta * 100.0) / baselineSize;

                return new NativeSymbolDelta(name, baselineSize, targetSize, delta, deltaPercent);
            })
            .Where(delta => delta.DeltaSize != 0)
            .ToArray();

        var growthHotspots = changed
            .Where(delta => delta.DeltaSize > 0 && delta.DeltaPercent >= thresholdPercent)
            .OrderByDescending(delta => delta.DeltaPercent)
            .ThenByDescending(delta => delta.DeltaSize)
            .ThenBy(delta => delta.Name, StringComparer.Ordinal)
            .ToArray();

        var shrunkSymbols = changed
            .Where(delta => delta.DeltaSize < 0 && Math.Abs(delta.DeltaPercent) >= thresholdPercent)
            .OrderBy(delta => delta.DeltaPercent)
            .ThenBy(delta => delta.DeltaSize)
            .ThenBy(delta => delta.Name, StringComparer.Ordinal)
            .ToArray();

        var sectionDelta = baselineSections.Keys
            .Union(targetSections.Keys, StringComparer.Ordinal)
            .Select(section =>
            {
                baselineSections.TryGetValue(section, out var baselineSize);
                targetSections.TryGetValue(section, out var targetSize);
                return new KeyValuePair<string, long>(section, targetSize - baselineSize);
            })
            .Where(kvp => kvp.Value != 0)
            .OrderByDescending(kvp => Math.Abs(kvp.Value))
            .ThenBy(kvp => kvp.Key, StringComparer.Ordinal)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);

        return new BinaryDiff(
            Verdict: CalculateVerdict(addedSymbols, removedSymbols, changed, sectionDelta.Values),
            AddedSymbols: addedSymbols,
            RemovedSymbols: removedSymbols,
            GrowthHotspots: growthHotspots,
            ShrunkSymbols: shrunkSymbols,
            SectionSizeDelta: sectionDelta);
    }

    private static string CalculateVerdict(
        IReadOnlyCollection<NativeSymbolSize> addedSymbols,
        IReadOnlyCollection<NativeSymbolSize> removedSymbols,
        IReadOnlyCollection<NativeSymbolDelta> changedSymbols,
        IEnumerable<long> sectionDeltaValues)
    {
        var hasPositive = addedSymbols.Any(s => s.Size > 0)
            || changedSymbols.Any(s => s.DeltaSize > 0)
            || sectionDeltaValues.Any(delta => delta > 0);
        var hasNegative = removedSymbols.Any(s => s.Size > 0)
            || changedSymbols.Any(s => s.DeltaSize < 0)
            || sectionDeltaValues.Any(delta => delta < 0);

        if (!hasPositive && !hasNegative)
        {
            return "no_change";
        }

        if (hasPositive && hasNegative)
        {
            return "mixed";
        }

        return hasPositive ? "grew" : "shrank";
    }
}

internal sealed record NativeBinaryImage(
    string Handle,
    string Name,
    IReadOnlyDictionary<string, long> Symbols,
    IReadOnlyDictionary<string, long> Sections);
