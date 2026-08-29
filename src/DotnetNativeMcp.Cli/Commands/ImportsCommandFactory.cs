using System.CommandLine;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;

namespace DotnetNativeMcp.Cli;

public static class ImportsCommandFactory
{
    public static Command Create(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var pathArgument = new Argument<string>("path")
        {
            Description = "Path to the native binary on disk.",
        };

        var kindOption = new Option<string>("--kind")
        {
            Description = "Import view to return: functions or libraries. Default functions.",
            DefaultValueFactory = _ => "functions",
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

        var command = new Command("imports", "List imported functions or libraries from a native .NET binary.");
        command.Arguments.Add(pathArgument);
        command.Options.Add(kindOption);
        command.Options.Add(pageSizeOption);
        command.Options.Add(limitOption);
        command.Options.Add(cursorOption);
        command.Options.Add(nameFilterOption);
        command.Options.Add(filterOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var invocation = CliApplication.BuildInvocationContext(parseResult, options);
            var path = parseResult.GetValue(pathArgument) ?? string.Empty;
            var kind = parseResult.GetValue(kindOption) ?? string.Empty;
            var pageSize = parseResult.GetValue(pageSizeOption) ?? parseResult.GetValue(limitOption) ?? 100;
            var cursor = parseResult.GetValue(cursorOption);
            var nameFilter = parseResult.GetValue(nameFilterOption) ?? parseResult.GetValue(filterOption);

            var validation = invocation.PathPolicy.Validate(path);
            if (validation.IsError)
            {
                var failure = NativeResult.Fail<ImportsCommandData>(
                    validation.Error!.Kind,
                    validation.Error.Message,
                    validation.Error.Detail);
                await invocation.OutputWriter.WriteAsync(failure, cancellationToken).ConfigureAwait(false);
                return 1;
            }

            var loadResult = NativeImageLoader.Load(validation.Data!);
            if (loadResult.IsError)
            {
                var failure = NativeResult.Fail<ImportsCommandData>(
                    loadResult.Error!.Kind,
                    loadResult.Error.Message,
                    loadResult.Error.Detail);
                await invocation.OutputWriter.WriteAsync(failure, cancellationToken).ConfigureAwait(false);
                return 1;
            }

            var result = BuildResult(loadResult.Data!, kind, pageSize, cursor, nameFilter);
            await invocation.OutputWriter.WriteAsync(result, cancellationToken).ConfigureAwait(false);
            return result.IsError ? 1 : 0;
        });

        return command;
    }

    private static NativeResult<ImportsCommandData> BuildResult(
        NativeImage image,
        string kind,
        int pageSize,
        int cursor,
        string? nameFilter)
    {
        if (!TryNormalizeImportKind(kind, out var normalizedKind, out var kindError))
            return NativeResult.Fail<ImportsCommandData>(ErrorKinds.InvalidArgument, kindError!);

        if (pageSize <= 0)
            pageSize = 100;
        if (pageSize > 500)
            pageSize = 500;
        if (cursor < 0)
            cursor = 0;

        if (normalizedKind == "functions")
        {
            var parsed = ReadImportedFunctions(image);
            if (parsed.IsError)
            {
                return new NativeResult<ImportsCommandData>(
                    parsed.Summary,
                    new ImportsCommandData(normalizedKind, [], null, 0, null),
                    [],
                    parsed.Error);
            }

            IEnumerable<ImportedFunction> filtered = parsed.Data!;
            if (!string.IsNullOrWhiteSpace(nameFilter))
            {
                filtered = filtered.Where(import =>
                    import.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(import.Library) && import.Library.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)));
            }

            var all = filtered.ToList();
            var page = all.Skip(cursor)
                .Take(pageSize)
                .Select(import => new ImportedFunctionRowData(import.Library, import.Name, import.Ordinal))
                .ToList();
            int? nextCursor = cursor + page.Count < all.Count ? cursor + page.Count : null;
            var hints = BuildImportHints(image, normalizedKind, nameFilter, pageSize, nextCursor);
            var summary = page.Count == 0
                ? $"No imported functions found in '{Path.GetFileName(image.FilePath)}'."
                : $"Page {cursor}..{cursor + page.Count - 1} of {all.Count} imported function(s) in '{Path.GetFileName(image.FilePath)}'.";

