using System.CommandLine;
using System.Globalization;
using DotnetNativeMcp.Cli.Output;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.R2R;
using DotnetNativeMcp.Core.Security;

namespace DotnetNativeMcp.Cli;

public static class R2rCommandFactory
{
    public static Command Create(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var command = new Command("r2r", "Inspect ReadyToRun headers and runtime-function tables.");
        command.Subcommands.Add(CreateHeaderCommand(options));
        command.Subcommands.Add(CreateRuntimeFunctionsCommand(options));
        return command;
    }

    private static Command CreateHeaderCommand(CliOptions options)
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "Path to the ReadyToRun PE image to inspect.",
        };
        var command = new Command("header", "Decode the ReadyToRun header from a managed PE binary.");
        command.Arguments.Add(pathArgument);
        command.SetAction((parseResult, cancellationToken) =>
            ExecuteAsync(
                parseResult,
                options,
                invocation => BuildHeaderResult(parseResult.GetValue(pathArgument) ?? string.Empty, invocation.PathPolicy),
                cancellationToken));

        return command;
    }

    private static Command CreateRuntimeFunctionsCommand(CliOptions options)
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "Path to the ReadyToRun PE image to inspect.",
        };
        var cursorOption = new Option<int>("--cursor")
        {
            Description = "Opaque pagination cursor from a prior call. Pass 0 for the first page.",
            DefaultValueFactory = _ => 0,
        };
        var limitOption = new Option<int>("--limit")
        {
            Description = "Page size for paginated listing (default 100, max 500).",
            DefaultValueFactory = _ => 100,
        };

        var command = new Command("runtime-functions", "List paginated RUNTIME_FUNCTION entries from the RuntimeFunctions section.");
        command.Arguments.Add(pathArgument);
        command.Options.Add(cursorOption);
        command.Options.Add(limitOption);
        command.SetAction((parseResult, cancellationToken) =>
            ExecuteAsync(
                parseResult,
                options,
                invocation => BuildRuntimeFunctionsResult(
                    parseResult.GetValue(pathArgument) ?? string.Empty,
                    parseResult.GetValue(cursorOption),
                    parseResult.GetValue(limitOption),
                    invocation.PathPolicy),
                cancellationToken));

        return command;
    }

    private static async Task<int> ExecuteAsync<T>(
        ParseResult parseResult,
        CliOptions options,
        Func<CliInvocationContext, NativeResult<T>> action,
        CancellationToken cancellationToken)
    {
        var invocation = CliApplication.BuildInvocationContext(parseResult, options);
        var result = action(invocation);
        await invocation.OutputWriter.WriteAsync(result, cancellationToken).ConfigureAwait(false);
        return result.IsError ? 1 : 0;
    }

    private static NativeResult<R2rHeaderCommandData> BuildHeaderResult(string path, PathAccessPolicy pathPolicy)
    {
        var loadResult = LoadImage(path, pathPolicy);
        if (loadResult.IsError)
            return NativeResult.Fail<R2rHeaderCommandData>(
                loadResult.Error!.Kind,
                loadResult.Error.Message,
                loadResult.Error.Detail);

        var image = loadResult.Data!;
        var headerResult = ReadyToRunReader.ReadHeader(image);
        if (headerResult.IsError)
            return NativeResult.Fail<R2rHeaderCommandData>(
                headerResult.Error!.Kind,
                headerResult.Error.Message,
                headerResult.Error.Detail);

        var header = headerResult.Data!;
        var flags = ReadyToRunHeaderAttributesExtensions.DecodeNames(header.Flags);
        var sections = header.Sections
            .Select(section => new R2rSectionCommandData(
                section.Type,
                section.TypeName,
                $"0x{section.VirtualAddress:X8}",
                section.Size))
            .ToList();

        var flagSummary = flags.Count > 0 ? $", flags [{string.Join(", ", flags)}]" : string.Empty;

        return NativeResult.Ok(
            $"R2R header v{header.Version}: {header.Sections.Count} sections, architecture {image.Architecture}{flagSummary}.",
            new R2rHeaderCommandData(
                Path: image.FilePath,
                Architecture: image.Architecture.ToString(),
                Version: header.Version,
                MajorVersion: header.MajorVersion,
                MinorVersion: header.MinorVersion,
                Flags: header.Flags,
                FlagsHex: $"0x{header.Flags:X8}",
                FlagNames: flags,
                SectionCount: header.Sections.Count,
                HasRuntimeFunctions: header.FindSection(ReadyToRunSectionType.RuntimeFunctions) is not null,
                CompilerIdentifier: ReadyToRunReader.ReadCompilerIdentifier(image, header),
                OwnerCompositeExecutable: ReadyToRunReader.ReadOwnerCompositeExecutable(image, header),
                Sections: sections));
    }

    private static NativeResult<R2rRuntimeFunctionsCommandData> BuildRuntimeFunctionsResult(
        string path,
        int cursor,
        int limit,
        PathAccessPolicy pathPolicy)
    {
        var loadResult = LoadImage(path, pathPolicy);
        if (loadResult.IsError)
            return NativeResult.Fail<R2rRuntimeFunctionsCommandData>(
                loadResult.Error!.Kind,
                loadResult.Error.Message,
                loadResult.Error.Detail);

        var image = loadResult.Data!;
        var headerResult = ReadyToRunReader.ReadHeader(image);
        if (headerResult.IsError)
            return NativeResult.Fail<R2rRuntimeFunctionsCommandData>(
                headerResult.Error!.Kind,
                headerResult.Error.Message,
                headerResult.Error.Detail);

        var pageResult = ReadyToRunReader.ReadRuntimeFunctions(image, headerResult.Data!, cursor, limit);
        if (pageResult.IsError)
            return NativeResult.Fail<R2rRuntimeFunctionsCommandData>(
                pageResult.Error!.Kind,
                pageResult.Error.Message,
                pageResult.Error.Detail);

        var page = pageResult.Data!;
        return NativeResult.Ok(
            pageResult.Summary,
            new R2rRuntimeFunctionsCommandData(
                Path: image.FilePath,
                Cursor: page.Cursor,
                TotalCount: page.TotalCount,
                NextCursor: page.NextCursor,
                Functions: page.Functions
                    .Select(function => new R2rRuntimeFunctionCommandData(
                        function.Index,
                        $"0x{function.BeginAddress:X8}",
                        $"0x{function.EndAddress:X8}",
                        $"0x{function.UnwindInfoAddress:X8}"))
                    .ToList()));
    }

    private static NativeResult<NativeImage> LoadImage(string path, PathAccessPolicy pathPolicy)
    {
        ArgumentNullException.ThrowIfNull(pathPolicy);

        var validationResult = pathPolicy.Validate(path);
        if (validationResult.IsError)
            return NativeResult.Fail<NativeImage>(
                validationResult.Error!.Kind,
                validationResult.Error.Message,
                validationResult.Error.Detail);

        return NativeImageLoader.Load(validationResult.Data!);
    }
}

