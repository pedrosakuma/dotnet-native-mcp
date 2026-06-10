using System.ComponentModel;
using System.Globalization;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Dgml;
using DotnetNativeMcp.Core.Diff;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Mstat;
using ModelContextProtocol.Server;

namespace DotnetNativeMcp.Server.Tools;

public sealed partial class NativeTools
{
    [McpServerTool(Name = "get_size_breakdown")]
    [Description(
        "Reads the NativeAOT .mstat sidecar paired with a loaded binary and returns aggregated native size by assembly, namespace, type, method, or category. " +
        "Counts the whole binary — method bodies (code + GC + EH info), EETypes/types, and the Blobs catch-all table (dehydrated data, runtime metadata, frozen-object regions, RVA static-field data, manifest resources) — so the totals account for far more than just code. " +
        "The result also reports the mstat format version, a per-category size summary, and the count of deduplicated (folded) method bodies. " +
        "Defaults to method grouping and the top 25 rows. Max topN: 500.")]
    public NativeResult<GetSizeBreakdownResult> GetSizeBreakdown(
        [Description("ImageHandle returned by load_native_binary.")] string imageHandle,
        [Description("Grouping: assembly, namespace, type, method, or category. Default: method.")] string groupBy = "method",
        [Description("Maximum rows to return. Default 25, capped at 500.")] int topN = MstatReader.DefaultTopN,
        [Description("Optional absolute path override for the .mstat sidecar. Defaults to a sibling file next to the loaded binary.")] string? mstatPath = null)
    {
        if (!registry.TryGet(imageHandle, out var image) || image is null)
            return NativeResult.Fail<GetSizeBreakdownResult>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{imageHandle}'. Call load_native_binary first.");

        if (!TryParseGroupBy(groupBy, out var grouping))
            return NativeResult.Fail<GetSizeBreakdownResult>(
                ErrorKinds.InvalidArgument,
                $"groupBy must be one of: assembly, namespace, type, method, category. Actual: '{groupBy}'.");

        var mstatCandidate = !string.IsNullOrWhiteSpace(mstatPath)
            ? mstatPath
            : MstatReader.GetDefaultMstatPath(image.FilePath);
        var mstatValidation = _pathPolicy.Validate(mstatCandidate);
        if (mstatValidation.IsError)
            return NativeResult.Fail<GetSizeBreakdownResult>(mstatValidation.Error!.Kind, mstatValidation.Error.Message, mstatValidation.Error.Detail);
        var resolvedMstatPath = mstatValidation.Data!;

        var mstat = MstatReader.Read(resolvedMstatPath);
        if (mstat.IsError)
            return NativeResult.Fail<GetSizeBreakdownResult>(mstat.Error!.Kind, mstat.Error.Message, mstat.Error.Detail);

        var rows = MstatReader.Aggregate(mstat.Data!.Attributions, grouping, topN)
            .Select(row => new SizeBreakdownRow(
                row.Key,
                row.AssemblyName,
                row.NamespaceName,
                row.TypeName,
                row.MethodName,
                row.TotalSize,
                row.AttributionCount))
            .ToList();

        var categoryTotals = mstat.Data.CategoryTotals
            .Select(c => new SizeCategoryTotalRow(c.Category, c.TotalSize, c.AttributionCount))
            .ToList();

        var hints = new List<NextActionHint>();
        if (grouping != MstatGroupBy.Method)
        {
            hints.Add(new NextActionHint(
                "get_size_breakdown",
                "Drill into per-method size buckets for this image.",
                new Dictionary<string, object?>
                {
                    ["imageHandle"] = imageHandle,
                    ["groupBy"] = "method",
                    ["mstatPath"] = resolvedMstatPath,
                }));
        }

        return NativeResult.Ok(
            $"Returned {rows.Count} {grouping.ToString().ToLowerInvariant()} size bucket(s) from '{Path.GetFileName(resolvedMstatPath)}' (mstat {mstat.Data.FormatVersion}, {mstat.Data.TotalSize} total bytes).",
            new GetSizeBreakdownResult(
                grouping.ToString().ToLowerInvariant(),
                resolvedMstatPath,
                mstat.Data.FormatVersion,
                mstat.Data.TotalSize,
                mstat.Data.DeduplicatedMethodCount,
                categoryTotals,
                rows),
            hints);
    }

