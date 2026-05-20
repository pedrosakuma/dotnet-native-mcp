using System.ComponentModel;
using System.Globalization;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Dgml;
using DotnetNativeMcp.Core.Diff;
using DotnetNativeMcp.Core.Disassembly;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Mstat;
using DotnetNativeMcp.Core.Strings;
using DotnetNativeMcp.Core.Symbols;
using DotnetNativeMcp.Core.Xref;
using ModelContextProtocol.Server;

namespace DotnetNativeMcp.Server.Tools;

/// <summary>
/// V0 MCP tools for navigating native .NET binaries (NativeAOT and ReadyToRun).
/// Accepts <c>NativeFrame</c> handoffs from <c>dotnet-diagnostics-mcp</c>.
/// </summary>
[McpServerToolType]
public sealed class NativeTools(INativeBinaryRegistry registry, NativeCallGraphCache callGraphCache, SourceResolver sourceResolver)
{
    [McpServerTool(Name = "load_native_binary")]
    [Description(
        "Opens a PE or ELF native binary, verifies it is a managed-flavored native build " +
        "(NativeAOT or ReadyToRun), and returns an ImageHandle used by all other tools. " +
        "Rejects arbitrary system .so/.dll files with 'not_a_native_dotnet_image'. " +
        "Optionally validates the build-id against a value from dotnet-diagnostics-mcp " +
        "to prevent stale-binary mistakes.")]
    public NativeResult<LoadNativeBinaryResult> LoadNativeBinary(
        [Description("Absolute path to the native binary on disk.")] string path,
        [Description("Optional build-id (hex) from dotnet-diagnostics-mcp NativeFrame.buildId. When supplied, the loaded binary's build-id must match or binary_mismatch is returned.")] string? buildId = null)
    {
        var result = registry.Load(path, buildId);
        if (result.IsError)
            return NativeResult.Fail<LoadNativeBinaryResult>(result.Error!.Kind, result.Error.Message, result.Error.Detail);

        var image = result.Data!;
        var data = new LoadNativeBinaryResult(
            image.Handle.Value,
            image.Format.ToString(),
            image.Architecture.ToString(),
            image.Handle.BuildIdHex,
            image.Symbols.Count,
            image.Sections.Count);

        return NativeResult.Ok(result.Summary, data, result.Hints);
    }