public sealed record R2rHeaderCommandData(
    string Path,
    string Architecture,
    string Version,
    ushort MajorVersion,
    ushort MinorVersion,
    uint Flags,
    string FlagsHex,
    IReadOnlyList<string> FlagNames,
    int SectionCount,
    bool HasRuntimeFunctions,
    string? CompilerIdentifier,
    string? OwnerCompositeExecutable,
    IReadOnlyList<R2rSectionCommandData> Sections) : ITableRenderable
{
    public ValueTask WriteTableAsync(TextWriter writer, CancellationToken cancellationToken = default) =>
        R2rTableRenderer.WriteAsync(this, writer, cancellationToken);
}

public sealed record R2rSectionCommandData(
    uint Type,
    string TypeName,
    string Rva,
    uint Size);

public sealed record R2rRuntimeFunctionsCommandData(
    string Path,
    int Cursor,
    int TotalCount,
    int? NextCursor,
    IReadOnlyList<R2rRuntimeFunctionCommandData> Functions) : ITableRenderable
{
    public ValueTask WriteTableAsync(TextWriter writer, CancellationToken cancellationToken = default) =>
        R2rTableRenderer.WriteAsync(this, writer, cancellationToken);
}

public sealed record R2rRuntimeFunctionCommandData(
    int Index,
    string BeginAddress,
    string EndAddress,
    string UnwindInfoAddress);

