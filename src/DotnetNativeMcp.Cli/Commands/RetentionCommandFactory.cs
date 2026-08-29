using System.CommandLine;
using System.Globalization;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Dgml;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Mstat;

namespace DotnetNativeMcp.Cli;

public static class RetentionCommandFactory
{
    public static Command Create(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var pathArgument = new Argument<string>("path")
        {
            Description = "Path to the native binary whose paired DGML sidecar will be inspected.",
        };
        var targetOption = new Option<string?>("--target")
        {
            Description = "Target DGML node query. Matches exact node id or a case-insensitive substring on the node label.",
            Required = true,
        };
        var dgmlPathOption = new Option<string?>("--dgml-path")
        {
            Description = "Optional absolute path override for the .dgml sidecar. Defaults to a sibling file next to the binary.",
        };
        var maxDepthOption = new Option<int>("--max-depth")
        {
            Description = $"Maximum edge depth to search from any root. Default {RetentionPathFinder.DefaultMaxDepth}, valid range 1..{RetentionPathFinder.MaxDepthLimit}.",
            DefaultValueFactory = _ => RetentionPathFinder.DefaultMaxDepth,
        };
        var maxPathsOption = new Option<int>("--max-paths")
        {
            Description = $"Maximum number of retention paths to return. Default {RetentionPathFinder.DefaultMaxPaths}, valid range 1..{RetentionPathFinder.MaxPathsLimit}.",
            DefaultValueFactory = _ => RetentionPathFinder.DefaultMaxPaths,
        };
        var mstatPathOption = new Option<string?>("--mstat-path")
        {
            Description = "Optional absolute path override for the .mstat sidecar used for size pricing.",
        };
        var noSizeCostOption = new Option<bool>("--no-size-cost")
        {
            Description = "Disable best-effort .mstat size pricing for retention-path nodes.",
        };

        var command = new Command("retention", "Explain why a target symbol or type is retained using the paired NativeAOT DGML sidecar.");
        command.Arguments.Add(pathArgument);
        command.Options.Add(targetOption);
        command.Options.Add(dgmlPathOption);
        command.Options.Add(maxDepthOption);
        command.Options.Add(maxPathsOption);
        command.Options.Add(mstatPathOption);
        command.Options.Add(noSizeCostOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var invocation = CliApplication.BuildInvocationContext(parseResult, options);
            var result = ExplainRetention(
                invocation,
                parseResult.GetValue(pathArgument)!,
                parseResult.GetValue(targetOption),
                parseResult.GetValue(dgmlPathOption),
                parseResult.GetValue(maxDepthOption),
                parseResult.GetValue(maxPathsOption),
                !parseResult.GetValue(noSizeCostOption),
                parseResult.GetValue(mstatPathOption));

            await invocation.OutputWriter.WriteAsync(result, cancellationToken).ConfigureAwait(false);
            return 0;
        });

