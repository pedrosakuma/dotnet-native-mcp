using System.CommandLine;
using System.Globalization;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Mstat;

namespace DotnetNativeMcp.Cli;

public static class SizeDiffCommandFactory
{
    public static Command Create(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var baselinePathArgument = new Argument<string>("baseline-path")
        {
            Description = "Path to the baseline native binary paired with a NativeAOT .mstat sidecar.",
        };
        var candidatePathArgument = new Argument<string>("candidate-path")
        {
            Description = "Path to the candidate native binary paired with a NativeAOT .mstat sidecar.",
        };
        var topNOption = new Option<int>("--top-n")
        {
            Description = "Maximum grown and shrunk buckets to return. Default 50, valid range 1..500.",
            DefaultValueFactory = _ => 50,
        };
        var mstatGroupByOption = new Option<string>("--mstat-group-by")
        {
            Description = "Grouping for the .mstat size diff: assembly, namespace, type, method, or category. Default: category.",
            DefaultValueFactory = _ => "category",
        };
        var baselineMstatPathOption = new Option<string?>("--baseline-mstat-path")
        {
            Description = "Optional absolute path override for the baseline .mstat sidecar. Defaults to a sibling file next to the baseline binary.",
        };
        var currentMstatPathOption = new Option<string?>("--current-mstat-path")
        {
            Description = "Optional absolute path override for the candidate/current .mstat sidecar. Defaults to a sibling file next to the candidate binary.",
        };
        var failOnIncreaseBytesOption = new Option<long?>("--fail-on-increase-bytes")
        {
            Description = "Return a non-zero exit code when total attributed bytes increase by more than this threshold.",
        };

        var command = new Command("size-diff", "Diff paired NativeAOT .mstat sidecars for two native binaries.");
        command.Arguments.Add(baselinePathArgument);
        command.Arguments.Add(candidatePathArgument);
        command.Options.Add(topNOption);
        command.Options.Add(mstatGroupByOption);
        command.Options.Add(baselineMstatPathOption);
        command.Options.Add(currentMstatPathOption);
        command.Options.Add(failOnIncreaseBytesOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var invocation = CliApplication.BuildInvocationContext(parseResult, options);
            var result = Execute(
                parseResult.GetValue(baselinePathArgument)!,
                parseResult.GetValue(candidatePathArgument)!,
                parseResult.GetValue(topNOption),
                parseResult.GetValue(mstatGroupByOption),
                parseResult.GetValue(baselineMstatPathOption),
                parseResult.GetValue(currentMstatPathOption),
                parseResult.GetValue(failOnIncreaseBytesOption),
                invocation.PathPolicy);

            await invocation.OutputWriter.WriteAsync(result, cancellationToken).ConfigureAwait(false);
            return result.IsError
                ? 1
                : result.Data!.ThresholdExceeded
                    ? 2
                    : 0;
        });