    [McpServerTool(Name = "explain_retention")]
    [Description(
        "Reads the NativeAOT DGML reachability sidecar paired with a loaded binary and returns the shortest retained-by path " +
        "from any root to the matched node. Target matches exact DGML node id or a case-insensitive substring on node label.")]
    public NativeResult<ExplainRetentionResult> ExplainRetention(
        [Description("ImageHandle returned by load_native_binary.")] string imageHandle,
        [Description("Target DGML node query. Matches exact node id or a case-insensitive substring on the node label.")] string target,
        [Description("Optional absolute path override for the .dgml sidecar. Defaults to a sibling file next to the loaded binary.")] string? dgmlPath = null,
        [Description("Maximum edge depth to search from any root. Default 12, valid range 1..64.")] int maxDepth = RetentionPathFinder.DefaultMaxDepth)
    {
        if (!registry.TryGet(imageHandle, out var image) || image is null)
            return NativeResult.Fail<ExplainRetentionResult>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{imageHandle}'. Call load_native_binary first.");

        if (string.IsNullOrWhiteSpace(target))
            return NativeResult.Fail<ExplainRetentionResult>(
                ErrorKinds.InvalidArgument,
                "target must not be empty.");

        if (maxDepth < 1 || maxDepth > RetentionPathFinder.MaxDepthLimit)
        {
            return NativeResult.Fail<ExplainRetentionResult>(
                ErrorKinds.InvalidArgument,
                $"maxDepth must be between 1 and {RetentionPathFinder.MaxDepthLimit}. Actual: {maxDepth.ToString(CultureInfo.InvariantCulture)}.");
        }

        var dgmlCandidate = !string.IsNullOrWhiteSpace(dgmlPath)
            ? dgmlPath
            : DgmlReader.GetDefaultDgmlPath(image.FilePath);
        var dgmlValidation = _pathPolicy.Validate(dgmlCandidate);
        if (dgmlValidation.IsError)
            return NativeResult.Fail<ExplainRetentionResult>(dgmlValidation.Error!.Kind, dgmlValidation.Error.Message, dgmlValidation.Error.Detail);
        var resolvedDgmlPath = dgmlValidation.Data!;

        var dgml = DgmlReader.Read(resolvedDgmlPath);
        if (dgml.IsError)
            return NativeResult.Fail<ExplainRetentionResult>(dgml.Error!.Kind, dgml.Error.Message, dgml.Error.Detail);

        var targetMatchCount = RetentionPathFinder.CountTargetMatches(dgml.Data!, target);
        var path = RetentionPathFinder.FindShortestPath(dgml.Data!, target, maxDepth)
            .Select(segment => new RetentionPathNodeRow(
                segment.NodeId,
                segment.Label,
                segment.Category,
                segment.IncomingEdgeLabel))
            .ToList();

        var matchedNode = path.Count > 0 ? path[^1] : null;
        var summary = path.Count > 0
            ? $"Found a retention path with {path.Count} node(s) to '{matchedNode!.Label}' from '{Path.GetFileName(resolvedDgmlPath)}'."
            : $"No retention path found for '{target}' in '{Path.GetFileName(resolvedDgmlPath)}'.";

        return NativeResult.Ok(
            summary,
            new ExplainRetentionResult(
                resolvedDgmlPath,
                target,
                targetMatchCount,
                matchedNode?.Id,
                matchedNode?.Label,
                matchedNode?.Category,
                path));
    }

    private static bool TryParseGroupBy(string? value, out MstatGroupBy groupBy)
    {
        groupBy = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().ToLowerInvariant();
        groupBy = normalized switch
        {
            "assembly" => MstatGroupBy.Assembly,
            "namespace" => MstatGroupBy.Namespace,
            "type" => MstatGroupBy.Type,
            "method" => MstatGroupBy.Method,
            "category" => MstatGroupBy.Category,
            _ => default,
        };

        return normalized is "assembly" or "namespace" or "type" or "method" or "category";
    }