            return NativeResult.Ok(summary, new ImportsCommandData(normalizedKind, page, null, all.Count, nextCursor), hints);
        }

        var librariesResult = ReadImportedLibraries(image);
        if (librariesResult.IsError)
        {
            return new NativeResult<ImportsCommandData>(
                librariesResult.Summary,
                new ImportsCommandData(normalizedKind, null, [], 0, null),
                [],
                librariesResult.Error);
        }

        IEnumerable<ImportedLibrary> libraries = librariesResult.Data!;
        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            libraries = libraries.Where(import =>
                import.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase));
        }

        var libraryAll = libraries.ToList();
        var libraryPage = libraryAll.Skip(cursor)
            .Take(pageSize)
            .Select(import => new ImportedLibraryRowData(import.Name))
            .ToList();
        int? libraryNextCursor = cursor + libraryPage.Count < libraryAll.Count ? cursor + libraryPage.Count : null;
        var libraryHints = BuildImportHints(image, normalizedKind, nameFilter, pageSize, libraryNextCursor, libraryPage);
        var librarySummary = libraryPage.Count == 0
            ? $"No imported libraries found in '{Path.GetFileName(image.FilePath)}'."
            : $"Page {cursor}..{cursor + libraryPage.Count - 1} of {libraryAll.Count} imported libraries in '{Path.GetFileName(image.FilePath)}'.";

        return NativeResult.Ok(
            librarySummary,
            new ImportsCommandData(normalizedKind, null, libraryPage, libraryAll.Count, libraryNextCursor),
            libraryHints);
    }

    private static bool TryNormalizeImportKind(string kind, out string normalizedKind, out string? error)
    {
        normalizedKind = string.Empty;
        error = null;

        var candidate = string.IsNullOrWhiteSpace(kind) ? "functions" : kind.Trim();
        if (candidate.Equals("functions", StringComparison.OrdinalIgnoreCase))
        {
            normalizedKind = "functions";
            return true;
        }

        if (candidate.Equals("libraries", StringComparison.OrdinalIgnoreCase))
        {
            normalizedKind = "libraries";
            return true;
        }

        error = $"kind must be one of: functions, libraries. Actual: '{kind}'.";
        return false;
    }

    private static NativeResult<IReadOnlyList<ImportedFunction>> ReadImportedFunctions(NativeImage image) =>
        image.Format switch
        {
            BinaryFormat.Elf => ElfReader.ReadImportedFunctions(image),
            BinaryFormat.Pe => PeNativeReader.ReadImportedFunctions(image),
            BinaryFormat.MachO => MachOReader.ReadImportedFunctions(image),
            _ => NativeResult.Fail<IReadOnlyList<ImportedFunction>>(ErrorKinds.InternalError, $"Unsupported binary format '{image.Format}'."),
        };

    private static NativeResult<IReadOnlyList<ImportedLibrary>> ReadImportedLibraries(NativeImage image) =>
        image.Format switch
        {
            BinaryFormat.Elf => ElfReader.ReadImportedLibraries(image),
            BinaryFormat.Pe => PeNativeReader.ReadImportedLibraries(image),
            BinaryFormat.MachO => MachOReader.ReadImportedLibraries(image),
            _ => NativeResult.Fail<IReadOnlyList<ImportedLibrary>>(ErrorKinds.InternalError, $"Unsupported binary format '{image.Format}'."),
        };

    private static List<NextActionHint> BuildImportHints(
        NativeImage image,
        string normalizedKind,
        string? nameFilter,
        int pageSize,
        int? nextCursor,
        List<ImportedLibraryRowData>? libraries = null)
    {
        var hints = new List<NextActionHint>();
        if (nextCursor is not null)
        {
            hints.Add(new NextActionHint(
                "imports",
                $"More imported {normalizedKind} available on the next page.",
                new Dictionary<string, object?>
                {
                    ["path"] = image.FilePath,
                    ["kind"] = normalizedKind,
                    ["page-size"] = pageSize,
                    ["cursor"] = nextCursor,
                    ["name-filter"] = nameFilter,
                }));
        }

        if (normalizedKind == "libraries" && libraries is { Count: > 0 })
        {
            var suggestedArguments = new Dictionary<string, object?>
            {
                ["path"] = image.FilePath,
                ["kind"] = "functions",
            };

            var reason = "Switch to imported functions for a deeper dependency walk.";
            if (image.Format == BinaryFormat.Pe)
            {
                suggestedArguments["name-filter"] = libraries[0].Name;
                reason = $"Inspect functions imported from '{libraries[0].Name}'.";
            }

            hints.Add(new NextActionHint("imports", reason, suggestedArguments));
        }

        return hints;
    }
}

public sealed record ImportsCommandData(
    string Kind,
    IReadOnlyList<ImportedFunctionRowData>? Functions,
    IReadOnlyList<ImportedLibraryRowData>? Libraries,
    int TotalCount,
    int? NextCursor);

public sealed record ImportedFunctionRowData(
    string? Library,
    string Name,
    ushort? Ordinal);

public sealed record ImportedLibraryRowData(string Name);