    [McpServerTool(Name = "import_native_manifest")]
    [Description(
        "Bulk handshake from a producer (typically dotnet-diagnostics-mcp): registers a list of " +
        "native binaries in one call. " +
        "mode='lazy' (default) records path hints without opening each file; " +
        "mode='eager' opens every entry immediately and verifies build-ids. " +
        "Per-entry failures are reported inline — one bad entry does not fail the whole batch.")]
    public NativeResult<ImportManifestData> ImportNativeManifest(
        [Description("Manifest entries. Each entry has a 'path' and optional 'name' and 'buildId'.")] IReadOnlyList<BatchManifestEntry> entries,
        [Description("'lazy' (default) records path hints without opening binaries; 'eager' opens and verifies each entry immediately.")] string mode = "lazy")
    {
        var normalizedMode = mode.Trim().ToLowerInvariant();
        if (normalizedMode is not ("lazy" or "eager"))
            return NativeResult.Fail<ImportManifestData>(ErrorKinds.InvalidArgument,
                $"mode must be 'lazy' or 'eager'. Actual: '{mode}'.");

        var isEager = normalizedMode == "eager";
        var results = new List<BatchLoadEntry>(entries.Count);
        var loadedCount = 0;

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Path))
            {
                results.Add(new BatchLoadEntry(
                    entry.Path ?? string.Empty,
                    entry.Name,
                    null,
                    "failed",
                    new NativeError(ErrorKinds.InvalidArgument, "entry path must not be empty.", null)));
                continue;
            }

            if (isEager)
            {
                var loadResult = registry.Load(entry.Path, entry.BuildId);
                if (loadResult.IsError)
                {
                    // Remap binary_mismatch to build_id_mismatch for per-entry clarity
                    var errKind = loadResult.Error!.Kind == ErrorKinds.BinaryMismatch
                        ? ErrorKinds.BuildIdMismatch
                        : loadResult.Error.Kind;
                    results.Add(new BatchLoadEntry(entry.Path, entry.Name, null, "failed",
                        new NativeError(errKind, loadResult.Error.Message, loadResult.Error.Detail)));
                }
                else
                {
                    loadedCount++;
                    results.Add(new BatchLoadEntry(
                        entry.Path, entry.Name, loadResult.Data!.Handle.Value, "loaded", null));
                }
            }
            else
            {
                registry.RegisterHint(entry.Path, entry.BuildId);
                loadedCount++;
                results.Add(new BatchLoadEntry(entry.Path, entry.Name, null, "registered", null));
            }
        }

        var total = entries.Count;
        var verb = isEager ? "Loaded" : "Registered";
        var summary = $"{verb} {loadedCount} of {total} entries.";
        return NativeResult.Ok(summary, new ImportManifestData(results, loadedCount, total));
    }

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
        "Per-address failures (bad parse, symbol not found) are reported inline — a bad " +
        "address does not fail the whole batch. Supplying an empty list returns an empty " +
        "result without error. Replaces the former single-address 'resolve_symbol' and " +
        "the batch 'symbolicate_stack' tools.")]
    public NativeResult<ResolveSymbolsResult> ResolveSymbols(
        [Description("ImageHandle returned by load_native_binary.")] string imageHandle,
        [Description("Addresses to resolve (hex with optional 0x prefix, or decimal). Up to 200 entries.")] IReadOnlyList<string> addresses,
        [Description("When true (default), annotates each resolved address with file:line from DWARF/PDB debug info. Set false to skip debug-info I/O.")] bool resolveSource = true)
    {
        if (!registry.TryGet(imageHandle, out var image) || image is null)
            return NativeResult.Fail<ResolveSymbolsResult>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{imageHandle}'. Call load_native_binary first.");

        if (addresses.Count > StackSymbolicator.MaxFrameCount)
            return NativeResult.Fail<ResolveSymbolsResult>(
                ErrorKinds.InvalidArgument,
                $"Address count {addresses.Count} exceeds the maximum of {StackSymbolicator.MaxFrameCount}.");

        var inner = StackSymbolicator.ResolveAddresses(image, addresses);
        var rows = inner.Data!.Select(r =>
        {
            SourceLocation? src = null;
            if (resolveSource && !r.IsError && r.ResolvedRvaHex is not null)
            {
                var va = image.ImageBase + ulong.Parse(r.ResolvedRvaHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                src = sourceResolver.TrySourceFor(image, va);
            }

            return new ResolvedAddressRow(
                r.InputAddress,
                r.ResolvedRvaHex,
                r.MangledName,
                r.DemangledName,
                r.SectionName,
                r.Displacement,
                src,
                r.Error);
        }).ToList();

        return NativeResult.Ok(
            inner.Summary,
            new ResolveSymbolsResult(rows),
            inner.Hints);
    }

    [McpServerTool(Name = "get_size_breakdown")]
    [Description(
        "Reads the NativeAOT .mstat sidecar paired with a loaded binary and returns aggregated native size by assembly, namespace, type, or method. " +
        "Defaults to method grouping and the top 25 rows. Max topN: 500.")]
    public NativeResult<GetSizeBreakdownResult> GetSizeBreakdown(
        [Description("ImageHandle returned by load_native_binary.")] string imageHandle,
        [Description("Grouping: assembly, namespace, type, or method. Default: method.")] string groupBy = "method",
        [Description("Maximum rows to return. Default 25, capped at 500.")] int topN = MstatReader.DefaultTopN,
        [Description("Optional absolute path override for the .mstat sidecar. Defaults to a sibling file next to the loaded binary.")] string? mstatPath = null)
    {
        if (!registry.TryGet(imageHandle, out var image) || image is null)
            return NativeResult.Fail<GetSizeBreakdownResult>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{imageHandle}'. Call load_native_binary first.");

        if (!TryParseGroupBy(groupBy, out var grouping))
            return NativeResult.Fail<GetSizeBreakdownResult>(
                ErrorKinds.InvalidArgument,
                $"groupBy must be one of: assembly, namespace, type, method. Actual: '{groupBy}'.");

        var resolvedMstatPath = string.IsNullOrWhiteSpace(mstatPath)
            ? MstatReader.GetDefaultMstatPath(image.FilePath)
            : Path.GetFullPath(mstatPath);

        var mstat = MstatReader.Read(resolvedMstatPath);
        if (mstat.IsError)
            return NativeResult.Fail<GetSizeBreakdownResult>(mstat.Error!.Kind, mstat.Error.Message, mstat.Error.Detail);

        var rows = MstatReader.Aggregate(mstat.Data!.Attributions, grouping, topN)
            .Select(row => new SizeBreakdownRow(
                row.Key,
                row.AssemblyName,
                row.NamespaceName,
                row.TypeName,
                row.MethodName,
                row.TotalSize,
                row.AttributionCount))
            .ToList();

        var hints = new List<NextActionHint>();
        if (grouping != MstatGroupBy.Method)
        {
            hints.Add(new NextActionHint(
                "get_size_breakdown",
                "Drill into per-method size buckets for this image.",
                new Dictionary<string, object?>
                {
                    ["imageHandle"] = imageHandle,
                    ["groupBy"] = "method",
                    ["mstatPath"] = resolvedMstatPath,
                }));
        }

        return NativeResult.Ok(
            $"Returned {rows.Count} {grouping.ToString().ToLowerInvariant()} size bucket(s) from '{Path.GetFileName(resolvedMstatPath)}'.",
            new GetSizeBreakdownResult(
                grouping.ToString().ToLowerInvariant(),
                resolvedMstatPath,
                mstat.Data.TotalSize,
                rows),
            hints);
    }

    [McpServerTool(Name = "explain_retention")]
    [Description(
        "Reads the NativeAOT DGML reachability sidecar paired with a loaded binary and returns the shortest retained-by path " +
        "from any root to the matched node. Target matches exact DGML node id or a case-insensitive substring on node label.")]
    public NativeResult<ExplainRetentionResult> ExplainRetention(
        [Description("ImageHandle returned by load_native_binary.")] string imageHandle,
        [Description("Target DGML node query. Matches exact node id or a case-insensitive substring on the node label.")] string target,
        [Description("Optional absolute path override for the .dgml sidecar. Defaults to a sibling file next to the loaded binary.")] string? dgmlPath = null,
        [Description("Maximum edge depth to search from any root. Default 12, valid range 1..64.")] int maxDepth = RetentionPathFinder.DefaultMaxDepth)
    {
        if (!registry.TryGet(imageHandle, out var image) || image is null)
            return NativeResult.Fail<ExplainRetentionResult>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{imageHandle}'. Call load_native_binary first.");

        if (string.IsNullOrWhiteSpace(target))
            return NativeResult.Fail<ExplainRetentionResult>(
                ErrorKinds.InvalidArgument,
                "target must not be empty.");

        if (maxDepth < 1 || maxDepth > RetentionPathFinder.MaxDepthLimit)
        {
            return NativeResult.Fail<ExplainRetentionResult>(
                ErrorKinds.InvalidArgument,
                $"maxDepth must be between 1 and {RetentionPathFinder.MaxDepthLimit}. Actual: {maxDepth.ToString(CultureInfo.InvariantCulture)}.");
        }

        var resolvedDgmlPath = string.IsNullOrWhiteSpace(dgmlPath)
            ? DgmlReader.GetDefaultDgmlPath(image.FilePath)
            : Path.GetFullPath(dgmlPath);

        var dgml = DgmlReader.Read(resolvedDgmlPath);
        if (dgml.IsError)
            return NativeResult.Fail<ExplainRetentionResult>(dgml.Error!.Kind, dgml.Error.Message, dgml.Error.Detail);

        var targetMatchCount = RetentionPathFinder.CountTargetMatches(dgml.Data!, target);
        var path = RetentionPathFinder.FindShortestPath(dgml.Data!, target, maxDepth)
            .Select(segment => new RetentionPathNodeRow(
                segment.NodeId,
                segment.Label,
                segment.Category,
                segment.IncomingEdgeLabel))
            .ToList();

        var matchedNode = path.Count > 0 ? path[^1] : null;
        var summary = path.Count > 0
            ? $"Found a retention path with {path.Count} node(s) to '{matchedNode!.Label}' from '{Path.GetFileName(resolvedDgmlPath)}'."
            : $"No retention path found for '{target}' in '{Path.GetFileName(resolvedDgmlPath)}'.";

        return NativeResult.Ok(
            summary,
            new ExplainRetentionResult(
                resolvedDgmlPath,
                target,
                targetMatchCount,
                matchedNode?.Id,
                matchedNode?.Label,
                matchedNode?.Category,
                path));
    }

    private static bool TryParseGroupBy(string value, out MstatGroupBy groupBy)
    {
        var normalized = value.Trim().ToLowerInvariant();
        groupBy = normalized switch
        {
            "assembly" => MstatGroupBy.Assembly,
            "namespace" => MstatGroupBy.Namespace,
            "type" => MstatGroupBy.Type,
            "method" => MstatGroupBy.Method,
            _ => default,
        };

        return normalized is "assembly" or "namespace" or "type" or "method";
    }

    [McpServerTool(Name = "compare_native_binaries")]
    [Description(
        "Diffs two loaded native images and reports build-id, format, architecture, file-size, section-size, and symbol-size deltas. " +
        "Use it to spot release-over-release native binary regressions and identify the symbols that grew the most.")]
    public NativeResult<CompareNativeBinariesResult> CompareNativeBinaries(
        [Description("Baseline ImageHandle returned by load_native_binary.")] string baselineHandle,
        [Description("Current ImageHandle returned by load_native_binary.")] string currentHandle,
        [Description("Maximum added, removed, and changed symbols to return per category. Must be between 1 and 500.")] int topN = 50)
    {
        if (!registry.TryGet(baselineHandle, out var baseline) || baseline is null)
            return NativeResult.Fail<CompareNativeBinariesResult>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{baselineHandle}'. Call load_native_binary first.");

        if (!registry.TryGet(currentHandle, out var current) || current is null)
            return NativeResult.Fail<CompareNativeBinariesResult>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{currentHandle}'. Call load_native_binary first.");

        if (topN < 1 || topN > 500)
            return NativeResult.Fail<CompareNativeBinariesResult>(
                ErrorKinds.InvalidArgument,
                $"topN must be between 1 and 500. Actual: {topN.ToString(CultureInfo.InvariantCulture)}.");

        var comparison = NativeBinaryComparer.Compare(baseline, current, topN);
        var data = new CompareNativeBinariesResult(
            comparison.Verdict.ToString().ToLowerInvariant(),
            new ValueDeltaResult(comparison.BaselineBuildIdHex, comparison.CurrentBuildIdHex, comparison.IsBuildIdEqual),
            new ValueDeltaResult(comparison.BaselineFormat.ToString(), comparison.CurrentFormat.ToString(), comparison.IsFormatEqual),
            new ValueDeltaResult(comparison.BaselineArchitecture.ToString(), comparison.CurrentArchitecture.ToString(), comparison.IsArchitectureEqual),
            comparison.BaselineBinarySizeBytes,
            comparison.CurrentBinarySizeBytes,
            comparison.TotalBinarySizeDeltaBytes,
            comparison.SectionDeltas.Select(delta => new SectionSizeDeltaRow(
                delta.Name,
                delta.BaselineSizeBytes,
                delta.CurrentSizeBytes,
                delta.SizeDeltaBytes)).ToList(),
            comparison.AddedSymbolCount,
            comparison.RemovedSymbolCount,
            comparison.ChangedSymbolCount,
            comparison.AddedSymbols.Select(symbol => new SymbolInventoryRow(
                symbol.Name,
                symbol.DemangledName,
                ToHex(symbol.Rva),
                symbol.SizeBytes,
                symbol.Section,
                symbol.IsFunction)).ToList(),
            comparison.RemovedSymbols.Select(symbol => new SymbolInventoryRow(
                symbol.Name,
                symbol.DemangledName,
                ToHex(symbol.Rva),
                symbol.SizeBytes,
                symbol.Section,
                symbol.IsFunction)).ToList(),
            comparison.ChangedSymbols.Select(symbol => new ChangedSymbolDeltaRow(
                symbol.Name,
                symbol.DemangledName,
                ToHex(symbol.BaselineRva),
                ToHex(symbol.CurrentRva),
                symbol.RvaDeltaBytes,
                symbol.BaselineSizeBytes,
                symbol.CurrentSizeBytes,
                symbol.SizeDeltaBytes,
                symbol.BaselineSection,
                symbol.CurrentSection,
                symbol.IsFunction)).ToList());

        var hints = new List<NextActionHint>();
        if (comparison.ChangedSymbols.Count > 0)
        {
            hints.Add(new NextActionHint(
                "resolve_symbols",
                "Inspect the largest size-changed symbol in the current image.",
                new Dictionary<string, object?>
                {
                    ["imageHandle"] = currentHandle,
                    ["addresses"] = new[] { ToHex(comparison.ChangedSymbols[0].CurrentRva) },
                }));
        }

        return NativeResult.Ok(
            $"{comparison.AddedSymbolCount} added, {comparison.RemovedSymbolCount} removed, {comparison.ChangedSymbolCount} size-changed (Δ {FormatByteDelta(comparison.TotalBinarySizeDeltaBytes)}).",
            data,
            hints);
    }

    [McpServerTool(Name = "extract_strings")]
    [Description(
        "Scans read-only data sections of a loaded native image for printable ASCII and UTF-16LE strings. " +
        "Defaults to .rodata/.rdata/.data.rel.ro/__const and falls back to .data only when read-only sections are absent. " +
        "Returns paginated results with section, offset, RVA, encoding, length, and value.")]
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
        foreach (var selectedSection in sections)
        {
            foreach (var extracted in StringExtractor.Extract(
                image.GetSectionBytes(selectedSection).Span,
                selectedSection.VirtualAddress,
                selectedSection.Name,
                minLength,
                scanAscii,
                scanUtf16))
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
            : $"Page {cursor}..{cursor + page.Count - 1} of {ordered.Count} extracted string(s) in '{imageHandle}'.";

        return NativeResult.Ok(summary, new ExtractStringsResult(page, ordered.Count, nextCursor), hints);
    }

    [McpServerTool(Name = "disassemble")]
    [Description(
        "Disassembles native machine code using Iced (x86/x64 only). " +
        "Two modes: (1) registered-handle mode — supply imageHandle (returned by load_native_binary) " +
        "plus address or symbolName; (2) raw-bytes mode — supply imagePath + rva + size directly, " +
        "bypassing load_native_binary (works on any PE/ELF/Mach-O including managed PEs with R2R bodies). " +
        "Exactly one of {imageHandle, imagePath} must be present. " +
        "Each instruction includes absolute address, raw bytes, mnemonic, operands, " +
        "and a cross-ref hint for CALL/JMP targets that can be resolved against the symbol table. " +
        "When resolveSource is true each instruction is optionally annotated with file:line from DWARF debug info. " +
        "Default: 64 instructions. Max: 2048. ARM64 returns 'disassembly_unsupported'.")]
    public NativeResult<IReadOnlyList<InstructionView>> Disassemble(
        [Description("ImageHandle returned by load_native_binary.")] string? imageHandle = null,
        [Description("Hex RVA or absolute VA (no 0x prefix) to start disassembly.")] string? address = null,
        [Description("Symbol name to disassemble (looked up then resolved to its RVA). Mutually exclusive with 'address'.")] string? symbolName = null,
        [Description("Maximum instructions to decode. Default 64, capped at 2048.")] int maxInstructions = IcedDisassembler.DefaultMaxInstructions,
        [Description("When true, annotates each instruction with file:line from DWARF debug info (may be noisy). Default false.")] bool resolveSource = false,
        [Description("Absolute path to a PE, ELF, or Mach-O binary (raw-bytes mode). Mutually exclusive with imageHandle.")] string? imagePath = null,
        [Description("Start RVA within imagePath (required when imagePath is supplied).")] int? rva = null,
        [Description("Number of code bytes to decode from imagePath (required when imagePath is supplied).")] int? size = null,
        [Description("CPU architecture override for imagePath mode: 'x64', 'x86', or 'arm64'. Detected from the binary header when omitted.")] string? architecture = null,
        [Description("Image base for absolute-address formatting in imagePath mode. Detected from the binary header when omitted.")] ulong? baseAddress = null)
    {
        var hasHandle = !string.IsNullOrEmpty(imageHandle);
        var hasPath = !string.IsNullOrEmpty(imagePath);

        if (hasHandle && hasPath)
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.InvalidArgument,
                "Supply exactly one of 'imageHandle' or 'imagePath', not both.");

        if (!hasHandle && !hasPath)
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.InvalidArgument,
                "Supply either 'imageHandle' (registered-handle mode) or 'imagePath' (raw-bytes mode).");

        // ── Raw-bytes mode ───────────────────────────────────────────────────────
        if (hasPath)
        {
            if (rva is null || size is null)
                return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                    ErrorKinds.InvalidArgument,
                    "When 'imagePath' is supplied, both 'rva' and 'size' are required.");

            if (size.Value <= 0)
                return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                    ErrorKinds.InvalidArgument,
                    $"'size' must be > 0; got {size.Value}.");

            Core.Imaging.Architecture? parsedArch = null;
            if (!string.IsNullOrEmpty(architecture))
            {
                parsedArch = architecture.Trim().ToLowerInvariant() switch
                {
                    "x64" or "amd64" => Core.Imaging.Architecture.X64,
                    "x86" or "i386" => Core.Imaging.Architecture.X86,
                    "arm64" or "aarch64" => Core.Imaging.Architecture.Arm64,
                    _ => null,
                };
                if (parsedArch is null)
                    return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                        ErrorKinds.InvalidArgument,
                        $"Unknown architecture '{architecture}'. Valid values: x64, x86, arm64.");
            }

            return RawDisassembler.Disassemble(
                imagePath!,
                rva.Value,
                size.Value,
                parsedArch,
                baseAddress,
                maxInstructions);
        }

        // ── Registered-handle mode ────────────────────────────────────────────────
        if (!registry.TryGet(imageHandle!, out var image) || image is null)
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{imageHandle}'. Call load_native_binary first.");

        ulong resolvedRva;

        if (!string.IsNullOrEmpty(symbolName))
        {
            var sym = SymbolResolution.FindByName(image.Symbols, symbolName);
            if (sym is null)
                return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                    ErrorKinds.SymbolNotFound, $"Symbol '{symbolName}' not found.");
            resolvedRva = sym.Rva;
        }
        else if (!string.IsNullOrEmpty(address))
        {
            if (!ulong.TryParse(address, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var va))
                return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                    ErrorKinds.InvalidArgument, $"Cannot parse address '{address}' as a hex value.");
            resolvedRva = SymbolResolution.VaToRva(va, image.ImageBase);
        }
        else
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.InvalidArgument, "Supply either 'address' or 'symbolName'.");
        }

        var disasmResult = IcedDisassembler.Disassemble(image, resolvedRva, maxInstructions);
        if (disasmResult.IsError || !resolveSource)
            return disasmResult;

        var annotated = disasmResult.Data!.Select(instr =>
        {
            if (!ulong.TryParse(instr.AddressHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var instrVa))
                return instr;

            var loc = sourceResolver.TrySourceFor(image, instrVa);
            return loc is null ? instr : instr with { Source = loc };
        }).ToList();

        return NativeResult.Ok(disasmResult.Summary, (IReadOnlyList<InstructionView>)annotated, disasmResult.Hints);
    }

    [McpServerTool(Name = "find_native_callers")]
    [Description(
        "Scans all executable sections of a loaded native image using static disassembly (Iced, x86/x64 only) " +
        "and returns every CALL/JMP instruction whose target resolves to the requested symbol or address. " +
        "The full xref index is built lazily on the first call and cached per image handle; " +
        "subsequent calls for the same image are O(callers). " +
        "The index is persisted to disk under ~/.cache/dotnet-native-mcp/<build-id>.xref so large " +
        "NativeAOT binaries pay the scan cost only once across sessions. " +
        "Set DOTNET_NATIVE_MCP_XREF_CACHE=0 to disable the disk cache. " +
        "ARM64 returns 'disassembly_unsupported'. Use 'disassemble' to inspect any returned call site. " +
        "When resolveSource is true (default) each call site is annotated with file:line from DWARF/PDB debug info. " +
        "Set resolveSource=false to skip debug-info I/O for large binaries where PDB reads are slow.")]
    public NativeResult<FindCallersResult> FindNativeCallers(
        [Description("ImageHandle returned by load_native_binary.")] string imageHandle,
        [Description(
            "Target to find callers of. " +
            "Accepts a raw mangled or demangled symbol name, a hex address (0x prefix optional), or a decimal address.")] string target,
        [Description("When true (default), annotates each call site with file:line from DWARF/PDB debug info. Set false to skip debug-info I/O.")] bool resolveSource = true)
    {
        if (!registry.TryGet(imageHandle, out var image) || image is null)
            return NativeResult.Fail<FindCallersResult>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{imageHandle}'. Call load_native_binary first.");

        if (string.IsNullOrWhiteSpace(target))
            return NativeResult.Fail<FindCallersResult>(
                ErrorKinds.InvalidArgument,
                "target must not be empty.");

        if (image.Architecture is not (Architecture.X64 or Architecture.X86))
            return NativeResult.Fail<FindCallersResult>(
                ErrorKinds.DisassemblyUnsupported,
                $"Disassembly for {image.Architecture} is not supported in V0. Only x86/x64 is implemented.");

        // Resolve target to an absolute virtual address.
        ulong targetVa;
        NativeSymbol? targetSym;

        if (StackSymbolicator.TryParseAddress(target, out var parsedValue, out _))
        {
            // Normalise: if parsedValue < imageBase treat as RVA, otherwise as VA.
            var rva = SymbolResolution.VaToRva(parsedValue, image.ImageBase);
            targetVa = image.ImageBase + rva;

            // Try to attribute the address to a symbol (best-effort; null is fine).
            targetSym = SymbolResolution.FindByRva(image.Symbols, rva);

            // Validate that the resolved RVA is inside a known section.
            if (image.FindSection(rva) is null)
                return NativeResult.Fail<FindCallersResult>(
                    ErrorKinds.AddressOutOfRange,
                    $"Address 0x{parsedValue:x} is outside the known sections of '{imageHandle}'.");
        }
        else
        {
            // Try by symbol name (mangled or demangled).
            targetSym = SymbolResolution.FindByName(image.Symbols, target);
            if (targetSym is null)
                return NativeResult.Fail<FindCallersResult>(
                    ErrorKinds.SymbolNotFound,
                    $"Symbol '{target}' not found in '{imageHandle}'. Use list_native_symbols to browse.");

            targetVa = image.ImageBase + targetSym.Rva;
        }

        var callers = callGraphCache.FindCallers(image, targetVa);

        var targetAddrHex = targetVa.ToString("x16", CultureInfo.InvariantCulture);
        var displayName = targetSym?.Name ?? target;

        var rows = callers
            .Select(site =>
            {
                SourceLocation? src = null;
                if (resolveSource && ulong.TryParse(site.SourceAddressHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var siteVa))
                    src = sourceResolver.TrySourceFor(image, siteVa);

                return new CallSiteRow(
                    site.SourceAddressHex,
                    site.CallerSymbol,
                    site.CallerDemangled,
                    site.Mnemonic,
                    site.Operands,
                    site.RawBytes,
                    src);
            })
            .ToList();

        var hints = new List<NextActionHint>();
        if (rows.Count > 0)
        {
            hints.Add(new NextActionHint(
                "disassemble",
                $"Disassemble the first call site at 0x{rows[0].SourceAddressHex}.",
                new Dictionary<string, object?>
                {
                    ["imageHandle"] = imageHandle,
                    ["address"] = rows[0].SourceAddressHex,
                }));
        }

        return NativeResult.Ok(
            $"Found {rows.Count} caller(s) of '{displayName}' in '{imageHandle}'.",
            new FindCallersResult(targetAddrHex, targetSym?.Name, targetSym?.DemangledName, rows.Count, rows),
            hints);
    }

    private static string ToHex(ulong value) => value.ToString("x16", CultureInfo.InvariantCulture);

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

    private static ulong ParseHex(string hex) => ulong.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

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