    [McpServerTool(Name = "compare_native_binaries")]
    [Description(
        "Diffs two loaded native images and reports build-id, format, architecture, file-size, section-size, and symbol-size deltas. " +
        "Use it to spot release-over-release native binary regressions and identify the symbols that grew the most. " +
        "When both images have a sibling NativeAOT .mstat sidecar, it also computes a managed size diff ('what grew between two builds?') at the requested grouping (assembly, namespace, type, method, or category), reporting build-level total deltas plus the top grown and shrunk buckets.")]
    public NativeResult<CompareNativeBinariesResult> CompareNativeBinaries(
        [Description("Baseline ImageHandle returned by load_native_binary.")] string baselineHandle,
        [Description("Current ImageHandle returned by load_native_binary.")] string currentHandle,
        [Description("Maximum added, removed, and changed symbols (and grown/shrunk mstat size buckets) to return per category. Must be between 1 and 500.")] int topN = 50,
        [Description("Grouping for the optional .mstat size diff: assembly, namespace, type, method, or category. Default: category.")] string mstatGroupBy = "category",
        [Description("Optional absolute path override for the baseline .mstat sidecar. Defaults to a sibling file next to the baseline binary.")] string? baselineMstatPath = null,
        [Description("Optional absolute path override for the current .mstat sidecar. Defaults to a sibling file next to the current binary.")] string? currentMstatPath = null)
    {
        if (!registry.TryGet(baselineHandle, out var baseline) || baseline is null)
            return NativeResult.Fail<CompareNativeBinariesResult>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{baselineHandle}'. Call load_native_binary first.");

        if (!registry.TryGet(currentHandle, out var current) || current is null)
            return NativeResult.Fail<CompareNativeBinariesResult>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{currentHandle}'. Call load_native_binary first.");

        if (topN < 1 || topN > 500)
            return NativeResult.Fail<CompareNativeBinariesResult>(
                ErrorKinds.InvalidArgument,
                $"topN must be between 1 and 500. Actual: {topN.ToString(CultureInfo.InvariantCulture)}.");

        if (!TryParseGroupBy(mstatGroupBy, out var mstatGrouping))
            return NativeResult.Fail<CompareNativeBinariesResult>(
                ErrorKinds.InvalidArgument,
                $"mstatGroupBy must be one of: assembly, namespace, type, method, category. Actual: '{mstatGroupBy}'.");

        var (mstatSizeDiff, mstatSizeDiffNote) = TryComputeMstatSizeDiff(
            baseline, current, baselineMstatPath, currentMstatPath, mstatGrouping, topN);

        var comparison = NativeBinaryComparer.Compare(baseline, current, topN);
        var data = new CompareNativeBinariesResult(
            comparison.Verdict.ToString().ToLowerInvariant(),
            new ValueDeltaResult(comparison.BaselineBuildIdHex, comparison.CurrentBuildIdHex, comparison.IsBuildIdEqual),
            new ValueDeltaResult(comparison.BaselineFormat.ToString(), comparison.CurrentFormat.ToString(), comparison.IsFormatEqual),
            new ValueDeltaResult(comparison.BaselineArchitecture.ToString(), comparison.CurrentArchitecture.ToString(), comparison.IsArchitectureEqual),
            comparison.BaselineBinarySizeBytes,
            comparison.CurrentBinarySizeBytes,
            comparison.TotalBinarySizeDeltaBytes,
            comparison.SectionDeltas.Select(delta => new SectionSizeDeltaRow(
                delta.Name,
                delta.BaselineSizeBytes,
                delta.CurrentSizeBytes,
                delta.SizeDeltaBytes)).ToList(),
            comparison.AddedSymbolCount,
            comparison.RemovedSymbolCount,
            comparison.ChangedSymbolCount,
            comparison.AddedSymbols.Select(symbol => new SymbolInventoryRow(
                symbol.Name,
                symbol.DemangledName,
                ToHex(symbol.Rva),
                symbol.SizeBytes,
                symbol.Section,
                symbol.IsFunction)).ToList(),
            comparison.RemovedSymbols.Select(symbol => new SymbolInventoryRow(
                symbol.Name,
                symbol.DemangledName,
                ToHex(symbol.Rva),
                symbol.SizeBytes,
                symbol.Section,
                symbol.IsFunction)).ToList(),
            comparison.ChangedSymbols.Select(symbol => new ChangedSymbolDeltaRow(
                symbol.Name,
                symbol.DemangledName,
                ToHex(symbol.BaselineRva),
                ToHex(symbol.CurrentRva),
                symbol.RvaDeltaBytes,
                symbol.BaselineSizeBytes,
                symbol.CurrentSizeBytes,
                symbol.SizeDeltaBytes,
                symbol.BaselineSection,
                symbol.CurrentSection,
                symbol.IsFunction)).ToList(),
            mstatSizeDiff,
            mstatSizeDiffNote);

        var hints = new List<NextActionHint>();
        if (comparison.ChangedSymbols.Count > 0)
        {
            hints.Add(new NextActionHint(
                "resolve_symbols",
                "Inspect the largest size-changed symbol in the current image.",
                new Dictionary<string, object?>
                {
                    ["imageHandle"] = currentHandle,
                    ["addresses"] = new[] { ToHex(comparison.ChangedSymbols[0].CurrentRva) },
                }));
        }

        if (mstatSizeDiff is { TopGrew.Count: > 0 })
        {
            hints.Add(new NextActionHint(
                "get_size_breakdown",
                "Drill into the per-method size breakdown of the current build's largest growth.",
                new Dictionary<string, object?>
                {
                    ["imageHandle"] = currentHandle,
                    ["groupBy"] = "method",
                }));
        }

        var mstatSummary = mstatSizeDiff is null
            ? string.Empty
            : $" mstat {mstatSizeDiff.GroupBy} Δ {FormatByteDelta(mstatSizeDiff.TotalSizeDelta)} ({mstatSizeDiff.TopGrew.Count} grew, {mstatSizeDiff.TopShrank.Count} shrank).";

        return NativeResult.Ok(
            $"{comparison.AddedSymbolCount} added, {comparison.RemovedSymbolCount} removed, {comparison.ChangedSymbolCount} size-changed (Δ {FormatByteDelta(comparison.TotalBinarySizeDeltaBytes)}).{mstatSummary}",
            data,
            hints);
    }

