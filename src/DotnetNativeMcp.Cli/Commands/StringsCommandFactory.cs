using System.CommandLine;
using System.Globalization;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Strings;

namespace DotnetNativeMcp.Cli;

public static class StringsCommandFactory
{
    public static Command Create(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var pathArgument = new Argument<string>("path")
        {
            Description = "Path to the native binary to scan.",
        };
        var minLengthOption = new Option<int>("--min-length")
        {
            Description = "Minimum string length in characters. Default 6, allowed range 1..4096.",
            DefaultValueFactory = _ => 6,
        };
        var encodingsOption = new Option<string>("--encodings")
        {
            Description = "Comma-separated encodings to scan: ascii, utf16le. Default 'ascii,utf16le'.",
            DefaultValueFactory = _ => "ascii,utf16le",
        };
        var sectionOption = new Option<string?>("--section")
        {
            Description = "Optional section name override. When supplied, only that section is scanned.",
        };
        var pageSizeOption = new Option<int>("--page-size", aliases: ["--limit"])
        {
            Description = "Page size. Default 200, max 5000.",
            DefaultValueFactory = _ => 200,
        };
        var cursorOption = new Option<int>("--cursor")
        {
            Description = "Opaque pagination cursor from a prior call. Omit or pass 0 for the first page.",
            DefaultValueFactory = _ => 0,
        };

        var command = new Command("strings", "Extract printable ASCII and UTF-16LE strings from a native binary.");
        command.Arguments.Add(pathArgument);
        command.Options.Add(minLengthOption);
        command.Options.Add(encodingsOption);
        command.Options.Add(sectionOption);
        command.Options.Add(pageSizeOption);
        command.Options.Add(cursorOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var invocation = CliApplication.BuildInvocationContext(parseResult, options);
            var path = parseResult.GetValue(pathArgument);
            var result = ExtractStrings(
                invocation,
                path!,
                parseResult.GetValue(minLengthOption),
                parseResult.GetValue(encodingsOption) ?? "ascii,utf16le",
                parseResult.GetValue(sectionOption),
                parseResult.GetValue(pageSizeOption),
                parseResult.GetValue(cursorOption));

            await invocation.OutputWriter.WriteAsync(result, cancellationToken).ConfigureAwait(false);
            return 0;
        });