/// <summary>Result payload for <c>load_native_binary</c> (single-path mode).</summary>
/// <param name="ImageHandle">Opaque handle for subsequent tool calls.</param>
/// <param name="Format">Binary format: <c>Elf</c>, <c>Pe</c>, or <c>MachO</c>.</param>
/// <param name="Architecture">CPU architecture: <c>X64</c>, <c>X86</c>, <c>Arm64</c>, or <c>Unknown</c>.</param>
/// <param name="BuildIdHex">Build-id as lowercase hex.</param>
/// <param name="SymbolCount">Total symbol count after loading.</param>
/// <param name="SectionCount">Total section count.</param>
public sealed record LoadNativeBinaryResult(
    string ImageHandle,
    string Format,
    string Architecture,
    string BuildIdHex,
    int SymbolCount,
    int SectionCount);

/// <summary>One entry in a manifest supplied to <c>import_native_manifest</c>.</summary>
/// <param name="Path">Absolute path to the native binary on disk.</param>
/// <param name="Name">Optional display name for the binary (defaults to the file name).</param>
/// <param name="BuildId">Optional expected build-id hex. When supplied and mode is 'eager', the loaded binary's build-id must match.</param>
public sealed record BatchManifestEntry(
    string Path,
    string? Name = null,
    string? BuildId = null);