        return command;
    }

    private static NativeResult<RetentionCommandData> ExplainRetention(
        CliInvocationContext invocation,
        string path,
        string? target,
        string? dgmlPath,
        int maxDepth,
        int maxPaths,
        bool includeSizeCost,
        string? mstatPath)
    {
        var registry = new NativeBinaryRegistry(invocation.PathPolicy);
        var load = registry.Load(path);
        if (load.IsError)
            return NativeResult.Fail<RetentionCommandData>(load.Error!.Kind, load.Error.Message, load.Error.Detail);

        if (string.IsNullOrWhiteSpace(target))
            return NativeResult.Fail<RetentionCommandData>(ErrorKinds.InvalidArgument, "target must not be empty.");

        if (maxDepth < 1 || maxDepth > RetentionPathFinder.MaxDepthLimit)
        {
            return NativeResult.Fail<RetentionCommandData>(
                ErrorKinds.InvalidArgument,
                $"maxDepth must be between 1 and {RetentionPathFinder.MaxDepthLimit}. Actual: {maxDepth.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (maxPaths < 1 || maxPaths > RetentionPathFinder.MaxPathsLimit)
        {
            return NativeResult.Fail<RetentionCommandData>(
                ErrorKinds.InvalidArgument,
                $"maxPaths must be between 1 and {RetentionPathFinder.MaxPathsLimit}. Actual: {maxPaths.ToString(CultureInfo.InvariantCulture)}.");
        }

        var dgmlCandidate = !string.IsNullOrWhiteSpace(dgmlPath)
            ? dgmlPath
            : DgmlReader.GetDefaultDgmlPath(load.Data!.FilePath);
        var dgmlValidation = invocation.PathPolicy.Validate(dgmlCandidate);
        if (dgmlValidation.IsError)
            return NativeResult.Fail<RetentionCommandData>(dgmlValidation.Error!.Kind, dgmlValidation.Error.Message, dgmlValidation.Error.Detail);

        var dgml = DgmlReader.Read(dgmlValidation.Data!);
        if (dgml.IsError)
            return NativeResult.Fail<RetentionCommandData>(dgml.Error!.Kind, dgml.Error.Message, dgml.Error.Detail);

        var graph = dgml.Data!;
        var candidates = RetentionPathFinder.FindTargets(graph, target, maxResults: 25)
            .Select(candidate => new RetentionTargetCandidateCommandRow(candidate.NodeId, candidate.Label, candidate.Category))
            .ToList();
        var targetMatchCount = RetentionPathFinder.CountTargetMatches(graph, target);

        MstatRetentionPricer? pricer = null;
        string? sizeCostNote = null;
        if (includeSizeCost)
        {
            var (mstatDoc, note) = TryReadMstatForImage(invocation.PathPolicy, load.Data!, mstatPath, "retention size-cost");
            if (mstatDoc is not null)
                pricer = MstatRetentionPricer.Build(mstatDoc);
            else
                sizeCostNote = note;
        }

        var paths = RetentionPathFinder.FindRetentionPaths(graph, target, maxDepth, maxPaths)
            .Select(pathResult => ToPathRow(pathResult, pricer))
            .ToList();

        var primaryPath = paths.Count > 0 ? paths[0].Nodes : (IReadOnlyList<RetentionPathNodeCommandRow>)[];
        var matchedNode = candidates.Count > 0 ? candidates[0] : null;
        string summary;
        if (paths.Count > 0)
        {
            var ambiguity = candidates.Count > 1
                ? $" (query matched {targetMatchCount.ToString(CultureInfo.InvariantCulture)} node(s); resolved to the first — pass an exact node id to disambiguate)"
                : string.Empty;
            var reflectionDrivenCount = paths.Count(pathResult => pathResult.ReflectionDriven);
            var verdict = reflectionDrivenCount > 0
                ? $" {reflectionDrivenCount.ToString(CultureInfo.InvariantCulture)} of {paths.Count.ToString(CultureInfo.InvariantCulture)} path(s) are reflection-driven (potentially trimmable)."
                : $" All {paths.Count.ToString(CultureInfo.InvariantCulture)} path(s) are structural (direct code / vtable / generics).";
            var sizeCost = string.Empty;
            if (pricer is not null && paths.Count > 0)
            {
                var shortest = paths[0];
                sizeCost = $" Shortest path keeps ~{shortest.PricedBytes.ToString(CultureInfo.InvariantCulture)} byte(s) alive ({shortest.PricedNodeCount.ToString(CultureInfo.InvariantCulture)} of {shortest.Nodes.Count.ToString(CultureInfo.InvariantCulture)} node(s) priced from .mstat).";
            }
            else if (sizeCostNote is not null)
            {
                sizeCost = $" Size cost unavailable: {sizeCostNote}";
            }

            summary = paths.Count == 1
                ? $"Found a retention path with {primaryPath.Count.ToString(CultureInfo.InvariantCulture)} node(s) to '{matchedNode!.Label}' from '{Path.GetFileName(dgmlValidation.Data!)}'.{ambiguity}{verdict}{sizeCost}"
                : $"Found {paths.Count.ToString(CultureInfo.InvariantCulture)} retention path(s) to '{matchedNode!.Label}' (shortest has {primaryPath.Count.ToString(CultureInfo.InvariantCulture)} node(s)) from '{Path.GetFileName(dgmlValidation.Data!)}'.{ambiguity}{verdict}{sizeCost}";
        }
        else
        {
            summary = matchedNode is null
                ? $"No node matched '{target}' in '{Path.GetFileName(dgmlValidation.Data!)}'."
                : $"Matched '{matchedNode.Label}' but found no retention path within depth {maxDepth.ToString(CultureInfo.InvariantCulture)} in '{Path.GetFileName(dgmlValidation.Data!)}'.";
        }

        return NativeResult.Ok(
            summary,
            new RetentionCommandData(
                dgmlValidation.Data!,
                target,
                targetMatchCount,
                matchedNode?.Id,
                matchedNode?.Label,
                matchedNode?.Category,
                primaryPath,
                candidates,
                paths,
                sizeCostNote));
    }

    private static RetentionPathCommandRow ToPathRow(RetentionPath path, MstatRetentionPricer? pricer)
    {
        var classification = RetentionReasonClassifier.ClassifyPath(path);
        var nodes = path.Segments.Select(segment => ToNodeRow(segment, pricer)).ToList();
        long pricedBytes = 0;
        var pricedNodeCount = 0;
        foreach (var node in nodes)
        {
            if (node.SizeBytes is { } size)
            {
                pricedBytes += size;
                pricedNodeCount++;
            }
        }

        return new RetentionPathCommandRow(
            path.RootId,
            path.RootLabel,
            path.RootCategory,
            path.Depth,
            classification.Verdict,
            classification.ReflectionDriven,
            nodes,
            pricedBytes,
            pricedNodeCount);
    }

    private static RetentionPathNodeCommandRow ToNodeRow(RetentionPathSegment segment, MstatRetentionPricer? pricer)
    {
        long? sizeBytes = null;
        string? sizeMatchKind = null;
        var sizeAttributionCount = 0;
        if (pricer is not null && pricer.TryPrice(segment.Label, out var cost))
        {
            sizeBytes = cost.SizeBytes;
            sizeMatchKind = cost.MatchKind;
            sizeAttributionCount = cost.AttributionCount;
        }

        return new RetentionPathNodeCommandRow(
            segment.NodeId,
            segment.Label,
            segment.Category,
            segment.IncomingEdgeLabel,
            segment.IncomingEdgeLabel is null ? null : RetentionReasonClassifier.Classify(segment.IncomingEdgeLabel).ToString(),
            sizeBytes,
            sizeMatchKind,
            sizeAttributionCount);
    }

    private static (MstatDocument? Doc, string? Note) TryReadMstatForImage(
        DotnetNativeMcp.Core.Security.PathAccessPolicy pathPolicy,
        NativeImage image,
        string? overridePath,
        string label)
    {
        var candidate = !string.IsNullOrWhiteSpace(overridePath)
            ? overridePath
            : MstatReader.GetDefaultMstatPath(image.FilePath);

        var validation = pathPolicy.Validate(candidate);
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
}

public sealed record RetentionCommandData(
    string DgmlPath,
    string TargetQuery,
    int TargetMatchCount,
    string? MatchedNodeId,
    string? MatchedNodeLabel,
    string? MatchedNodeCategory,
    IReadOnlyList<RetentionPathNodeCommandRow> Path,
    IReadOnlyList<RetentionTargetCandidateCommandRow> Candidates,
    IReadOnlyList<RetentionPathCommandRow> Paths,
    string? SizeCostNote);

public sealed record RetentionTargetCandidateCommandRow(
    string Id,
    string Label,
    string? Category);

public sealed record RetentionPathCommandRow(
    string RootId,
    string RootLabel,
    string? RootCategory,
    int Depth,
    string Classification,
    bool ReflectionDriven,
    IReadOnlyList<RetentionPathNodeCommandRow> Nodes,
    long PricedBytes,
    int PricedNodeCount);

public sealed record RetentionPathNodeCommandRow(
    string Id,
    string Label,
    string? Category,
    string? EdgeLabelFromPrevious,
    string? EdgeKind,
    long? SizeBytes,
    string? SizeMatchKind,
    int SizeAttributionCount);
