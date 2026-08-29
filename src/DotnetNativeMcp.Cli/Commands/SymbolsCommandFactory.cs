using System.CommandLine;
using System.Globalization;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Imaging;

namespace DotnetNativeMcp.Cli;

public static class SymbolsCommandFactory
{
    public static Command Create(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var pathArgument = new Argument<string>("path")
        {
            Description = "Path to the native binary on disk.",
        };

        var pageSizeOption = new Option<int?>("--page-size")
        {
            Description = "Page size (default 100, max 500).",
            DefaultValueFactory = _ => null,
        };

        var limitOption = new Option<int?>("--limit")
        {
            Description = "Alias for --page-size.",
            DefaultValueFactory = _ => null,
        };

        var cursorOption = new Option<int>("--cursor")
        {
            Description = "Opaque pagination cursor from a prior call. Omit or pass 0 for the first page.",
            DefaultValueFactory = _ => 0,
        };

        var nameFilterOption = new Option<string?>("--name-filter")
        {
            Description = "Optional case-insensitive name filter substring.",
            DefaultValueFactory = _ => null,
        };

        var filterOption = new Option<string?>("--filter")
        {
            Description = "Alias for --name-filter.",
            DefaultValueFactory = _ => null,
        };

        var command = new Command("symbols", "List native symbols from a native .NET binary.");
        command.Arguments.Add(pathArgument);
        command.Options.Add(pageSizeOption);
        command.Options.Add(limitOption);
        command.Options.Add(cursorOption);
        command.Options.Add(nameFilterOption);
        command.Options.Add(filterOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var invocation = CliApplication.BuildInvocationContext(parseResult, options);
            var path = parseResult.GetValue(pathArgument) ?? string.Empty;
            var pageSize = parseResult.GetValue(pageSizeOption) ?? parseResult.GetValue(limitOption) ?? 100;
            var cursor = parseResult.GetValue(cursorOption);
            var nameFilter = parseResult.GetValue(nameFilterOption) ?? parseResult.GetValue(filterOption);

            var validation = invocation.PathPolicy.Validate(path);
            if (validation.IsError)
            {
                var failure = NativeResult.Fail<SymbolsCommandData>(
                    validation.Error!.Kind,
                    validation.Error.Message,
                    validation.Error.Detail);
                await invocation.OutputWriter.WriteAsync(failure, cancellationToken).ConfigureAwait(false);
                return 1;
            }

            var loadResult = NativeImageLoader.Load(validation.Data!);
            if (loadResult.IsError)
            {
                var failure = NativeResult.Fail<SymbolsCommandData>(
                    loadResult.Error!.Kind,
                    loadResult.Error.Message,
                    loadResult.Error.Detail);
                await invocation.OutputWriter.WriteAsync(failure, cancellationToken).ConfigureAwait(false);
                return 1;
            }

            var result = BuildResult(loadResult.Data!, pageSize, cursor, nameFilter);
            await invocation.OutputWriter.WriteAsync(result, cancellationToken).ConfigureAwait(false);
            return 0;
        });

        return command;
    }

    private static NativeResult<SymbolsCommandData> BuildResult(
        NativeImage image,
        int pageSize,
        int cursor,
        string? nameFilter)
    {
        if (pageSize <= 0)
            pageSize = 100;
        if (pageSize > 500)
            pageSize = 500;
        if (cursor < 0)
            cursor = 0;

        IEnumerable<NativeSymbol> filtered = image.Symbols;
        if (!string.IsNullOrEmpty(nameFilter))
        {
            filtered = filtered.Where(symbol =>
                symbol.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase) ||
                symbol.DemangledName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase));
        }

        var all = filtered.ToList();
        var page = all.Skip(cursor)
            .Take(pageSize)
            .Select(symbol => new SymbolRowData(
                symbol.Index,
                symbol.Name,
                symbol.DemangledName,
                symbol.Rva.ToString("x16", CultureInfo.InvariantCulture),
                symbol.Size,
                symbol.Section,
                symbol.IsFunction))
            .ToList();

        int? nextCursor = cursor + page.Count < all.Count ? cursor + page.Count : null;
        var hints = new List<NextActionHint>();
        if (nextCursor is not null)
        {
            hints.Add(new NextActionHint(
                "symbols",
                "More symbols available on the next page.",
                new Dictionary<string, object?>
                {
                    ["path"] = image.FilePath,
                    ["cursor"] = nextCursor,
                    ["page-size"] = pageSize,
                    ["name-filter"] = nameFilter,
                }));
        }

        var end = cursor + page.Count - 1;
        var displayName = Path.GetFileName(image.FilePath);
        var summary = page.Count == 0
            ? $"No symbols found in '{displayName}'."
            : $"Page {cursor}..{end} of {all.Count} symbol(s) in '{displayName}'.";

        return NativeResult.Ok(summary, new SymbolsCommandData(page, all.Count, nextCursor), hints);
    }
}

public sealed record SymbolsCommandData(
    IReadOnlyList<SymbolRowData> Symbols,
    int TotalCount,
    int? NextCursor);

public sealed record SymbolRowData(
    int Index,
    string Name,
    string DemangledName,
    string RvaHex,
    ulong Size,
    string? Section,
    bool IsFunction);
