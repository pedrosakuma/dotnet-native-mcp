using System.Collections.Concurrent;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;

namespace DotnetNativeMcp.Core.Imaging
{

/// <summary>Thread-safe registry of loaded <see cref="NativeImage"/> instances.</summary>
public interface INativeBinaryRegistry
{
    /// <summary>Loads a binary from disk (or returns the cached instance if already loaded).</summary>
    NativeResult<NativeImage> Load(string path, string? expectedBuildId = null);

    /// <summary>
    /// Records a lazy path hint without opening the binary.
    /// A later <see cref="Load"/> for the same path uses the hint's optional build-id for verification.
    /// </summary>
    void RegisterHint(string path, string? buildId = null);

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
    private readonly ConcurrentDictionary<string, string?> _hints = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public NativeResult<NativeImage> Load(string path, string? expectedBuildId = null)
    {
        var absPath = path.Length > 0 ? Path.GetFullPath(path) : path;

        // Prefer hint's buildId when the caller didn't supply one
        if (expectedBuildId is null && _hints.TryGetValue(absPath, out var hintBuildId))
            expectedBuildId = hintBuildId;

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
                    cached,
                    NativeImageLoader.BuildLoadHints(cached));
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
    public void RegisterHint(string path, string? buildId = null)
    {
        var absPath = path.Length > 0 ? Path.GetFullPath(path) : path;
        _hints[absPath] = buildId;
    }

    /// <inheritdoc />
    public bool TryGet(string imageHandle, out NativeImage? image) =>
        _byHandle.TryGetValue(imageHandle, out image);

    /// <inheritdoc />
    public IReadOnlyList<NativeImage> List() => [.. _byHandle.Values];
}

}

namespace DotnetNativeMcp.Core.Diff
{
    using DotnetNativeMcp.Core.Imaging;

    public enum BinaryDiffVerdict
    {
        NoChange,
        Grew,
        Shrank,
        Mixed,
    }

    public sealed record CompareResult(
        string BaselineBuildIdHex,
        string CurrentBuildIdHex,
        bool IsBuildIdEqual,
        BinaryFormat BaselineFormat,
        BinaryFormat CurrentFormat,
        bool IsFormatEqual,
        Architecture BaselineArchitecture,
        Architecture CurrentArchitecture,
        bool IsArchitectureEqual,
        long BaselineBinarySizeBytes,
        long CurrentBinarySizeBytes,
        long TotalBinarySizeDeltaBytes,
        IReadOnlyList<SectionSizeDelta> SectionDeltas,
        int AddedSymbolCount,
        int RemovedSymbolCount,
        int ChangedSymbolCount,
        IReadOnlyList<AddedSymbolDelta> AddedSymbols,
        IReadOnlyList<RemovedSymbolDelta> RemovedSymbols,
        IReadOnlyList<ChangedSymbolDelta> ChangedSymbols,
        BinaryDiffVerdict Verdict);

    public sealed record SectionSizeDelta(
        string Name,
        ulong BaselineSizeBytes,
        ulong CurrentSizeBytes,
        long SizeDeltaBytes);

    public sealed record AddedSymbolDelta(
        string Name,
        string DemangledName,
        ulong Rva,
        ulong SizeBytes,
        string? Section,
        bool IsFunction);

    public sealed record RemovedSymbolDelta(
        string Name,
        string DemangledName,
        ulong Rva,
        ulong SizeBytes,
        string? Section,
        bool IsFunction);

    public sealed record ChangedSymbolDelta(
        string Name,
        string DemangledName,
        ulong BaselineRva,
        ulong CurrentRva,
        long RvaDeltaBytes,
        ulong BaselineSizeBytes,
        ulong CurrentSizeBytes,
        long SizeDeltaBytes,
        string? BaselineSection,
        string? CurrentSection,
        bool IsFunction);