    private (MstatSizeDiffResult? Diff, string? Note) TryComputeMstatSizeDiff(
        Core.Imaging.NativeImage baseline,
        Core.Imaging.NativeImage current,
        string? baselineMstatPath,
        string? currentMstatPath,
        MstatGroupBy groupBy,
        int topN)
    {
        var (baselineDoc, baselineNote) = TryReadMstatForImage(baseline, baselineMstatPath, "baseline");
        var (currentDoc, currentNote) = TryReadMstatForImage(current, currentMstatPath, "current");

        if (baselineDoc is null || currentDoc is null)
        {
            var notes = new[] { baselineNote, currentNote }.Where(n => n is not null);
            return (null, string.Join(" ", notes));
        }

        var diff = MstatReader.Diff(baselineDoc, currentDoc, groupBy, topN);
        var result = new MstatSizeDiffResult(
            diff.GroupBy,
            diff.BaselineTotalSize,
            diff.CurrentTotalSize,
            diff.TotalSizeDelta,
            diff.AddedBucketCount,
            diff.RemovedBucketCount,
            diff.ChangedBucketCount,
            diff.TopGrew.Select(ToMstatSizeDiffRow).ToList(),
            diff.TopShrank.Select(ToMstatSizeDiffRow).ToList());
        return (result, null);
    }

    private (MstatDocument? Doc, string? Note) TryReadMstatForImage(Core.Imaging.NativeImage image, string? overridePath, string label)
    {
        var candidate = !string.IsNullOrWhiteSpace(overridePath)
            ? overridePath
            : MstatReader.GetDefaultMstatPath(image.FilePath);

        var validation = _pathPolicy.Validate(candidate);
        if (validation.IsError)
            return (null, $"{label} .mstat path rejected ({validation.Error!.Kind}).");

        var resolved = validation.Data!;
        if (!File.Exists(resolved))
            return (null, $"No sibling .mstat sidecar found for the {label} binary ('{Path.GetFileName(resolved)}').");

        var read = MstatReader.Read(resolved);
        if (read.IsError)
            return (null, $"Could not read the {label} .mstat sidecar: {read.Error!.Message}");

        return (read.Data!, null);
    }

    private static MstatSizeDiffRow ToMstatSizeDiffRow(MstatSizeBucketDelta delta) => new(
        delta.Key,
        delta.AssemblyName,
        delta.NamespaceName,
        delta.TypeName,
        delta.MethodName,
        delta.BaselineSize,
        delta.CurrentSize,
        delta.SizeDelta);
}