/// <summary>Per-entry outcome in a batch manifest import.</summary>
/// <param name="Path">Absolute path supplied in the manifest entry.</param>
/// <param name="Name">Optional display name from the manifest entry.</param>
/// <param name="BinaryHandle">ImageHandle when the binary was successfully loaded (eager mode); <c>null</c> in lazy mode or on failure.</param>
/// <param name="Status"><c>loaded</c> (eager success), <c>registered</c> (lazy success), or <c>failed</c>.</param>
/// <param name="Error">Populated on failure; <c>null</c> on success.</param>
public sealed record BatchLoadEntry(
    string Path,
    string? Name,
    string? BinaryHandle,
    string Status,
    NativeError? Error);

/// <summary>Result payload for <c>import_native_manifest</c>.</summary>
/// <param name="Entries">Per-entry outcomes in the same order as the input manifest.</param>
/// <param name="LoadedCount">Number of entries that succeeded (loaded or registered).</param>
/// <param name="TotalCount">Total number of entries submitted.</param>
public sealed record ImportManifestData(
    IReadOnlyList<BatchLoadEntry> Entries,
    int LoadedCount,
    int TotalCount);

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

/// <summary>Simple baseline/current delta payload.</summary>
public sealed record ValueDeltaResult(
    string Baseline,
    string Current,
    bool IsEqual);