    public sealed class NativeBinaryComparer
    {
        public static CompareResult Compare(NativeImage baseline, NativeImage current, int topN)
        {
            ArgumentNullException.ThrowIfNull(baseline);
            ArgumentNullException.ThrowIfNull(current);

            if (topN <= 0)
                throw new ArgumentOutOfRangeException(nameof(topN), "topN must be greater than zero.");

            var sectionDeltas = CompareSections(baseline.Sections, current.Sections);

            var baselineSymbols = IndexByName(baseline.Symbols);
            var currentSymbols = IndexByName(current.Symbols);

            var added = new List<AddedSymbolDelta>();
            var removed = new List<RemovedSymbolDelta>();
            var changed = new List<ChangedSymbolDelta>();

            foreach (var (name, currentSymbol) in currentSymbols)
            {
                if (!baselineSymbols.TryGetValue(name, out var baselineSymbol))
                {
                    added.Add(new AddedSymbolDelta(
                        currentSymbol.Name,
                        currentSymbol.DemangledName,
                        currentSymbol.Rva,
                        currentSymbol.Size,
                        currentSymbol.Section,
                        currentSymbol.IsFunction));
                    continue;
                }

                if (baselineSymbol.Size == currentSymbol.Size)
                    continue;

                changed.Add(new ChangedSymbolDelta(
                    currentSymbol.Name,
                    currentSymbol.DemangledName,
                    baselineSymbol.Rva,
                    currentSymbol.Rva,
                    ComputeDelta(baselineSymbol.Rva, currentSymbol.Rva),
                    baselineSymbol.Size,
                    currentSymbol.Size,
                    ComputeDelta(baselineSymbol.Size, currentSymbol.Size),
                    baselineSymbol.Section,
                    currentSymbol.Section,
                    baselineSymbol.IsFunction || currentSymbol.IsFunction));
            }

            foreach (var (name, baselineSymbol) in baselineSymbols)
            {
                if (currentSymbols.ContainsKey(name))
                    continue;

                removed.Add(new RemovedSymbolDelta(
                    baselineSymbol.Name,
                    baselineSymbol.DemangledName,
                    baselineSymbol.Rva,
                    baselineSymbol.Size,
                    baselineSymbol.Section,
                    baselineSymbol.IsFunction));
            }

            added.Sort(static (left, right) => CompareByMagnitudeThenName(left.SizeBytes, right.SizeBytes, left.Name, right.Name));
            removed.Sort(static (left, right) => CompareByMagnitudeThenName(left.SizeBytes, right.SizeBytes, left.Name, right.Name));
            changed.Sort(static (left, right) => CompareByMagnitudeThenName(left.SizeDeltaBytes, right.SizeDeltaBytes, left.Name, right.Name));

            var baselineBinarySizeBytes = baseline.RawBytes.Length;
            var currentBinarySizeBytes = current.RawBytes.Length;
            var totalBinarySizeDeltaBytes = currentBinarySizeBytes - baselineBinarySizeBytes;
            var verdict = DetermineVerdict(
                totalBinarySizeDeltaBytes,
                sectionDeltas.Count,
                added.Count,
                removed.Count,
                changed.Count,
                baseline.Handle.BuildIdHex == current.Handle.BuildIdHex,
                baseline.Format == current.Format,
                baseline.Architecture == current.Architecture);

            return new CompareResult(
                baseline.Handle.BuildIdHex,
                current.Handle.BuildIdHex,
                baseline.Handle.BuildIdHex == current.Handle.BuildIdHex,
                baseline.Format,
                current.Format,
                baseline.Format == current.Format,
                baseline.Architecture,
                current.Architecture,
                baseline.Architecture == current.Architecture,
                baselineBinarySizeBytes,
                currentBinarySizeBytes,
                totalBinarySizeDeltaBytes,
                sectionDeltas,
                added.Count,
                removed.Count,
                changed.Count,
                added.Take(topN).ToList(),
                removed.Take(topN).ToList(),
                changed.Take(topN).ToList(),
                verdict);
        }

        private static List<SectionSizeDelta> CompareSections(
            IReadOnlyList<NativeSection> baselineSections,
            IReadOnlyList<NativeSection> currentSections)
        {
            var baselineIndex = IndexSectionsByName(baselineSections);
            var currentIndex = IndexSectionsByName(currentSections);
            var names = new HashSet<string>(baselineIndex.Keys, StringComparer.Ordinal);
            names.UnionWith(currentIndex.Keys);

            var deltas = new List<SectionSizeDelta>();
            foreach (var name in names)
            {
                baselineIndex.TryGetValue(name, out var baselineSection);
                currentIndex.TryGetValue(name, out var currentSection);

                var baselineSize = baselineSection?.FileSize ?? 0UL;
                var currentSize = currentSection?.FileSize ?? 0UL;
                var sizeDelta = ComputeDelta(baselineSize, currentSize);
                if (sizeDelta == 0)
                    continue;

                deltas.Add(new SectionSizeDelta(name, baselineSize, currentSize, sizeDelta));
            }

            deltas.Sort(static (left, right) => CompareByMagnitudeThenName(left.SizeDeltaBytes, right.SizeDeltaBytes, left.Name, right.Name));
            return deltas;
        }

        private static Dictionary<string, NativeSymbol> IndexByName(IReadOnlyList<NativeSymbol> symbols)
        {
            var index = new Dictionary<string, NativeSymbol>(StringComparer.Ordinal);
            foreach (var symbol in symbols)
                index[symbol.Name] = symbol;
            return index;
        }

        private static Dictionary<string, NativeSection> IndexSectionsByName(IReadOnlyList<NativeSection> sections)
        {
            var index = new Dictionary<string, NativeSection>(StringComparer.Ordinal);
            foreach (var section in sections)
                index[section.Name] = section;
            return index;
        }

        private static BinaryDiffVerdict DetermineVerdict(
            long totalBinarySizeDeltaBytes,
            int sectionDeltaCount,
            int addedCount,
            int removedCount,
            int changedCount,
            bool isBuildIdEqual,
            bool isFormatEqual,
            bool isArchitectureEqual)
        {
            var hasAnyChange = sectionDeltaCount > 0 || addedCount > 0 || removedCount > 0 || changedCount > 0 ||
                !isBuildIdEqual || !isFormatEqual || !isArchitectureEqual;

            if (!hasAnyChange)
                return BinaryDiffVerdict.NoChange;

            if (totalBinarySizeDeltaBytes > 0)
                return BinaryDiffVerdict.Grew;

            if (totalBinarySizeDeltaBytes < 0)
                return BinaryDiffVerdict.Shrank;

            return BinaryDiffVerdict.Mixed;
        }

        private static int CompareByMagnitudeThenName(ulong leftMagnitude, ulong rightMagnitude, string leftName, string rightName)
        {
            var byMagnitude = rightMagnitude.CompareTo(leftMagnitude);
            return byMagnitude != 0 ? byMagnitude : StringComparer.Ordinal.Compare(leftName, rightName);
        }

        private static int CompareByMagnitudeThenName(long leftMagnitude, long rightMagnitude, string leftName, string rightName)
        {
            var byMagnitude = AbsoluteValue(rightMagnitude).CompareTo(AbsoluteValue(leftMagnitude));
            return byMagnitude != 0 ? byMagnitude : StringComparer.Ordinal.Compare(leftName, rightName);
        }

        private static ulong AbsoluteValue(long value) => value >= 0 ? (ulong)value : (ulong)(-value);

        private static long ComputeDelta(ulong baseline, ulong current) => checked((long)current - (long)baseline);
    }
}
