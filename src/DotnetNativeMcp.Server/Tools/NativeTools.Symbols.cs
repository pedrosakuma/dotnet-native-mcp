using System.ComponentModel;
using System.Globalization;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Strings;
using DotnetNativeMcp.Core.Symbols;
using ModelContextProtocol.Server;

namespace DotnetNativeMcp.Server.Tools;

public sealed partial class NativeTools
{
    [McpServerTool(Name = "list_native_symbols")]
    [Description(
        "Returns a paginated list of symbols from a loaded native binary. " +
        "Source priority: .map sidecar (richest) > ELF .symtab/.dynsym > PE export table. " +
        "Each symbol includes its raw mangled name, best-effort demangled name, RVA, size, and function flag. " +
        "Use the returned cursor to page through large symbol tables.")]
    public NativeResult<ListNativeSymbolsResult> ListNativeSymbols(
        [Description("ImageHandle returned by load_native_binary.")] string imageHandle,
        [Description("Page size (default 100, max 500).")] int pageSize = 100,
        [Description("Opaque pagination cursor from a prior call. Omit or pass 0 for the first page.")] int cursor = 0,
        [Description("Optional case-insensitive name filter substring.")] string? nameFilter = null)
    {
        if (!registry.TryGet(imageHandle, out var image) || image is null)
            return NativeResult.Fail<ListNativeSymbolsResult>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{imageHandle}'. Call load_native_binary first.");

        if (pageSize <= 0) pageSize = 100;
        if (pageSize > 500) pageSize = 500;
        if (cursor < 0) cursor = 0;

        var symbols = image.Symbols;
        IEnumerable<NativeSymbol> filtered = symbols;
        if (!string.IsNullOrEmpty(nameFilter))
        {
            filtered = filtered.Where(s =>
                s.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase) ||
                s.DemangledName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase));
        }

        var all = filtered.ToList();
        var page = all.Skip(cursor).Take(pageSize).ToList();
        var nextCursor = cursor + page.Count < all.Count ? cursor + page.Count : -1;

        var rows = page.Select(s => new SymbolRow(
            s.Index, s.Name, s.DemangledName,
            s.Rva.ToString("x16", CultureInfo.InvariantCulture),
            s.Size, s.Section, s.IsFunction)).ToList();

        var hints = new List<NextActionHint>();
        if (nextCursor > 0)
        {
            hints.Add(new NextActionHint("list_native_symbols", "More symbols available on the next page.",
                new Dictionary<string, object?> { ["imageHandle"] = imageHandle, ["cursor"] = nextCursor, ["pageSize"] = pageSize }));
        }

        return NativeResult.Ok(
            $"Page {cursor}..{cursor + page.Count - 1} of {all.Count} symbol(s) in '{imageHandle}'.",
            new ListNativeSymbolsResult(rows, all.Count, nextCursor < 0 ? null : nextCursor),
            hints);
    }

    [McpServerTool(Name = "list_native_imports")]
    [Description(
        "Returns a paginated list of imported functions or shared-library dependencies from a loaded native binary. " +
        "kind='functions' lists ELF .dynsym undefined imports or PE Import Directory entries; " +
        "kind='libraries' lists ELF DT_NEEDED entries or PE import descriptor names.")]
    public NativeResult<ListNativeImportsResult> ListNativeImports(
        [Description("ImageHandle returned by load_native_binary.")] string imageHandle,
        [Description("Import view to return: functions or libraries. Default functions.")] string kind = "functions",
        [Description("Page size (default 100, max 500).")] int pageSize = 100,
        [Description("Opaque pagination cursor from a prior call. Omit or pass 0 for the first page.")] int cursor = 0,
        [Description("Optional case-insensitive name filter substring.")] string? nameFilter = null)
    {
        if (!registry.TryGet(imageHandle, out var image) || image is null)
            return NativeResult.Fail<ListNativeImportsResult>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{imageHandle}'. Call load_native_binary first.");

        if (!TryNormalizeImportKind(kind, out var normalizedKind, out var kindError))
            return NativeResult.Fail<ListNativeImportsResult>(ErrorKinds.InvalidArgument, kindError!);

        if (pageSize <= 0) pageSize = 100;
        if (pageSize > 500) pageSize = 500;
        if (cursor < 0) cursor = 0;

        if (normalizedKind == "functions")
        {
            var parsed = ReadImportedFunctions(image);
            if (parsed.IsError)
                return new NativeResult<ListNativeImportsResult>(parsed.Summary, EmptyImportResult(normalizedKind), [], parsed.Error);

            IEnumerable<ImportedFunction> filtered = parsed.Data!;
            if (!string.IsNullOrWhiteSpace(nameFilter))
            {
                filtered = filtered.Where(import =>
                    import.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(import.Library) && import.Library.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)));
            }

            var all = filtered.ToList();
            var page = all.Skip(cursor).Take(pageSize)
                .Select(import => new ImportedFunctionRow(import.Library, import.Name, import.Ordinal))
                .ToList();
            var nextCursor = cursor + page.Count < all.Count ? cursor + page.Count : (int?)null;
            var hints = BuildImportHints(image, imageHandle, normalizedKind, nameFilter, pageSize, nextCursor);
            var summary = page.Count == 0
                ? $"No imported functions found in '{imageHandle}'."
                : $"Page {cursor}..{cursor + page.Count - 1} of {all.Count} imported function(s) in '{imageHandle}'.";

            return NativeResult.Ok(summary, new ListNativeImportsResult(normalizedKind, page, null, all.Count, nextCursor), hints);
        }

        var librariesResult = ReadImportedLibraries(image);
        if (librariesResult.IsError)
            return new NativeResult<ListNativeImportsResult>(librariesResult.Summary, EmptyImportResult(normalizedKind), [], librariesResult.Error);

        IEnumerable<ImportedLibrary> libraries = librariesResult.Data!;
        if (!string.IsNullOrWhiteSpace(nameFilter))
            libraries = libraries.Where(import => import.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase));

        var libraryAll = libraries.ToList();
        var libraryPage = libraryAll.Skip(cursor).Take(pageSize)
            .Select(import => new ImportedLibraryRow(import.Name))
            .ToList();
        var libraryNextCursor = cursor + libraryPage.Count < libraryAll.Count ? cursor + libraryPage.Count : (int?)null;
        var libraryHints = BuildImportHints(image, imageHandle, normalizedKind, nameFilter, pageSize, libraryNextCursor, libraryPage);
        var librarySummary = libraryPage.Count == 0
            ? $"No imported libraries found in '{imageHandle}'."
            : $"Page {cursor}..{cursor + libraryPage.Count - 1} of {libraryAll.Count} imported libraries in '{imageHandle}'.";

        return NativeResult.Ok(
            librarySummary,
            new ListNativeImportsResult(normalizedKind, null, libraryPage, libraryAll.Count, libraryNextCursor),
            libraryHints);
    }

    [McpServerTool(Name = "resolve_symbols")]
    [Description(
        "Batch resolves up to 200 addresses against a single loaded native image. " +
        "Accepts hex strings with or without a '0x' prefix, and plain decimal strings. " +
        "Returns one row per address with the raw mangled name, demangled name, section, " +
        "byte displacement from the symbol start, and (when debug info is present) a " +
        "SourceLocation with file, line, and optional SourceLink URL. " +
        "When DWARF .debug_info type metadata is available the row also includes a " +
        "'signature' field with the function's return type and parameter types. " +
        "Per-address failures (bad parse, symbol not found) are reported inline — a bad " +
        "address does not fail the whole batch. Supplying an empty list returns an empty " +
        "result without error. Replaces the former single-address 'resolve_symbol' and " +
        "the batch 'symbolicate_stack' tools.")]
    public NativeResult<ResolveSymbolsResult> ResolveSymbols(
        [Description("ImageHandle returned by load_native_binary.")] string imageHandle,
        [Description("Addresses to resolve (hex with optional 0x prefix, or decimal). Up to 200 entries.")] IReadOnlyList<string> addresses,
        [Description("When true (default), annotates each resolved address with file:line from DWARF/PDB debug info. Set false to skip debug-info I/O.")] bool resolveSource = true,
        [Description("Optional producer-observed module load base from the NativeFrame handoff (hex, with or without a '0x' prefix — matching the contract's transport format). When supplied, each address is treated as a runtime absolute VA and rebased as 'rva = address - loadBase'. Required to resolve ASLR'd position-independent (PIE) binaries whose on-disk image base is 0. Omit for non-ASLR binaries.")] string? loadBase = null)
    {
        if (!registry.TryGet(imageHandle, out var image) || image is null)
            return NativeResult.Fail<ResolveSymbolsResult>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{imageHandle}'. Call load_native_binary first.");

        if (addresses.Count > StackSymbolicator.MaxFrameCount)
            return NativeResult.Fail<ResolveSymbolsResult>(
                ErrorKinds.InvalidArgument,
                $"Address count {addresses.Count} exceeds the maximum of {StackSymbolicator.MaxFrameCount}.");

        ulong? parsedLoadBase = null;
        if (!string.IsNullOrWhiteSpace(loadBase))
        {
            // loadBase is an address and is transported as hex (bare or 0x-prefixed); parse it as
            // hex rather than via the decimal-first address parser so all-digit hex values like
            // "0000000000400000" are not misread as decimal.
            if (!StackSymbolicator.TryParseHexAddress(loadBase, out var lb, out _))
                return NativeResult.Fail<ResolveSymbolsResult>(
                    ErrorKinds.InvalidArgument,
                    $"Cannot parse loadBase '{loadBase}' as a hex value.");
            parsedLoadBase = lb;
        }

        var inner = StackSymbolicator.ResolveAddresses(image, addresses, parsedLoadBase);
        var rows = inner.Data!.Select(r =>
        {
            SourceLocation? src = null;
            string? signature = null;
            if (resolveSource && !r.IsError && r.ResolvedRvaHex is not null)
            {
                var va = image.ImageBase + ulong.Parse(r.ResolvedRvaHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                src = sourceResolver.TrySourceFor(image, va);
                signature = DwarfInfoReader.TryGetSignatureForRva(image, va);
            }

            return new ResolvedAddressRow(
                r.InputAddress,
                r.ResolvedRvaHex,
                r.MangledName,
                r.DemangledName,
                r.SectionName,
                r.Displacement,
                src,
                signature,
                r.Error);
        }).ToList();

        return NativeResult.Ok(
            inner.Summary,
            new ResolveSymbolsResult(rows),
            inner.Hints);
    }

    [McpServerTool(Name = "extract_strings")]
    [Description(
        "Scans read-only data sections of a loaded native image for printable ASCII and UTF-16LE strings. " +
        "Defaults to .rodata/.rdata/.data.rel.ro/__const and falls back to .data only when read-only sections are absent. " +
        "Returns paginated results with section, offset, RVA, encoding, length, and value. Total matches are capped at 500000 per scan.")]
    public NativeResult<ExtractStringsResult> ExtractStrings(
        [Description("ImageHandle returned by load_native_binary.")] string imageHandle,
        [Description("Minimum string length in characters. Default 6, allowed range 1..4096.")] int minLength = 6,
        [Description("Comma-separated encodings to scan: ascii, utf16le. Default 'ascii,utf16le'.")] string encodings = "ascii,utf16le",
        [Description("Optional section name override. When supplied, only that section is scanned.")] string? section = null,
        [Description("Page size. Default 200, max 5000.")] int pageSize = 200,
        [Description("Opaque pagination cursor from a prior call. Omit or pass 0 for the first page.")] int cursor = 0)
    {
        if (!registry.TryGet(imageHandle, out var image) || image is null)
            return NativeResult.Fail<ExtractStringsResult>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{imageHandle}'. Call load_native_binary first.");

        if (minLength < 1 || minLength > 4096)
            return NativeResult.Fail<ExtractStringsResult>(
                ErrorKinds.InvalidArgument,
                $"minLength must be between 1 and 4096. Got {minLength}.");

        if (pageSize < 1 || pageSize > 5000)
            return NativeResult.Fail<ExtractStringsResult>(
                ErrorKinds.InvalidArgument,
                $"pageSize must be between 1 and 5000. Got {pageSize}.");

        if (!TryParseEncodings(encodings, out var scanAscii, out var scanUtf16, out var encodingError))
            return NativeResult.Fail<ExtractStringsResult>(ErrorKinds.InvalidArgument, encodingError!);

        if (!TrySelectSections(image, section, out var sections, out var sectionError))
            return NativeResult.Fail<ExtractStringsResult>(ErrorKinds.InvalidArgument, sectionError!);

        if (cursor < 0)
            cursor = 0;

        List<ExtractedStringRow> allRows = [];
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
                image.GetSectionBytes(selectedSection).Span,
                selectedSection.VirtualAddress,
                selectedSection.Name,
                minLength,
                scanAscii,
                scanUtf16,
                out var sectionTruncated,
                remaining);

            foreach (var extracted in extractedStrings)
            {
                var rva = ParseHex(extracted.RvaHex);
                var offset = rva - selectedSection.VirtualAddress;
                allRows.Add(new ExtractedStringRow(
                    extracted.SectionName,
                    offset.ToString("x16", CultureInfo.InvariantCulture),
                    extracted.RvaHex,
                    extracted.Encoding,
                    extracted.Length,
                    extracted.Value));
            }

            if (sectionTruncated)
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

        var hints = new List<NextActionHint>();
        if (nextCursor is not null)
        {
            hints.Add(new NextActionHint(
                "extract_strings",
                "More extracted strings available on the next page.",
                new Dictionary<string, object?>
                {
                    ["imageHandle"] = imageHandle,
                    ["minLength"] = minLength,
                    ["encodings"] = encodings,
                    ["section"] = section,
                    ["pageSize"] = pageSize,
                    ["cursor"] = nextCursor,
                }));
        }

        var summary = page.Count == 0
            ? $"No extracted strings found in '{imageHandle}'."
            : $"Page {cursor}..{cursor + page.Count - 1} of {ordered.Count} extracted string(s) in '{imageHandle}'{(truncated ? " (truncated)." : ".")}";

        return NativeResult.Ok(summary, new ExtractStringsResult(page, ordered.Count, nextCursor, truncated), hints);
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

    private static ListNativeImportsResult EmptyImportResult(string normalizedKind) =>
        normalizedKind == "functions"
            ? new ListNativeImportsResult(normalizedKind, [], null, 0, null)
            : new ListNativeImportsResult(normalizedKind, null, [], 0, null);

    private static List<NextActionHint> BuildImportHints(
        NativeImage image,
        string imageHandle,
        string normalizedKind,
        string? nameFilter,
        int pageSize,
        int? nextCursor,
        List<ImportedLibraryRow>? libraries = null)
    {
        var hints = new List<NextActionHint>();
        if (nextCursor is not null)
        {
            hints.Add(new NextActionHint("list_native_imports", $"More imported {normalizedKind} available on the next page.",
                new Dictionary<string, object?>
                {
                    ["imageHandle"] = imageHandle,
                    ["kind"] = normalizedKind,
                    ["pageSize"] = pageSize,
                    ["cursor"] = nextCursor,
                    ["nameFilter"] = nameFilter,
                }));
        }

        if (normalizedKind == "libraries" && libraries is { Count: > 0 })
        {
            var suggestedArguments = new Dictionary<string, object?>
            {
                ["imageHandle"] = imageHandle,
                ["kind"] = "functions",
            };

            var reason = "Switch to imported functions for a deeper dependency walk.";
            if (image.Format == BinaryFormat.Pe)
            {
                suggestedArguments["nameFilter"] = libraries[0].Name;
                reason = $"Inspect functions imported from '{libraries[0].Name}'.";
            }

            hints.Add(new NextActionHint("list_native_imports", reason, suggestedArguments));
        }

        return hints;
    }

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
            .Where(static s => string.Equals(s.Name, ".rodata", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Name, ".rdata", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Name, ".data.rel.ro", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Name, "__const", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var hasReadOnlyData = image.Sections.Any(static s =>
            string.Equals(s.Name, ".rodata", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.Name, ".rdata", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.Name, ".data.rel.ro", StringComparison.OrdinalIgnoreCase));

        if (!hasReadOnlyData)
        {
            selectedSections.AddRange(image.Sections.Where(static s =>
                string.Equals(s.Name, ".data", StringComparison.OrdinalIgnoreCase)));
        }

        sections = selectedSections;
        return true;
    }
}

/// <summary>One row returned by <c>list_native_symbols</c>.</summary>
public sealed record SymbolRow(
    int Index,
    string Name,
    string DemangledName,
    string RvaHex,
    ulong Size,
    string? Section,
    bool IsFunction);

/// <summary>Result payload for <c>list_native_symbols</c>.</summary>
public sealed record ListNativeSymbolsResult(
    IReadOnlyList<SymbolRow> Symbols,
    int TotalCount,
    int? NextCursor);

/// <summary>One imported function row returned by <c>list_native_imports</c>.</summary>
public sealed record ImportedFunctionRow(
    string? Library,
    string Name,
    ushort? Ordinal);

/// <summary>One imported library row returned by <c>list_native_imports</c>.</summary>
public sealed record ImportedLibraryRow(
    string Name);

/// <summary>Result payload for <c>list_native_imports</c>.</summary>
public sealed record ListNativeImportsResult(
    string Kind,
    IReadOnlyList<ImportedFunctionRow>? Functions,
    IReadOnlyList<ImportedLibraryRow>? Libraries,
    int TotalCount,
    int? NextCursor);

/// <summary>One resolved address row returned by <c>resolve_symbols</c>.</summary>
/// <param name="InputAddress">The original address string as supplied by the caller.</param>
/// <param name="ResolvedRvaHex">Normalized 16-digit hex RVA, or <c>null</c> when parsing failed.</param>
/// <param name="MangledName">Raw mangled symbol name on success.</param>
/// <param name="DemangledName">Best-effort demangled name on success.</param>
/// <param name="SectionName">Containing section name when available.</param>
/// <param name="Displacement">Byte offset from the start of the resolved symbol.</param>
/// <param name="Source">Source file+line from DWARF/PDB debug info, when available and resolveSource=true.</param>
/// <param name="Signature">
/// DWARF-derived function signature (<c>ReturnType Name(ParamType, ...)</c>),
/// or <c>null</c> when DWARF type info is absent or the address is not inside a
/// known subprogram. Only populated when <c>resolveSource=true</c>.
/// </param>
/// <param name="Error">Per-row error; <c>null</c> on success.</param>
public sealed record ResolvedAddressRow(
    string InputAddress,
    string? ResolvedRvaHex,
    string? MangledName,
    string? DemangledName,
    string? SectionName,
    ulong? Displacement,
    SourceLocation? Source,
    string? Signature,
    NativeError? Error);

/// <summary>Result payload for <c>resolve_symbols</c>.</summary>
public sealed record ResolveSymbolsResult(IReadOnlyList<ResolvedAddressRow> Resolutions);

/// <summary>One row returned by <c>extract_strings</c>.</summary>
public sealed record ExtractedStringRow(
    string SectionName,
    string OffsetHex,
    string RvaHex,
    string Encoding,
    int Length,
    string Value);

/// <summary>Result payload for <c>extract_strings</c>.</summary>
public sealed record ExtractStringsResult(
    IReadOnlyList<ExtractedStringRow> Strings,
    int TotalCount,
    int? NextCursor,
    bool Truncated);
