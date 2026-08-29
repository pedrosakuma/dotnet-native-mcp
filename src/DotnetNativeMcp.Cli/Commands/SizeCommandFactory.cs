using System.CommandLine;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Mstat;

namespace DotnetNativeMcp.Cli;

public static class SizeCommandFactory
{
    public static Command Create(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var pathArgument = new Argument<string>("path")
        {
            Description = "Path to the native binary paired with a NativeAOT .mstat sidecar.",
        };
        var groupByOption = new Option<string>("--group-by")
        {
            Description = "Grouping: assembly, namespace, type, method, or category. Default: method.",
            DefaultValueFactory = _ => "method",
        };
        var topNOption = new Option<int>("--top-n")
        {
            Description = "Maximum rows to return. Default 25, capped at 500.",
            DefaultValueFactory = _ => MstatReader.DefaultTopN,
        };
        var mstatPathOption = new Option<string?>("--mstat-path")
        {
            Description = "Optional absolute path override for the .mstat sidecar. Defaults to a sibling file next to the binary.",
        };

        var command = new Command("size", "Read the paired NativeAOT .mstat sidecar and return an aggregated size breakdown.");
        command.Arguments.Add(pathArgument);
        command.Options.Add(groupByOption);
        command.Options.Add(topNOption);
        command.Options.Add(mstatPathOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var invocation = CliApplication.BuildInvocationContext(parseResult, options);
            var result = Execute(
                parseResult.GetValue(pathArgument)!,
                parseResult.GetValue(groupByOption),
                parseResult.GetValue(topNOption),
                parseResult.GetValue(mstatPathOption),
                invocation.PathPolicy);

            await invocation.OutputWriter.WriteAsync(result, cancellationToken).ConfigureAwait(false);
            return result.IsError ? 1 : 0;
        });

        return command;
    }

    private static NativeResult<SizeCommandData> Execute(
        string binaryPath,
        string? groupBy,
        int topN,
        string? mstatPath,
        DotnetNativeMcp.Core.Security.PathAccessPolicy pathPolicy)
    {
        ArgumentNullException.ThrowIfNull(pathPolicy);

        var binaryValidation = pathPolicy.Validate(binaryPath);
        if (binaryValidation.IsError)
            return NativeResult.Fail<SizeCommandData>(binaryValidation.Error!.Kind, binaryValidation.Error.Message, binaryValidation.Error.Detail);

        if (!TryParseGroupBy(groupBy, out var grouping))
        {
            return NativeResult.Fail<SizeCommandData>(
                ErrorKinds.InvalidArgument,
                $"groupBy must be one of: assembly, namespace, type, method, category. Actual: '{groupBy}'.");
        }

        var load = DotnetNativeMcp.Core.NativeImageLoader.Load(binaryValidation.Data!);
        if (load.IsError)
            return NativeResult.Fail<SizeCommandData>(load.Error!.Kind, load.Error.Message, load.Error.Detail);

        var resolvedBinaryPath = load.Data!.FilePath;
        var mstatCandidate = !string.IsNullOrWhiteSpace(mstatPath)
            ? mstatPath
            : MstatReader.GetDefaultMstatPath(resolvedBinaryPath);
        var mstatValidation = pathPolicy.Validate(mstatCandidate);
        if (mstatValidation.IsError)
            return NativeResult.Fail<SizeCommandData>(mstatValidation.Error!.Kind, mstatValidation.Error.Message, mstatValidation.Error.Detail);

        var mstat = MstatReader.Read(mstatValidation.Data!);
        if (mstat.IsError)
            return NativeResult.Fail<SizeCommandData>(mstat.Error!.Kind, mstat.Error.Message, mstat.Error.Detail);

        var rows = MstatReader.Aggregate(mstat.Data!.Attributions, grouping, topN).ToList();
        var groupByName = grouping.ToString().ToLowerInvariant();

        return NativeResult.Ok(
            $"Returned {rows.Count} {groupByName} size bucket(s) from '{Path.GetFileName(mstatValidation.Data!)}' (mstat {mstat.Data.FormatVersion}, {mstat.Data.TotalSize} total bytes).",
            new SizeCommandData(
                resolvedBinaryPath,
                groupByName,
                mstatValidation.Data!,
                mstat.Data.FormatVersion,
                mstat.Data.TotalSize,
                mstat.Data.DeduplicatedMethodCount,
                mstat.Data.CategoryTotals,
                rows));
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
}

public sealed record SizeCommandData(
    string BinaryPath,
    string GroupBy,
    string MstatPath,
    string FormatVersion,
    long TotalAttributedBytes,
    int DeduplicatedMethodCount,
    IReadOnlyList<MstatCategoryTotal> CategoryTotals,
    IReadOnlyList<MstatBreakdown> Rows);
