using System.CommandLine;
using System.Globalization;
using DotnetNativeMcp.Cli.Output;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Security;
using DotnetNativeMcp.Core.Symbols;

namespace DotnetNativeMcp.Cli;

public static class ResolveCommandFactory
{
    public static Command Create(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var pathArgument = new Argument<string>("path")
        {
            Description = "Path to the native binary to inspect.",
        };
        var addressOption = new Option<string[]>("--address")
        {
            Description = "Address to resolve (hex with optional 0x prefix, or decimal). Repeat to resolve a batch.",
            Required = true,
            AllowMultipleArgumentsPerToken = true,
        };

        var command = new Command("resolve", "Resolve one or more addresses to native symbols and source locations.");
        command.Arguments.Add(pathArgument);
        command.Options.Add(addressOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var invocation = CliApplication.BuildInvocationContext(parseResult, options);
            var registry = new NativeBinaryRegistry(invocation.PathPolicy);
            var sourceResolver = new SourceResolver();
            var result = Resolve(
                registry,
                sourceResolver,
                invocation.PathPolicy,
                parseResult.GetValue(pathArgument)!,
                parseResult.GetValue(addressOption) ?? []);

            await invocation.OutputWriter.WriteAsync(result, cancellationToken).ConfigureAwait(false);
            return result.IsError ? 1 : 0;
        });

        return command;
    }

    internal static NativeResult<ResolveCommandData> Resolve(
        INativeBinaryRegistry registry,
        SourceResolver sourceResolver,
        PathAccessPolicy pathPolicy,
        string path,
        IReadOnlyList<string> addresses)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(sourceResolver);
        ArgumentNullException.ThrowIfNull(pathPolicy);
        ArgumentNullException.ThrowIfNull(addresses);

        if (addresses.Count > StackSymbolicator.MaxFrameCount)
        {
            return NativeResult.Fail<ResolveCommandData>(
                ErrorKinds.InvalidArgument,
                $"Address count {addresses.Count} exceeds the maximum of {StackSymbolicator.MaxFrameCount}.");
        }

        var loadResult = CliBinaryLoadHelper.LoadValidatedImage(registry, pathPolicy, path);
        if (loadResult.IsError)
        {
            return NativeResult.Fail<ResolveCommandData>(
                loadResult.Error!.Kind,
                loadResult.Error.Message,
                loadResult.Error.Detail);
        }

        var image = loadResult.Data!;
        var resolved = StackSymbolicator.ResolveAddresses(image, addresses);
        var rows = resolved.Data!
            .Select(row => ToCommandRow(image, sourceResolver, row))
            .ToList();

        return NativeResult.Ok(resolved.Summary, new ResolveCommandData(rows));
    }

    private static ResolveCommandRow ToCommandRow(
        NativeImage image,
        SourceResolver sourceResolver,
        ResolvedAddress row)
    {
        SourceLocation? source = null;
        string? signature = null;

        if (!row.IsError &&
            row.ResolvedRvaHex is not null &&
            ulong.TryParse(row.ResolvedRvaHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rva))
        {
            var va = image.ImageBase + rva;
            source = sourceResolver.TrySourceFor(image, va);
            signature = DwarfInfoReader.TryGetSignatureForRva(image, va);
        }

        return new ResolveCommandRow(
            row.InputAddress,
            row.ResolvedRvaHex,
            row.MangledName,
            row.DemangledName,
            row.SectionName,
            row.Displacement,
            source,
            signature,
            row.Error);
    }
}

internal sealed record ResolveCommandRow(
    string InputAddress,
    string? ResolvedRvaHex,
    string? MangledName,
    string? DemangledName,
    string? SectionName,
    ulong? Displacement,
    SourceLocation? Source,
    string? Signature,
    NativeError? Error);

internal sealed record ResolveCommandData(IReadOnlyList<ResolveCommandRow> Resolutions) : ITableRenderable
{
    public ValueTask WriteTableAsync(TextWriter writer, CancellationToken cancellationToken = default) =>
        TableRenderer.WriteGridAsync(
            writer,
            "Resolutions",
            Resolutions,
            [
                new TableColumn<ResolveCommandRow>("address", FormatAddress),
                new TableColumn<ResolveCommandRow>("symbol", FormatSymbol),
                new TableColumn<ResolveCommandRow>("source", row => FormatSource(row.Source)),
            ],
            cancellationToken);

    private static string FormatAddress(ResolveCommandRow row) =>
        row.ResolvedRvaHex is null ? row.InputAddress : $"0x{row.ResolvedRvaHex}";

    private static string FormatSymbol(ResolveCommandRow row)
    {
        if (row.Error is not null)
        {
            return $"[{row.Error.Kind}] {row.Error.Message}";
        }

        return row.DemangledName ?? row.MangledName ?? string.Empty;
    }

    private static string FormatSource(SourceLocation? source)
    {
        if (source is null)
        {
            return string.Empty;
        }

        return source.EndLine is int endLine
            ? $"{Path.GetFileName(source.File)}:{source.StartLine}-{endLine}"
            : $"{Path.GetFileName(source.File)}:{source.StartLine}";
    }
}