/// <summary>Result payload for <c>get_size_breakdown</c>.</summary>
public sealed record GetSizeBreakdownResult(
    string GroupBy,
    string MstatPath,
    string FormatVersion,
    long TotalAttributedBytes,
    int DeduplicatedMethodCount,
    IReadOnlyList<SizeCategoryTotalRow> CategoryTotals,
    IReadOnlyList<SizeBreakdownRow> Rows);

/// <summary>One per-category size total returned by <c>get_size_breakdown</c>.</summary>
public sealed record SizeCategoryTotalRow(
    string Category,
    long TotalSize,
    int AttributionCount);

/// <summary>One aggregated row returned by <c>get_size_breakdown</c>.</summary>
public sealed record SizeBreakdownRow(
    string Key,
    string AssemblyName,
    string NamespaceName,
    string TypeName,
    string? MethodName,
    long TotalSize,
    int AttributionCount);

/// <summary>Result payload for <c>explain_retention</c>.</summary>
public sealed record ExplainRetentionResult(
    string DgmlPath,
    string TargetQuery,
    int TargetMatchCount,
    string? MatchedNodeId,
    string? MatchedNodeLabel,
    string? MatchedNodeCategory,
    IReadOnlyList<RetentionPathNodeRow> Path);

/// <summary>One node in a DGML retention path.</summary>
public sealed record RetentionPathNodeRow(
    string Id,
    string Label,
    string? Category,
    string? EdgeLabelFromPrevious);

/// <summary>Simple baseline/current delta payload.</summary>
public sealed record ValueDeltaResult(
    string Baseline,
    string Current,
    bool IsEqual);

/// <summary>Section size delta row for <c>compare_native_binaries</c>.</summary>
public sealed record SectionSizeDeltaRow(
    string Name,
    ulong BaselineSizeBytes,
    ulong CurrentSizeBytes,
    long SizeDeltaBytes);

/// <summary>Added or removed symbol row for <c>compare_native_binaries</c>.</summary>
public sealed record SymbolInventoryRow(
    string Name,
    string DemangledName,
    string RvaHex,
    ulong SizeBytes,
    string? Section,
    bool IsFunction);

/// <summary>Changed symbol row for <c>compare_native_binaries</c>.</summary>
public sealed record ChangedSymbolDeltaRow(
    string Name,
    string DemangledName,
    string BaselineRvaHex,
    string CurrentRvaHex,
    long RvaDeltaBytes,
    ulong BaselineSizeBytes,
    ulong CurrentSizeBytes,
    long SizeDeltaBytes,
    string? BaselineSection,
    string? CurrentSection,
    bool IsFunction);

/// <summary>Result payload for <c>compare_native_binaries</c>.</summary>
public sealed record CompareNativeBinariesResult(
    string Verdict,
    ValueDeltaResult BuildIdDelta,
    ValueDeltaResult FormatDelta,
    ValueDeltaResult ArchitectureDelta,
    long BaselineBinarySizeBytes,
    long CurrentBinarySizeBytes,
    long TotalBinarySizeDeltaBytes,
    IReadOnlyList<SectionSizeDeltaRow> SectionDeltas,
    int AddedSymbolCount,
    int RemovedSymbolCount,
    int ChangedSymbolCount,
    IReadOnlyList<SymbolInventoryRow> AddedSymbols,
    IReadOnlyList<SymbolInventoryRow> RemovedSymbols,
    IReadOnlyList<ChangedSymbolDeltaRow> ChangedSymbols,
    MstatSizeDiffResult? MstatSizeDiff,
    string? MstatSizeDiffNote);

/// <summary>Optional NativeAOT .mstat managed size diff returned by <c>compare_native_binaries</c>.</summary>
public sealed record MstatSizeDiffResult(
    string GroupBy,
    long BaselineTotalSize,
    long CurrentTotalSize,
    long TotalSizeDelta,
    int AddedBucketCount,
    int RemovedBucketCount,
    int ChangedBucketCount,
    IReadOnlyList<MstatSizeDiffRow> TopGrew,
    IReadOnlyList<MstatSizeDiffRow> TopShrank);

/// <summary>One grown or shrunk size bucket in the <c>compare_native_binaries</c> mstat size diff.</summary>
public sealed record MstatSizeDiffRow(
    string Key,
    string AssemblyName,
    string NamespaceName,
    string TypeName,
    string? MethodName,
    long BaselineSize,
    long CurrentSize,
    long SizeDelta);