/// <summary>Section size delta row for <c>compare_native_binaries</c>.</summary>
public sealed record SectionSizeDeltaRow(
    string Name,
    ulong BaselineSizeBytes,
    ulong CurrentSizeBytes,
    long SizeDeltaBytes);

/// <summary>Added or removed symbol row for <c>compare_native_binaries</c>.</summary>
public sealed record SymbolInventoryRow(
    string Name,
    string DemangledName,
    string RvaHex,
    ulong SizeBytes,
    string? Section,
    bool IsFunction);

/// <summary>Changed symbol row for <c>compare_native_binaries</c>.</summary>
public sealed record ChangedSymbolDeltaRow(
    string Name,
    string DemangledName,
    string BaselineRvaHex,
    string CurrentRvaHex,
    long RvaDeltaBytes,
    ulong BaselineSizeBytes,
    ulong CurrentSizeBytes,
    long SizeDeltaBytes,
    string? BaselineSection,
    string? CurrentSection,
    bool IsFunction);

/// <summary>Result payload for <c>compare_native_binaries</c>.</summary>
public sealed record CompareNativeBinariesResult(
    string Verdict,
    ValueDeltaResult BuildIdDelta,
    ValueDeltaResult FormatDelta,
    ValueDeltaResult ArchitectureDelta,
    long BaselineBinarySizeBytes,
    long CurrentBinarySizeBytes,
    long TotalBinarySizeDeltaBytes,
    IReadOnlyList<SectionSizeDeltaRow> SectionDeltas,
    int AddedSymbolCount,
    int RemovedSymbolCount,
    int ChangedSymbolCount,
    IReadOnlyList<SymbolInventoryRow> AddedSymbols,
    IReadOnlyList<SymbolInventoryRow> RemovedSymbols,
    IReadOnlyList<ChangedSymbolDeltaRow> ChangedSymbols);

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
    int? NextCursor);