internal static class R2rTableRenderer
{
    public static async ValueTask WriteAsync(
        R2rHeaderCommandData data,
        TextWriter writer,
        CancellationToken cancellationToken)
    {
        await TableRenderer.WriteKeyValueRowsAsync(
            writer,
            [
                ("Path", data.Path),
                ("Architecture", data.Architecture),
                ("Version", data.Version),
                ("MajorVersion", data.MajorVersion.ToString(CultureInfo.InvariantCulture)),
                ("MinorVersion", data.MinorVersion.ToString(CultureInfo.InvariantCulture)),
                ("Flags", data.Flags.ToString(CultureInfo.InvariantCulture)),
                ("FlagsHex", data.FlagsHex),
                ("FlagNames", data.FlagNames.Count == 0 ? "(none)" : string.Join(", ", data.FlagNames)),
                ("SectionCount", data.SectionCount.ToString(CultureInfo.InvariantCulture)),
                ("HasRuntimeFunctions", data.HasRuntimeFunctions.ToString()),
                ("CompilerIdentifier", string.IsNullOrWhiteSpace(data.CompilerIdentifier) ? "(none)" : data.CompilerIdentifier),
                ("OwnerCompositeExecutable", string.IsNullOrWhiteSpace(data.OwnerCompositeExecutable) ? "(none)" : data.OwnerCompositeExecutable),
            ],
            cancellationToken)
            .ConfigureAwait(false);

        await TableRenderer.WriteBlankLineAsync(writer, cancellationToken).ConfigureAwait(false);
        await TableRenderer.WriteGridAsync(
            writer,
            "Sections",
            data.Sections,
            [
                new TableColumn<R2rSectionCommandData>("Type", row => row.Type.ToString(CultureInfo.InvariantCulture)),
                new TableColumn<R2rSectionCommandData>("TypeName", row => row.TypeName),
                new TableColumn<R2rSectionCommandData>("Rva", row => row.Rva),
                new TableColumn<R2rSectionCommandData>("Size", row => row.Size.ToString(CultureInfo.InvariantCulture)),
            ],
            cancellationToken)
            .ConfigureAwait(false);
    }

    public static async ValueTask WriteAsync(
        R2rRuntimeFunctionsCommandData data,
        TextWriter writer,
        CancellationToken cancellationToken)
    {
        await TableRenderer.WriteKeyValueRowsAsync(
            writer,
            [
                ("Path", data.Path),
                ("Cursor", data.Cursor.ToString(CultureInfo.InvariantCulture)),
                ("TotalCount", data.TotalCount.ToString(CultureInfo.InvariantCulture)),
                ("NextCursor", data.NextCursor?.ToString(CultureInfo.InvariantCulture) ?? "(none)"),
                ("ReturnedCount", data.Functions.Count.ToString(CultureInfo.InvariantCulture)),
            ],
            cancellationToken)
            .ConfigureAwait(false);

        await TableRenderer.WriteBlankLineAsync(writer, cancellationToken).ConfigureAwait(false);
        await TableRenderer.WriteGridAsync(
            writer,
            "Functions",
            data.Functions,
            [
                new TableColumn<R2rRuntimeFunctionCommandData>("Index", row => row.Index.ToString(CultureInfo.InvariantCulture)),
                new TableColumn<R2rRuntimeFunctionCommandData>("BeginAddress", row => row.BeginAddress),
                new TableColumn<R2rRuntimeFunctionCommandData>("EndAddress", row => row.EndAddress),
                new TableColumn<R2rRuntimeFunctionCommandData>("UnwindInfoAddress", row => row.UnwindInfoAddress),
            ],
            cancellationToken)
            .ConfigureAwait(false);
    }
}