        return command;
    }

    private static NativeResult<SizeDiffCommandData> Execute(
        string baselineBinaryPath,
        string candidateBinaryPath,
        int topN,
        string? mstatGroupBy,
        string? baselineMstatPath,
        string? currentMstatPath,
        long? failOnIncreaseBytes,
        DotnetNativeMcp.Core.Security.PathAccessPolicy pathPolicy)
    {
        ArgumentNullException.ThrowIfNull(pathPolicy);

        if (topN < 1 || topN > MstatReader.MaxTopN)
        {
            return NativeResult.Fail<SizeDiffCommandData>(
                ErrorKinds.InvalidArgument,
                $"topN must be between 1 and {MstatReader.MaxTopN}. Actual: {topN.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (!TryParseGroupBy(mstatGroupBy, out var grouping))
        {
            return NativeResult.Fail<SizeDiffCommandData>(
                ErrorKinds.InvalidArgument,
                $"mstatGroupBy must be one of: assembly, namespace, type, method, category. Actual: '{mstatGroupBy}'.");
        }

        if (failOnIncreaseBytes is < 0)
        {
            return NativeResult.Fail<SizeDiffCommandData>(
                ErrorKinds.InvalidArgument,
                $"failOnIncreaseBytes must be greater than or equal to 0. Actual: {failOnIncreaseBytes.Value.ToString(CultureInfo.InvariantCulture)}.");
        }

        var baselineValidation = pathPolicy.Validate(baselineBinaryPath);
        if (baselineValidation.IsError)
            return NativeResult.Fail<SizeDiffCommandData>(baselineValidation.Error!.Kind, baselineValidation.Error.Message, baselineValidation.Error.Detail);

        var candidateValidation = pathPolicy.Validate(candidateBinaryPath);
        if (candidateValidation.IsError)
            return NativeResult.Fail<SizeDiffCommandData>(candidateValidation.Error!.Kind, candidateValidation.Error.Message, candidateValidation.Error.Detail);

        var baselineLoad = DotnetNativeMcp.Core.NativeImageLoader.Load(baselineValidation.Data!);
        if (baselineLoad.IsError)
            return NativeResult.Fail<SizeDiffCommandData>(baselineLoad.Error!.Kind, baselineLoad.Error.Message, baselineLoad.Error.Detail);

        var candidateLoad = DotnetNativeMcp.Core.NativeImageLoader.Load(candidateValidation.Data!);
        if (candidateLoad.IsError)
            return NativeResult.Fail<SizeDiffCommandData>(candidateLoad.Error!.Kind, candidateLoad.Error.Message, candidateLoad.Error.Detail);

        var resolvedBaselineMstatPath = ResolveMstatPath(pathPolicy, baselineLoad.Data!.FilePath, baselineMstatPath);
        if (resolvedBaselineMstatPath.IsError)
            return NativeResult.Fail<SizeDiffCommandData>(resolvedBaselineMstatPath.Error!.Kind, resolvedBaselineMstatPath.Error.Message, resolvedBaselineMstatPath.Error.Detail);

        var resolvedCurrentMstatPath = ResolveMstatPath(pathPolicy, candidateLoad.Data!.FilePath, currentMstatPath);
        if (resolvedCurrentMstatPath.IsError)
            return NativeResult.Fail<SizeDiffCommandData>(resolvedCurrentMstatPath.Error!.Kind, resolvedCurrentMstatPath.Error.Message, resolvedCurrentMstatPath.Error.Detail);

        var baselineMstat = MstatReader.Read(resolvedBaselineMstatPath.Data!);
        if (baselineMstat.IsError)
            return NativeResult.Fail<SizeDiffCommandData>(baselineMstat.Error!.Kind, baselineMstat.Error.Message, baselineMstat.Error.Detail);

        var currentMstat = MstatReader.Read(resolvedCurrentMstatPath.Data!);
        if (currentMstat.IsError)
            return NativeResult.Fail<SizeDiffCommandData>(currentMstat.Error!.Kind, currentMstat.Error.Message, currentMstat.Error.Detail);

        var diff = MstatReader.Diff(baselineMstat.Data!, currentMstat.Data!, grouping, topN);
        var thresholdExceeded = failOnIncreaseBytes is long limit && diff.TotalSizeDelta > limit;

        var summary = $"mstat {diff.GroupBy} Δ {FormatByteDelta(diff.TotalSizeDelta)} ({diff.TopGrew.Count} grew, {diff.TopShrank.Count} shrank).";
        if (thresholdExceeded)
        {
            summary += $" Threshold exceeded: delta is greater than +{FormatBytes((ulong)failOnIncreaseBytes!.Value)}.";
        }

        return NativeResult.Ok(
            summary,
            new SizeDiffCommandData(
                baselineLoad.Data.FilePath,
                candidateLoad.Data.FilePath,
                diff.GroupBy,
                resolvedBaselineMstatPath.Data!,
                resolvedCurrentMstatPath.Data!,
                diff.BaselineTotalSize,
                diff.CurrentTotalSize,
                diff.TotalSizeDelta,
                diff.AddedBucketCount,
                diff.RemovedBucketCount,
                diff.ChangedBucketCount,
                diff.TopGrew,
                diff.TopShrank,
                failOnIncreaseBytes,
                thresholdExceeded));
    }

    private static NativeResult<string> ResolveMstatPath(
        DotnetNativeMcp.Core.Security.PathAccessPolicy pathPolicy,
        string binaryPath,
        string? overridePath)
    {
        var candidate = !string.IsNullOrWhiteSpace(overridePath)
            ? overridePath
            : MstatReader.GetDefaultMstatPath(binaryPath);

        var validation = pathPolicy.Validate(candidate);
        if (validation.IsError)
            return NativeResult.Fail<string>(validation.Error!.Kind, validation.Error.Message, validation.Error.Detail);

        return NativeResult.Ok($"Resolved '{Path.GetFileName(validation.Data!)}'.", validation.Data!);
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

    private static string FormatByteDelta(long delta)
    {
        if (delta == 0)
            return "0 B";

        var sign = delta > 0 ? "+" : "-";
        return sign + FormatBytes(delta > 0 ? (ulong)delta : (ulong)(-delta));
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        var format = unitIndex == 0 ? "0" : "0.0";
        return value.ToString(format, CultureInfo.InvariantCulture) + " " + units[unitIndex];
    }
}

public sealed record SizeDiffCommandData(
    string BaselineBinaryPath,
    string CandidateBinaryPath,
    string GroupBy,
    string BaselineMstatPath,
    string CurrentMstatPath,
    long BaselineTotalSize,
    long CandidateTotalSize,
    long TotalSizeDelta,
    int AddedBucketCount,
    int RemovedBucketCount,
    int ChangedBucketCount,
    IReadOnlyList<MstatSizeBucketDelta> TopGrew,
    IReadOnlyList<MstatSizeBucketDelta> TopShrank,
    long? FailOnIncreaseBytes,
    bool ThresholdExceeded);
