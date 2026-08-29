using System.CommandLine;
using System.Globalization;
using DotnetNativeMcp.Cli.Output;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Security;
using DotnetNativeMcp.Core.Symbols;
using DotnetNativeMcp.Core.Xref;

namespace DotnetNativeMcp.Cli;

public static class CallersCommandFactory
{
    public static Command Create(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var pathArgument = new Argument<string>("path")
        {
            Description = "Path to the native binary that contains the target address.",
        };
        var addressOption = new Option<string>("--address")
        {
            Description = "Target address to search callers for (hex with optional 0x prefix, or decimal).",
            Required = true,
        };
        var imageOption = new Option<string[]>("--image")
        {
            Description = "Additional candidate image to scan for cross-image callers. Repeat to search multiple images.",
            AllowMultipleArgumentsPerToken = true,
        };

        var command = new Command("callers", "Find native callers for a target address, including optional cross-image callers.");
        command.Arguments.Add(pathArgument);
        command.Options.Add(addressOption);
        command.Options.Add(imageOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var invocation = CliApplication.BuildInvocationContext(parseResult, options);
            var registry = new NativeBinaryRegistry(invocation.PathPolicy);
            var callGraphCache = new NativeCallGraphCache();
            var sourceResolver = new SourceResolver();
            var result = FindCallers(
                registry,
                callGraphCache,
                sourceResolver,
                invocation.PathPolicy,
                parseResult.GetValue(pathArgument)!,
                parseResult.GetValue(addressOption)!,
                parseResult.GetValue(imageOption) ?? []);

            await invocation.OutputWriter.WriteAsync(result, cancellationToken).ConfigureAwait(false);
            return result.IsError ? 1 : 0;
        });