/// <summary>Result payload for <c>get_size_breakdown</c>.</summary>
public sealed record GetSizeBreakdownResult(
    string GroupBy,
    string MstatPath,
    long TotalAttributedBytes,
    IReadOnlyList<SizeBreakdownRow> Rows);

/// <summary>One aggregated row returned by <c>get_size_breakdown</c>.</summary>
public sealed record SizeBreakdownRow(
    string Key,
    string AssemblyName,
    string NamespaceName,
    string TypeName,
    string? MethodName,
    long TotalSize,
    int AttributionCount);

/// <summary>Result payload for <c>explain_retention</c>.</summary>
public sealed record ExplainRetentionResult(
    string DgmlPath,
    string TargetQuery,
    int TargetMatchCount,
    string? MatchedNodeId,
    string? MatchedNodeLabel,
    string? MatchedNodeCategory,
    IReadOnlyList<RetentionPathNodeRow> Path);

/// <summary>One node in a DGML retention path.</summary>
public sealed record RetentionPathNodeRow(
    string Id,
    string Label,
    string? Category,
    string? EdgeLabelFromPrevious);

/// <summary>One resolved address row returned by <c>resolve_symbols</c>.</summary>
/// <param name="InputAddress">The original address string as supplied by the caller.</param>
/// <param name="ResolvedRvaHex">Normalized 16-digit hex RVA, or <c>null</c> when parsing failed.</param>
/// <param name="MangledName">Raw mangled symbol name on success.</param>
/// <param name="DemangledName">Best-effort demangled name on success.</param>
/// <param name="SectionName">Containing section name when available.</param>
/// <param name="Displacement">Byte offset from the start of the resolved symbol.</param>
/// <param name="Source">Source file+line from DWARF/PDB debug info, when available and resolveSource=true.</param>
/// <param name="Error">Per-row error; <c>null</c> on success.</param>
public sealed record ResolvedAddressRow(
    string InputAddress,
    string? ResolvedRvaHex,
    string? MangledName,
    string? DemangledName,
    string? SectionName,
    ulong? Displacement,
    SourceLocation? Source,
    NativeError? Error);