        return command;
    }

    private static NativeResult<StringsCommandData> ExtractStrings(
        CliInvocationContext invocation,
        string path,
        int minLength,
        string encodings,
        string? section,
        int pageSize,
        int cursor)
    {
        var registry = new NativeBinaryRegistry(invocation.PathPolicy);
        var load = registry.Load(path);
        if (load.IsError)
            return NativeResult.Fail<StringsCommandData>(load.Error!.Kind, load.Error.Message, load.Error.Detail);

        if (minLength < 1 || minLength > 4096)
        {
            return NativeResult.Fail<StringsCommandData>(
                ErrorKinds.InvalidArgument,
                $"minLength must be between 1 and 4096. Got {minLength.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (pageSize < 1 || pageSize > 5000)
        {
            return NativeResult.Fail<StringsCommandData>(
                ErrorKinds.InvalidArgument,
                $"pageSize must be between 1 and 5000. Got {pageSize.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (!TryParseEncodings(encodings, out var scanAscii, out var scanUtf16, out var encodingError))
            return NativeResult.Fail<StringsCommandData>(ErrorKinds.InvalidArgument, encodingError!);

        if (!TrySelectSections(load.Data!, section, out var sections, out var sectionError))
            return NativeResult.Fail<StringsCommandData>(ErrorKinds.InvalidArgument, sectionError!);

        if (cursor < 0)
            cursor = 0;

        List<ExtractedStringCommandRow> allRows = [];
        var truncated = false;
        foreach (var selectedSection in sections)
        {
            var remaining = ResourceLimits.MaxStringMatches - allRows.Count;
            if (remaining <= 0)
            {
                truncated = true;
                break;
            }

            var extractedStrings = StringExtractor.Extract(
                load.Data!.GetSectionBytes(selectedSection).Span,
                selectedSection.VirtualAddress,
                selectedSection.Name,
                minLength,
                scanAscii,
                scanUtf16,
                out _,
                out var sectionMatchCapReached,
                remaining);

            foreach (var extracted in extractedStrings)
            {
                var rva = ParseHex(extracted.RvaHex);
                var offset = rva - selectedSection.VirtualAddress;
                allRows.Add(new ExtractedStringCommandRow(
                    extracted.SectionName,
                    offset.ToString("x16", CultureInfo.InvariantCulture),
                    extracted.RvaHex,
                    extracted.Encoding,
                    extracted.Length,
                    extracted.Value));
            }

            if (sectionMatchCapReached)
            {
                truncated = true;
                break;
            }
        }

        var ordered = allRows
            .OrderBy(static row => row.SectionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => ParseHex(row.RvaHex))
            .ToList();

        if (cursor > ordered.Count)
            cursor = ordered.Count;

        var page = ordered.Skip(cursor).Take(pageSize).ToList();
        var nextCursor = cursor + page.Count < ordered.Count ? cursor + page.Count : (int?)null;
        var fileName = Path.GetFileName(load.Data!.FilePath);

        var hints = new List<NextActionHint>();
        if (nextCursor is not null)
        {
            hints.Add(new NextActionHint(
                "strings",
                "More extracted strings available on the next page.",
                new Dictionary<string, object?>
                {
                    ["path"] = load.Data.FilePath,
                    ["minLength"] = minLength,
                    ["encodings"] = encodings,
                    ["section"] = section,
                    ["pageSize"] = pageSize,
                    ["cursor"] = nextCursor,
                }));
        }

        var summary = page.Count == 0
            ? $"No extracted strings found in '{fileName}'."
            : $"Page {cursor.ToString(CultureInfo.InvariantCulture)}..{(cursor + page.Count - 1).ToString(CultureInfo.InvariantCulture)} of {ordered.Count.ToString(CultureInfo.InvariantCulture)} extracted string(s) in '{fileName}'{(truncated ? " (truncated)." : ".")}";

        return NativeResult.Ok(
            summary,
            new StringsCommandData(page, ordered.Count, nextCursor, truncated),
            hints);
    }

    private static ulong ParseHex(string hex) =>
        ulong.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static bool TryParseEncodings(string encodings, out bool ascii, out bool utf16, out string? error)
    {
        ascii = false;
        utf16 = false;
        error = null;

        foreach (var token in encodings.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "ascii":
                    ascii = true;
                    break;
                case "utf16":
                case "utf16le":
                    utf16 = true;
                    break;
                default:
                    error = $"Unsupported encoding '{token}'. Supported values: ascii, utf16le.";
                    return false;
            }
        }

        if (!ascii && !utf16)
        {
            error = "At least one encoding must be selected. Supported values: ascii, utf16le.";
            return false;
        }

        return true;
    }

    private static bool TrySelectSections(
        NativeImage image,
        string? section,
        out IReadOnlyList<NativeSection> sections,
        out string? error)
    {
        error = null;

        if (!string.IsNullOrWhiteSpace(section))
        {
            var explicitSections = image.Sections
                .Where(s => string.Equals(s.Name, section, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (explicitSections.Length == 0)
            {
                sections = [];
                error = $"Section '{section}' was not found in '{image.Handle.Value}'.";
                return false;
            }

            sections = explicitSections;
            return true;
        }

        var selectedSections = image.Sections
            .Where(static s => IsDefaultStringSection(s.Name))
            .ToList();

        if (selectedSections.Count == 0)
        {
            selectedSections.AddRange(image.Sections.Where(static s =>
                string.Equals(s.Name, ".data", StringComparison.OrdinalIgnoreCase)));
        }

        sections = selectedSections;
        return true;
    }

    private static bool IsDefaultStringSection(string sectionName) =>
        string.Equals(sectionName, ".rodata", StringComparison.OrdinalIgnoreCase)
        || string.Equals(sectionName, ".rdata", StringComparison.OrdinalIgnoreCase)
        || string.Equals(sectionName, ".data.rel.ro", StringComparison.OrdinalIgnoreCase)
        || string.Equals(sectionName, "__const", StringComparison.OrdinalIgnoreCase)
        || sectionName.EndsWith(",__const", StringComparison.OrdinalIgnoreCase);
}

public sealed record ExtractedStringCommandRow(
    string SectionName,
    string OffsetHex,
    string RvaHex,
    string Encoding,
    int Length,
    string Value);

public sealed record StringsCommandData(
    IReadOnlyList<ExtractedStringCommandRow> Strings,
    int TotalCount,
    int? NextCursor,
    bool Truncated);
