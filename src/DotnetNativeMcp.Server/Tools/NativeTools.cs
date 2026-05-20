using System.ComponentModel;
using System.Globalization;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Diff;
using DotnetNativeMcp.Core.Disassembly;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Mstat;
using DotnetNativeMcp.Core.Strings;
using DotnetNativeMcp.Core.Symbols;
using ModelContextProtocol.Server;

namespace DotnetNativeMcp.Server.Tools;

/// <summary>
/// V0 MCP tools for navigating native .NET binaries (NativeAOT and ReadyToRun).
/// Accepts <c>NativeFrame</c> handoffs from <c>dotnet-diagnostics-mcp</c>.
/// </summary>
[McpServerToolType]
public sealed class NativeTools(INativeBinaryRegistry registry)
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

    [McpServerTool(Name = "resolve_symbol")]
    [Description(
        "Resolves a symbol by mangled name OR by hex address (RVA or absolute VA). " +
        "Returns the raw name, demangled name, RVA, size, and section. " +
        "Applies NativeAOT ILC demangling to surface a managed-looking name. " +
        "When resolving by address the nearest symbol whose range contains the address is returned.")]
    public NativeResult<ResolveSymbolResult> ResolveSymbol(
        [Description("ImageHandle returned by load_native_binary.")] string imageHandle,
        [Description("Mangled or demangled symbol name to look up. Mutually exclusive with 'address'.")] string? name = null,
        [Description("Hex address (RVA or absolute VA, no 0x prefix). Mutually exclusive with 'name'.")] string? address = null)
    {
        if (!registry.TryGet(imageHandle, out var image) || image is null)
            return NativeResult.Fail<ResolveSymbolResult>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{imageHandle}'. Call load_native_binary first.");

        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(address))
            return NativeResult.Fail<ResolveSymbolResult>(
                ErrorKinds.InvalidArgument, "Supply either 'name' or 'address'.");

        NativeSymbol? sym = null;

        if (!string.IsNullOrEmpty(name))
        {
            sym = SymbolResolution.FindByName(image.Symbols, name);
            if (sym is null)
                return NativeResult.Fail<ResolveSymbolResult>(
                    ErrorKinds.SymbolNotFound, $"Symbol '{name}' not found in '{imageHandle}'.");
        }
        else
        {
            if (!ulong.TryParse(address, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var va))
                return NativeResult.Fail<ResolveSymbolResult>(
                    ErrorKinds.InvalidArgument, $"Cannot parse address '{address}' as a hex value.");

            var rva = SymbolResolution.VaToRva(va, image.ImageBase);
            sym = SymbolResolution.FindByRva(image.Symbols, rva);
            if (sym is null)
                return NativeResult.Fail<ResolveSymbolResult>(
                    ErrorKinds.SymbolNotFound, $"No symbol found at address 0x{va:x}.");
        }

        var rvaHex = sym.Rva.ToString("x16", CultureInfo.InvariantCulture);
        var section = sym.Section ?? image.FindSection(sym.Rva)?.Name;

        return NativeResult.Ok(
            $"Resolved '{sym.Name}' at RVA 0x{sym.Rva:x}.",
            new ResolveSymbolResult(sym.Index, sym.Name, sym.DemangledName, rvaHex, sym.Size, section, sym.IsFunction),
            [new NextActionHint("disassemble", "Disassemble native code at this symbol.",
                new Dictionary<string, object?> { ["imageHandle"] = imageHandle, ["address"] = rvaHex })]);
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
                "resolve_symbol",
                "Inspect the largest size-changed symbol in the current image.",
                new Dictionary<string, object?>
                {
                    ["imageHandle"] = currentHandle,
                    ["name"] = comparison.ChangedSymbols[0].Name,
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

    [McpServerTool(Name = "symbolicate_stack")]
    [Description(
        "Batch resolves up to 200 native frames from dotnet-diagnostics-mcp NativeFrame payloads " +
        "or a list of raw hex addresses against a single loaded image. " +
        "Each row carries its own success or error state so malformed or missing frames do not fail the batch.")]
    public NativeResult<IReadOnlyList<SymbolicatedFrame>> SymbolicateStack(
        [Description("Frames to symbolicate. Each row needs an address and may override the imageHandle.")] IReadOnlyList<NativeFrameInput> frames,
        [Description("Optional imageHandle applied to frames that omit imageHandle.")] string? defaultImageHandle = null) =>
        StackSymbolicator.SymbolicateStack(registry, frames, defaultImageHandle);

    [McpServerTool(Name = "disassemble")]
    [Description(
        "Disassembles native machine code using Iced (x86/x64 only). " +
        "Supply either an RVA or a symbol name to center the window. " +
        "Each instruction includes absolute address, raw bytes, mnemonic, operands, " +
        "and a cross-ref hint for CALL/JMP targets that can be resolved against the symbol table. " +
        "Default: 64 instructions. Max: 2048. ARM64 returns 'disassembly_unsupported'.")]
    public NativeResult<IReadOnlyList<InstructionView>> Disassemble(
        [Description("ImageHandle returned by load_native_binary.")] string imageHandle,
        [Description("Hex RVA or absolute VA (no 0x prefix) to start disassembly.")] string? address = null,
        [Description("Symbol name to disassemble (looked up then resolved to its RVA). Mutually exclusive with 'address'.")] string? symbolName = null,
        [Description("Maximum instructions to decode. Default 64, capped at 2048.")] int maxInstructions = IcedDisassembler.DefaultMaxInstructions)
    {
        if (!registry.TryGet(imageHandle, out var image) || image is null)
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{imageHandle}'. Call load_native_binary first.");

        ulong rva;

        if (!string.IsNullOrEmpty(symbolName))
        {
            var sym = SymbolResolution.FindByName(image.Symbols, symbolName);
            if (sym is null)
                return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                    ErrorKinds.SymbolNotFound, $"Symbol '{symbolName}' not found.");
            rva = sym.Rva;
        }
        else if (!string.IsNullOrEmpty(address))
        {
            if (!ulong.TryParse(address, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var va))
                return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                    ErrorKinds.InvalidArgument, $"Cannot parse address '{address}' as a hex value.");
            rva = SymbolResolution.VaToRva(va, image.ImageBase);
        }
        else
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.InvalidArgument, "Supply either 'address' or 'symbolName'.");
        }

        return IcedDisassembler.Disassemble(image, rva, maxInstructions);
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

/// <summary>Result payload for <c>load_native_binary</c>.</summary>
/// <param name="ImageHandle">Opaque handle for subsequent tool calls.</param>
/// <param name="Format">Binary format: <c>Elf</c> or <c>Pe</c>.</param>
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

/// <summary>Result payload for <c>resolve_symbol</c>.</summary>
public sealed record ResolveSymbolResult(
    int Index,
    string Name,
    string DemangledName,
    string RvaHex,
    ulong Size,
    string? Section,
    bool IsFunction);

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