/// <summary>Result payload for <c>resolve_symbols</c>.</summary>
public sealed record ResolveSymbolsResult(IReadOnlyList<ResolvedAddressRow> Resolutions);

/// <summary>One call-site row returned by <c>find_native_callers</c>.</summary>
/// <param name="SourceAddressHex">Absolute virtual address of the calling instruction, lowercase hex.</param>
/// <param name="CallerSymbol">Raw mangled name of the enclosing function, or <c>null</c>.</param>
/// <param name="CallerDemangled">Best-effort demangled name of the enclosing function, or <c>null</c>.</param>
/// <param name="Mnemonic">Lowercase transfer-of-control mnemonic (e.g. <c>call</c>, <c>jmp</c>).</param>
/// <param name="Operands">Formatted operand text.</param>
/// <param name="RawBytes">Hex-encoded raw bytes of the instruction.</param>
/// <param name="Source">Source file+line from DWARF/PDB debug info, when available.</param>
public sealed record CallSiteRow(
    string SourceAddressHex,
    string? CallerSymbol,
    string? CallerDemangled,
    string Mnemonic,
    string Operands,
    string RawBytes,
    SourceLocation? Source = null);

/// <summary>Result payload for <c>find_native_callers</c>.</summary>
/// <param name="TargetAddressHex">Resolved absolute virtual address of the target, lowercase hex.</param>
/// <param name="TargetSymbol">Raw mangled name of the target symbol, or <c>null</c> when only an address was supplied.</param>
/// <param name="TargetDemangled">Best-effort demangled name of the target symbol, or <c>null</c>.</param>
/// <param name="TotalCallers">Total number of call-sites found.</param>
/// <param name="Callers">The list of call-sites that target this address.</param>
public sealed record FindCallersResult(
    string TargetAddressHex,
    string? TargetSymbol,
    string? TargetDemangled,
    int TotalCallers,
    IReadOnlyList<CallSiteRow> Callers);