        return command;
    }

    internal static NativeResult<CallersCommandData> FindCallers(
        INativeBinaryRegistry registry,
        NativeCallGraphCache callGraphCache,
        SourceResolver sourceResolver,
        PathAccessPolicy pathPolicy,
        string path,
        string address,
        IReadOnlyList<string> candidateImages)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(callGraphCache);
        ArgumentNullException.ThrowIfNull(sourceResolver);
        ArgumentNullException.ThrowIfNull(pathPolicy);
        ArgumentNullException.ThrowIfNull(candidateImages);

        var loadResult = CliBinaryLoadHelper.LoadValidatedImage(registry, pathPolicy, path);
        if (loadResult.IsError)
        {
            return NativeResult.Fail<CallersCommandData>(
                loadResult.Error!.Kind,
                loadResult.Error.Message,
                loadResult.Error.Detail);
        }

        foreach (var candidateImage in candidateImages)
        {
            var candidateResult = CliBinaryLoadHelper.LoadValidatedImage(registry, pathPolicy, candidateImage);
            if (candidateResult.IsError)
            {
                return NativeResult.Fail<CallersCommandData>(
                    candidateResult.Error!.Kind,
                    candidateResult.Error.Message,
                    candidateResult.Error.Detail);
            }
        }

        var image = loadResult.Data!;
        if (string.IsNullOrWhiteSpace(address))
        {
            return NativeResult.Fail<CallersCommandData>(
                ErrorKinds.InvalidArgument,
                "address must not be empty.");
        }

        if (image.Architecture is not (Architecture.X64 or Architecture.X86 or Architecture.Arm64))
        {
            return NativeResult.Fail<CallersCommandData>(
                ErrorKinds.DisassemblyUnsupported,
                $"Disassembly for {image.Architecture} is not supported. Only x86/x64 and ARM64 are implemented.");
        }

        if (!StackSymbolicator.TryParseAddress(address, out var parsedValue, out _))
        {
            return NativeResult.Fail<CallersCommandData>(
                ErrorKinds.InvalidArgument,
                $"Cannot parse address '{address}' as a hex or decimal value.");
        }

        var rva = SymbolResolution.VaToRva(parsedValue, image.ImageBase);
        var targetVa = image.ImageBase + rva;
        var targetSymbol = SymbolResolution.FindByRva(image.Symbols, rva) ??
            TryResolveMachOExportByRva(callGraphCache, image, rva);

        if (image.FindSection(rva) is null)
        {
            return NativeResult.Fail<CallersCommandData>(
                ErrorKinds.AddressOutOfRange,
                $"Address 0x{parsedValue:x} is outside the known sections of '{path}'.");
        }

        var sameImageCallers = callGraphCache.FindCallers(image, targetVa);
        var sameImageCount = sameImageCallers.Count;
        var crossBudget = Math.Max(0, ResourceLimits.MaxCallerSites - sameImageCount + 1);
        IReadOnlyList<CrossImageCallSite> crossImageCallers = [];
        if (candidateImages.Count > 0 &&
            targetSymbol is { } resolvedTargetSymbol &&
            resolvedTargetSymbol.Rva == rva &&
            NativeCallGraphCache.IsCrossXrefEnabled)
        {
            crossImageCallers = callGraphCache.FindCrossImageCallers(
                image,
                resolvedTargetSymbol.Name,
                null,
                registry,
                crossBudget);
        }

        var totalCallers = sameImageCallers.Count + crossImageCallers.Count;
        var truncated = totalCallers > ResourceLimits.MaxCallerSites;
        var rows = new List<CallerCommandRow>(Math.Min(totalCallers, ResourceLimits.MaxCallerSites));

        foreach (var site in sameImageCallers)
        {
            if (rows.Count >= ResourceLimits.MaxCallerSites)
            {
                break;
            }

            rows.Add(ToCallerRow(image, sourceResolver, site));
        }

        foreach (var site in crossImageCallers)
        {
            if (rows.Count >= ResourceLimits.MaxCallerSites)
            {
                break;
            }

            rows.Add(ToCallerRow(registry, sourceResolver, site));
        }

        var displayName = targetSymbol?.Name ?? address;
        var summary = truncated
            ? $"Found {totalCallers} caller(s) of '{displayName}' in '{Path.GetFileName(image.FilePath)}' (truncated to {rows.Count})."
            : $"Found {rows.Count} caller(s) of '{displayName}' in '{Path.GetFileName(image.FilePath)}'.";

        return NativeResult.Ok(
            summary,
            new CallersCommandData(
                targetVa.ToString("x16", CultureInfo.InvariantCulture),
                targetSymbol?.Name,
                targetSymbol?.DemangledName,
                totalCallers,
                rows,
                truncated));
    }

    private static CallerCommandRow ToCallerRow(
        NativeImage image,
        SourceResolver sourceResolver,
        CallSite site)
    {
        SourceLocation? source = null;
        if (ulong.TryParse(site.SourceAddressHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var siteVa))
        {
            source = sourceResolver.TrySourceFor(image, siteVa);
        }

        return new CallerCommandRow(
            site.SourceAddressHex,
            site.CallerSymbol,
            site.CallerDemangled,
            site.Mnemonic,
            site.Operands,
            site.RawBytes,
            source,
            image.Handle.BuildIdHex,
            image.FilePath,
            false);
    }

    private static CallerCommandRow ToCallerRow(
        INativeBinaryRegistry registry,
        SourceResolver sourceResolver,
        CrossImageCallSite site)
    {
        SourceLocation? source = null;
        var callerImage = registry.List()
            .FirstOrDefault(image => string.Equals(image.FilePath, site.CallerImagePath, StringComparison.OrdinalIgnoreCase));

        if (callerImage is not null &&
            ulong.TryParse(site.SourceAddressHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var siteVa))
        {
            source = sourceResolver.TrySourceFor(callerImage, siteVa);
        }

        return new CallerCommandRow(
            site.SourceAddressHex,
            site.CallerSymbol,
            site.CallerDemangled,
            site.Mnemonic,
            site.Operands,
            site.RawBytes,
            source,
            site.CallerImageBuildId,
            site.CallerImagePath,
            true);
    }

    private static NativeSymbol? TryResolveMachOExportByRva(
        NativeCallGraphCache callGraphCache,
        NativeImage image,
        ulong rva)
    {
        if (image.Format != BinaryFormat.MachO)
        {
            return null;
        }

        foreach (var (name, exportRva) in callGraphCache.GetOrBuildMachOExports(image))
        {
            if (exportRva == rva)
            {
                return new NativeSymbol(-1, name, name, exportRva, 0, null, true);
            }
        }

        return null;
    }
}

internal sealed record CallerCommandRow(
    string SourceAddressHex,
    string? CallerSymbol,
    string? CallerDemangled,
    string Mnemonic,
    string Operands,
    string RawBytes,
    SourceLocation? Source,
    string? CallerImageBuildId,
    string? CallerImagePath,
    bool IsCrossImage);

internal sealed record CallersCommandData(
    string TargetAddressHex,
    string? TargetSymbol,
    string? TargetDemangled,
    int TotalCallers,
    IReadOnlyList<CallerCommandRow> Callers,
    bool Truncated) : ITableRenderable
{
    public ValueTask WriteTableAsync(TextWriter writer, CancellationToken cancellationToken = default) =>
        TableRenderer.WriteGridAsync(
            writer,
            "Callers",
            Callers,
            [
                new TableColumn<CallerCommandRow>("caller-address", row => $"0x{row.SourceAddressHex}"),
                new TableColumn<CallerCommandRow>("caller-symbol", row => row.CallerDemangled ?? row.CallerSymbol ?? string.Empty),
                new TableColumn<CallerCommandRow>("caller-image", row => Path.GetFileName(row.CallerImagePath ?? string.Empty)),
            ],
            cancellationToken);
}
