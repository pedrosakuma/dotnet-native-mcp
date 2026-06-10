using System.ComponentModel;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.R2R;
using ModelContextProtocol.Server;

namespace DotnetNativeMcp.Server.Tools;

public sealed partial class NativeTools
{
    [McpServerTool(Name = "get_r2r_header")]
    [Description(
        "Reads the ReadyToRun (R2R) header from a loaded managed PE binary and returns its version, " +
        "flags (raw value plus decoded READYTORUN_FLAG_* names such as Component, Partial, EmbeddedMsil), " +
        "and the full sections table. " +
        "Also decodes the CompilerIdentifier (type 100) and, for composite component images, the " +
        "OwnerCompositeExecutable (type 116) identification strings when present. " +
        "When includeImportSections is true, also decodes the ImportSections section (type 101) into " +
        "per-entry fixup-region metadata (RVA/size, decoded Type and Flags, EntrySize, Signatures and " +
        "AuxiliaryData RVAs); individual fixup signatures are not decoded. " +
        "When includeCompositeInfo is true, also decodes the composite-image ComponentAssemblies " +
        "(type 115) entries and ManifestAssemblyMvids (type 118) GUIDs when present. " +
        "When includeMethodEntryPoints is true, also decodes the MethodDefEntryPoints section (type 103) — " +
        "a NativeFormat array mapping each MethodDef RID to its entry-point RUNTIME_FUNCTION index and a " +
        "has-fixups flag (capped by methodEntryPointsLimit). " +
        "When includeAvailableTypes is true, also decodes the AvailableTypes section (type 108) — " +
        "a NativeFormat hashtable of the types compiled into the image — into metadata tokens " +
        "(TypeDef table 0x02 or ExportedType table 0x27); type names are not resolved (capped by availableTypesLimit). " +
        "When includeInfoMaps is true, also decodes the V9 RID-indexed info maps when present: " +
        "EnclosingTypeMap (type 122, nested-type -> enclosing-type), MethodIsGenericMap (type 121, generic methods) " +
        "and TypeGenericInfoMap (type 123, per-type generic arity/variance/constraints), emitting metadata tokens " +
        "for handoff (capped by infoMapsLimit). " +
        "When includeManifestMetadata is true, also surfaces the ManifestMetadata section (type 112) — " +
        "the embedded ECMA-335 metadata blob — as a handoff descriptor (file offset, RVA, size, version " +
        "string and stream directory); the managed metadata itself is not decoded (hand off to dotnet-assembly-mcp). " +
        "When includeHotColdMap is true, also decodes the HotColdMap section (type 120) — the (cold, hot) " +
        "RUNTIME_FUNCTION index pairs that map a split method's cold partition back to its hot partition " +
        "(capped by infoMapsLimit). " +
        "Returns r2r_not_present when the image has no R2R header (pure managed assembly or NativeAOT binary). " +
        "Use list_r2r_runtime_functions to navigate the RuntimeFunctions section.")]
    public NativeResult<R2RHeaderResult> GetR2RHeader(
        [Description("ImageHandle returned by load_native_binary.")] string imageHandle,
        [Description("When true, also decode and return the ImportSections (type 101) entries. Default false.")]
        bool includeImportSections = false,
        [Description("When true, also decode the composite-image ComponentAssemblies (type 115) and ManifestAssemblyMvids (type 118). Default false.")]
        bool includeCompositeInfo = false,
        [Description("When true, also decode the MethodDefEntryPoints (type 103) RID -> RUNTIME_FUNCTION mapping. Default false.")]
        bool includeMethodEntryPoints = false,
        [Description("Maximum MethodDefEntryPoints entries to return (default 200, max 2000). Ignored unless includeMethodEntryPoints is true.")]
        int methodEntryPointsLimit = 200,
        [Description("When true, also decode the AvailableTypes (type 108) section into metadata tokens. Default false.")]
        bool includeAvailableTypes = false,
        [Description("Maximum AvailableTypes entries to return (default 200, max 2000). Ignored unless includeAvailableTypes is true.")]
        int availableTypesLimit = 200,
        [Description("When true, also decode the V9 info maps (EnclosingTypeMap 122, MethodIsGenericMap 121, TypeGenericInfoMap 123) when present. Default false.")]
        bool includeInfoMaps = false,
        [Description("Maximum entries to return per info map (default 200, max 2000). Ignored unless includeInfoMaps is true.")]
        int infoMapsLimit = 200,
        [Description("When true, also surface the ManifestMetadata (type 112) embedded ECMA blob as a handoff descriptor. Default false.")]
        bool includeManifestMetadata = false,
        [Description("When true, also decode the HotColdMap (type 120) (cold, hot) RUNTIME_FUNCTION index pairs. Default false.")]
        bool includeHotColdMap = false)
    {
        if (!registry.TryGet(imageHandle, out var image) || image is null)
            return NativeResult.Fail<R2RHeaderResult>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{imageHandle}'. Call load_native_binary first.");

        var headerResult = ReadyToRunReader.ReadHeader(image);
        if (headerResult.IsError)
            return NativeResult.Fail<R2RHeaderResult>(
                headerResult.Error!.Kind, headerResult.Error.Message, headerResult.Error.Detail);

        var hdr = headerResult.Data!;
        var hasRtFuncs = hdr.FindSection(ReadyToRunSectionType.RuntimeFunctions) is not null;
        var hasImportSections = hdr.FindSection(ReadyToRunSectionType.ImportSections) is not null;
        var hasComponentAssemblies = hdr.FindSection(ReadyToRunSectionType.ComponentAssemblies) is not null;
        var hasManifestMvids = hdr.FindSection(ReadyToRunSectionType.ManifestAssemblyMvids) is not null;
        var hasCompositeInfo = hasComponentAssemblies || hasManifestMvids;
        var hasMethodEntryPoints = hdr.FindSection(ReadyToRunSectionType.MethodDefEntryPoints) is not null;
        var hasAvailableTypes = hdr.FindSection(ReadyToRunSectionType.AvailableTypes) is not null;
        var hasEnclosingTypeMap = hdr.FindSection(ReadyToRunSectionType.EnclosingTypeMap) is not null;
        var hasMethodIsGenericMap = hdr.FindSection(ReadyToRunSectionType.MethodIsGenericMap) is not null;
        var hasTypeGenericInfoMap = hdr.FindSection(ReadyToRunSectionType.TypeGenericInfoMap) is not null;
        var hasInfoMaps = hasEnclosingTypeMap || hasMethodIsGenericMap || hasTypeGenericInfoMap;
        var hasManifestMetadata = hdr.FindSection(ReadyToRunSectionType.ManifestMetadata) is not null;
        var hasHotColdMap = hdr.FindSection(ReadyToRunSectionType.HotColdMap) is not null;

        var hints = new List<NextActionHint>
        {
            new("get_r2r_header",
                "Re-run with the same handle to refresh.",
                new Dictionary<string, object?> { ["imageHandle"] = imageHandle }),
        };

        if (hasRtFuncs)
        {
            hints.Add(new NextActionHint(
                "list_r2r_runtime_functions",
                "Enumerate or look up RuntimeFunction entries in this R2R image.",
                new Dictionary<string, object?> { ["imageHandle"] = imageHandle }));
        }

        if (hasImportSections && !includeImportSections)
        {
            hints.Add(new NextActionHint(
                "get_r2r_header",
                "Re-run with includeImportSections=true to decode the ImportSections (type 101) entries.",
                new Dictionary<string, object?>
                {
                    ["imageHandle"] = imageHandle,
                    ["includeImportSections"] = true,
                }));
        }

        if (hasCompositeInfo && !includeCompositeInfo)
        {
            hints.Add(new NextActionHint(
                "get_r2r_header",
                "Re-run with includeCompositeInfo=true to decode the composite-image ComponentAssemblies (type 115) and ManifestAssemblyMvids (type 118).",
                new Dictionary<string, object?>
                {
                    ["imageHandle"] = imageHandle,
                    ["includeCompositeInfo"] = true,
                }));
        }

        if (hasMethodEntryPoints && !includeMethodEntryPoints)
        {
            hints.Add(new NextActionHint(
                "get_r2r_header",
                "Re-run with includeMethodEntryPoints=true to decode the MethodDefEntryPoints (type 103) RID -> RUNTIME_FUNCTION mapping.",
                new Dictionary<string, object?>
                {
                    ["imageHandle"] = imageHandle,
                    ["includeMethodEntryPoints"] = true,
                }));
        }

        if (hasAvailableTypes && !includeAvailableTypes)
        {
            hints.Add(new NextActionHint(
                "get_r2r_header",
                "Re-run with includeAvailableTypes=true to decode the AvailableTypes (type 108) metadata tokens.",
                new Dictionary<string, object?>
                {
                    ["imageHandle"] = imageHandle,
                    ["includeAvailableTypes"] = true,
                }));
        }

        if (hasInfoMaps && !includeInfoMaps)
        {
            hints.Add(new NextActionHint(
                "get_r2r_header",
                "Re-run with includeInfoMaps=true to decode the V9 info maps (EnclosingTypeMap 122, MethodIsGenericMap 121, TypeGenericInfoMap 123).",
                new Dictionary<string, object?>
                {
                    ["imageHandle"] = imageHandle,
                    ["includeInfoMaps"] = true,
                }));
        }

        if (hasManifestMetadata && !includeManifestMetadata)
        {
            hints.Add(new NextActionHint(
                "get_r2r_header",
                "Re-run with includeManifestMetadata=true to surface the ManifestMetadata (type 112) embedded ECMA blob descriptor.",
                new Dictionary<string, object?>
                {
                    ["imageHandle"] = imageHandle,
                    ["includeManifestMetadata"] = true,
                }));
        }

        if (hasHotColdMap && !includeHotColdMap)
        {
            hints.Add(new NextActionHint(
                "get_r2r_header",
                "Re-run with includeHotColdMap=true to decode the HotColdMap (type 120) (cold, hot) RUNTIME_FUNCTION index pairs.",
                new Dictionary<string, object?>
                {
                    ["imageHandle"] = imageHandle,
                    ["includeHotColdMap"] = true,
                }));
        }

        var sections = hdr.Sections
            .Select(s => new R2RSectionView(s.Type, s.TypeName, $"0x{s.VirtualAddress:X8}", s.Size))
            .ToList();

        var flagNames = ReadyToRunHeaderAttributesExtensions.DecodeNames(hdr.Flags);

        List<R2RImportSectionView>? importSections = null;
        if (includeImportSections && hasImportSections)
        {
            var importResult = ReadyToRunReader.ReadImportSections(image, hdr);
            if (importResult.IsError)
                return NativeResult.Fail<R2RHeaderResult>(
                    importResult.Error!.Kind, importResult.Error.Message, importResult.Error.Detail);

            importSections = importResult.Data!.Select(s => new R2RImportSectionView(
                s.Index,
                $"0x{s.SectionRva:X8}",
                s.SectionSize,
                s.Flags,
                ReadyToRunImportSectionDecoder.DecodeFlagNames(s.Flags),
                s.Type,
                ReadyToRunImportSectionDecoder.TypeName(s.Type),
                s.EntrySize,
                $"0x{s.SignaturesRva:X8}",
                $"0x{s.AuxiliaryDataRva:X8}")).ToList();
        }

        var compilerIdentifier = ReadyToRunReader.ReadCompilerIdentifier(image, hdr);
        var ownerCompositeExecutable = ReadyToRunReader.ReadOwnerCompositeExecutable(image, hdr);

        List<R2RComponentAssemblyView>? componentAssemblies = null;
        List<string>? manifestAssemblyMvids = null;
        if (includeCompositeInfo)
        {
            if (hasComponentAssemblies)
            {
                var caResult = ReadyToRunReader.ReadComponentAssemblies(image, hdr);
                if (caResult.IsError)
                    return NativeResult.Fail<R2RHeaderResult>(
                        caResult.Error!.Kind, caResult.Error.Message, caResult.Error.Detail);

                componentAssemblies = caResult.Data!.Select(c => new R2RComponentAssemblyView(
                    c.Index,
                    $"0x{c.CorHeaderRva:X8}",
                    c.CorHeaderSize,
                    $"0x{c.AssemblyHeaderRva:X8}",
                    c.AssemblyHeaderSize)).ToList();
            }

            if (hasManifestMvids)
            {
                var mvidResult = ReadyToRunReader.ReadManifestAssemblyMvids(image, hdr);
                if (mvidResult.IsError)
                    return NativeResult.Fail<R2RHeaderResult>(
                        mvidResult.Error!.Kind, mvidResult.Error.Message, mvidResult.Error.Detail);

                manifestAssemblyMvids = mvidResult.Data!.Select(g => g.ToString("D")).ToList();
            }
        }

        R2RMethodEntryPointsView? methodEntryPoints = null;
        if (includeMethodEntryPoints && hasMethodEntryPoints)
        {
            var limit = Math.Clamp(methodEntryPointsLimit, 1, 2000);
            var mepResult = ReadyToRunReader.ReadMethodDefEntryPoints(image, hdr, limit);
            if (mepResult.IsError)
                return NativeResult.Fail<R2RHeaderResult>(
                    mepResult.Error!.Kind, mepResult.Error.Message, mepResult.Error.Detail);

            var table = mepResult.Data!;
            methodEntryPoints = new R2RMethodEntryPointsView(
                table.MethodCount,
                table.Entries.Count,
                table.Truncated,
                table.Entries.Select(e => new R2RMethodEntryPointView(
                    e.Rid, e.RuntimeFunctionIndex, e.HasFixups)).ToList());
        }

        R2RAvailableTypesView? availableTypes = null;
        if (includeAvailableTypes && hasAvailableTypes)
        {
            var limit = Math.Clamp(availableTypesLimit, 1, 2000);
            var atResult = ReadyToRunReader.ReadAvailableTypes(image, hdr, limit);
            if (atResult.IsError)
                return NativeResult.Fail<R2RHeaderResult>(
                    atResult.Error!.Kind, atResult.Error.Message, atResult.Error.Detail);

            var table = atResult.Data!;
            availableTypes = new R2RAvailableTypesView(
                table.Types.Count,
                table.Truncated,
                table.Types.Select(t => new R2RAvailableTypeView(
                    $"0x{t.MetadataToken:X8}", t.IsExportedType)).ToList());
        }

        R2RInfoMapsView? infoMaps = null;
        if (includeInfoMaps && hasInfoMaps)
        {
            var limit = Math.Clamp(infoMapsLimit, 1, 2000);

            R2REnclosingTypeMapView? enclosingMap = null;
            if (hasEnclosingTypeMap)
            {
                var r = ReadyToRunReader.ReadEnclosingTypeMap(image, hdr, limit);
                if (r.IsError)
                    return NativeResult.Fail<R2RHeaderResult>(r.Error!.Kind, r.Error.Message, r.Error.Detail);
                var t = r.Data!;
                enclosingMap = new R2REnclosingTypeMapView(
                    t.TypeDefCount, t.NestedTypes.Count, t.Truncated,
                    t.NestedTypes.Select(n => new R2RNestedTypeView(
                        $"0x{n.NestedTypeToken:X8}", $"0x{n.EnclosingTypeToken:X8}")).ToList());
            }

            R2RMethodIsGenericMapView? methodGenericMap = null;
            if (hasMethodIsGenericMap)
            {
                var r = ReadyToRunReader.ReadMethodIsGenericMap(image, hdr, limit);
                if (r.IsError)
                    return NativeResult.Fail<R2RHeaderResult>(r.Error!.Kind, r.Error.Message, r.Error.Detail);
                var t = r.Data!;
                methodGenericMap = new R2RMethodIsGenericMapView(
                    t.MethodDefCount, t.GenericMethodCount, t.Truncated,
                    t.GenericMethodTokens.Select(x => $"0x{x:X8}").ToList());
            }

            R2RTypeGenericInfoMapView? typeGenericMap = null;
            if (hasTypeGenericInfoMap)
            {
                var r = ReadyToRunReader.ReadTypeGenericInfoMap(image, hdr, limit);
                if (r.IsError)
                    return NativeResult.Fail<R2RHeaderResult>(r.Error!.Kind, r.Error.Message, r.Error.Detail);
                var t = r.Data!;
                typeGenericMap = new R2RTypeGenericInfoMapView(
                    t.TypeDefCount, t.GenericTypeCount, t.Truncated,
                    t.GenericTypes.Select(g => new R2RTypeGenericInfoView(
                        $"0x{g.TypeToken:X8}", g.GenericArgCount, g.HasVariance, g.HasConstraints)).ToList());
            }

            infoMaps = new R2RInfoMapsView(enclosingMap, methodGenericMap, typeGenericMap);
        }

        R2RManifestMetadataView? manifestMetadata = null;
        if (includeManifestMetadata && hasManifestMetadata)
        {
            var r = ReadyToRunReader.ReadManifestMetadata(image, hdr);
            if (r.IsError)
                return NativeResult.Fail<R2RHeaderResult>(r.Error!.Kind, r.Error.Message, r.Error.Detail);
            var m = r.Data!;
            manifestMetadata = new R2RManifestMetadataView(
                $"0x{m.FileOffset:X8}",
                $"0x{m.Rva:X8}",
                m.Size,
                m.MajorVersion,
                m.MinorVersion,
                m.Version,
                m.Streams.Select(s => new R2RMetadataStreamView(s.Name, $"0x{s.Offset:X8}", s.Size)).ToList());
        }

        R2RHotColdMapView? hotColdMap = null;
        if (includeHotColdMap && hasHotColdMap)
        {
            var limit = Math.Clamp(infoMapsLimit, 1, 2000);
            var r = ReadyToRunReader.ReadHotColdMap(image, hdr, limit);
            if (r.IsError)
                return NativeResult.Fail<R2RHeaderResult>(r.Error!.Kind, r.Error.Message, r.Error.Detail);
            var t = r.Data!;
            hotColdMap = new R2RHotColdMapView(
                t.PairCount,
                t.Truncated,
                t.Pairs.Select(p => new R2RHotColdPairView(
                    p.ColdRuntimeFunctionIndex, p.HotRuntimeFunctionIndex)).ToList());
        }

        var data = new R2RHeaderResult(
            imageHandle,
            hdr.Version,
            hdr.MajorVersion,
            hdr.MinorVersion,
            hdr.Flags,
            $"0x{hdr.Flags:X8}",
            flagNames,
            image.Architecture.ToString(),
            hdr.Sections.Count,
            hasRtFuncs,
            sections,
            importSections,
            compilerIdentifier,
            ownerCompositeExecutable,
            componentAssemblies,
            manifestAssemblyMvids,
            methodEntryPoints,
            availableTypes,
            infoMaps,
            manifestMetadata,
            hotColdMap);

        var flagSummary = flagNames.Count > 0 ? $", flags [{string.Join(", ", flagNames)}]" : string.Empty;
        var importSummary = importSections is not null ? $", {importSections.Count} import sections" : string.Empty;
        var compositeSummary = componentAssemblies is not null ? $", {componentAssemblies.Count} component assemblies" : string.Empty;
        var mepSummary = methodEntryPoints is not null ? $", {methodEntryPoints.ReturnedCount} method entry points" : string.Empty;
        var atSummary = availableTypes is not null ? $", {availableTypes.ReturnedCount} available types" : string.Empty;
        var infoMapsSummary = infoMaps is not null ? ", info maps decoded" : string.Empty;
        var manifestSummary = manifestMetadata is not null ? ", manifest metadata located" : string.Empty;
        var hotColdSummary = hotColdMap is not null ? $", {hotColdMap.PairCount} hot/cold pairs" : string.Empty;
        return NativeResult.Ok(
            $"R2R header v{hdr.Version}: {hdr.Sections.Count} sections, architecture {image.Architecture}{flagSummary}{importSummary}{compositeSummary}{mepSummary}{atSummary}{infoMapsSummary}{manifestSummary}{hotColdSummary}.",
            data,
            hints);
    }

    [McpServerTool(Name = "list_r2r_runtime_functions")]
    [Description(
        "Returns paginated RUNTIME_FUNCTION entries from the R2R RuntimeFunctions section (type 102), " +
        "or — when rva is supplied — performs a binary-search lookup and returns the single covering entry. " +
        "x64 and ARM64 images are supported; other architectures return r2r_arch_unsupported. " +
        "Returns r2r_section_not_present when the image has no RuntimeFunctions section.")]
    public NativeResult<R2RRuntimeFunctionsResult> ListR2RRuntimeFunctions(
        [Description("ImageHandle returned by load_native_binary.")] string imageHandle,
        [Description("Optional RVA (hex, e.g. '1a2b3c') to find the single covering RuntimeFunction (binary search). " +
                     "When supplied, pageSize and cursor are ignored.")] string? rva = null,
        [Description("Page size for paginated listing (default 100, max 500). Ignored when rva is supplied.")] int pageSize = 100,
        [Description("Opaque pagination cursor from a prior call. Pass 0 for the first page. Ignored when rva is supplied.")] int cursor = 0)
    {
        if (!registry.TryGet(imageHandle, out var image) || image is null)
            return NativeResult.Fail<R2RRuntimeFunctionsResult>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{imageHandle}'. Call load_native_binary first.");

        var headerResult = ReadyToRunReader.ReadHeader(image);
        if (headerResult.IsError)
            return NativeResult.Fail<R2RRuntimeFunctionsResult>(
                headerResult.Error!.Kind, headerResult.Error.Message, headerResult.Error.Detail);

        var hdr = headerResult.Data!;

        // --- RVA lookup mode ---
        if (rva is not null)
        {
            uint rvaValue;
            try
            {
                var rvaStr = rva.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? rva[2..]
                    : rva;
                rvaValue = Convert.ToUInt32(rvaStr, 16);
            }
            catch
            {
                return NativeResult.Fail<R2RRuntimeFunctionsResult>(
                    ErrorKinds.InvalidArgument,
                    $"Invalid RVA '{rva}'. Supply a hex value such as '1a2b3c' or '0x1a2b3c'.");
            }

            var findResult = ReadyToRunReader.FindRuntimeFunction(image, hdr, rvaValue);
            if (findResult.IsError)
                return NativeResult.Fail<R2RRuntimeFunctionsResult>(
                    findResult.Error!.Kind, findResult.Error.Message, findResult.Error.Detail);

            var fn = findResult.Data!;
            return NativeResult.Ok(
                findResult.Summary,
                new R2RRuntimeFunctionsResult(
                    [ToView(fn)], fn.Index, 1, null, true));
        }

        // --- Paginated listing mode ---
        var pageResult = ReadyToRunReader.ReadRuntimeFunctions(image, hdr, cursor, pageSize);
        if (pageResult.IsError)
            return NativeResult.Fail<R2RRuntimeFunctionsResult>(
                pageResult.Error!.Kind, pageResult.Error.Message, pageResult.Error.Detail);

        var page = pageResult.Data!;
        var views = page.Functions.Select(ToView).ToList();
        return NativeResult.Ok(
            pageResult.Summary,
            new R2RRuntimeFunctionsResult(
                views, page.Cursor, page.TotalCount, page.NextCursor, false));
    }

    private static R2RRuntimeFunctionView ToView(RuntimeFunction fn) =>
        new(fn.Index, $"0x{fn.BeginAddress:X8}", $"0x{fn.EndAddress:X8}", $"0x{fn.UnwindInfoAddress:X8}");
}

/// <summary>Result of <c>get_r2r_header</c>.</summary>
/// <param name="ImageHandle">Handle of the inspected image.</param>
/// <param name="Version">Human-readable version string (e.g. <c>"16.0"</c>).</param>
/// <param name="MajorVersion">R2R major version number.</param>
/// <param name="MinorVersion">R2R minor version number.</param>
/// <param name="Flags">Raw R2R header flags value.</param>
/// <param name="FlagsHex">Raw R2R header flags as a hex string (e.g. <c>"0x00000003"</c>).</param>
/// <param name="FlagNames">Decoded names of the set header flags (<c>READYTORUN_FLAG_*</c>).</param>
/// <param name="Architecture">CPU architecture of the image.</param>
/// <param name="SectionCount">Total number of R2R sections.</param>
/// <param name="HasRuntimeFunctions">Whether a RuntimeFunctions section (type 102) is present.</param>
/// <param name="Sections">All section entries.</param>
/// <param name="ImportSections">
/// Decoded ImportSections (type 101) entries, or <c>null</c> when <c>includeImportSections</c> was
/// false or the image has no ImportSections section.
/// </param>
/// <param name="CompilerIdentifier">
/// The CompilerIdentifier (type 100) string identifying the crossgen2 / compiler that produced the
/// image, or <c>null</c> when the section is absent or malformed.
/// </param>
/// <param name="OwnerCompositeExecutable">
/// The OwnerCompositeExecutable (type 116) filename of the composite executable that owns this
/// component image, or <c>null</c> when the image is not a composite component (section absent).
/// </param>
/// <param name="ComponentAssemblies">
/// Decoded ComponentAssemblies (type 115) entries of a composite image, or <c>null</c> when
/// <c>includeCompositeInfo</c> was false or the image has no ComponentAssemblies section.
/// </param>
/// <param name="ManifestAssemblyMvids">
/// The ManifestAssemblyMvids (type 118) GUIDs (in "D" format), one per manifest assembly, or
/// <c>null</c> when <c>includeCompositeInfo</c> was false or the section is absent.
/// </param>
/// <param name="MethodEntryPoints">
/// Decoded MethodDefEntryPoints (type 103) — the RID -> RUNTIME_FUNCTION mapping — or <c>null</c>
/// when <c>includeMethodEntryPoints</c> was false or the image has no MethodDefEntryPoints section.
/// </param>
/// <param name="AvailableTypes">
/// Decoded AvailableTypes (type 108) metadata tokens, or <c>null</c> when <c>includeAvailableTypes</c>
/// was false or the image has no AvailableTypes section.
/// </param>
/// <param name="InfoMaps">
/// Decoded V9 RID-indexed info maps (EnclosingTypeMap 122, MethodIsGenericMap 121, TypeGenericInfoMap 123),
/// or <c>null</c> when <c>includeInfoMaps</c> was false or the image has none of those sections.
/// </param>
/// <param name="ManifestMetadata">
/// Handoff descriptor for the ManifestMetadata (type 112) embedded ECMA blob, or <c>null</c> when
/// <c>includeManifestMetadata</c> was false or the image has no ManifestMetadata section.
/// </param>
/// <param name="HotColdMap">
/// Decoded HotColdMap (type 120) (cold, hot) RUNTIME_FUNCTION index pairs, or <c>null</c> when
/// <c>includeHotColdMap</c> was false or the image has no HotColdMap section.
/// </param>
public sealed record R2RHeaderResult(
    string ImageHandle,
    string Version,
    ushort MajorVersion,
    ushort MinorVersion,
    uint Flags,
    string FlagsHex,
    IReadOnlyList<string> FlagNames,
    string Architecture,
    int SectionCount,
    bool HasRuntimeFunctions,
    IReadOnlyList<R2RSectionView> Sections,
    IReadOnlyList<R2RImportSectionView>? ImportSections = null,
    string? CompilerIdentifier = null,
    string? OwnerCompositeExecutable = null,
    IReadOnlyList<R2RComponentAssemblyView>? ComponentAssemblies = null,
    IReadOnlyList<string>? ManifestAssemblyMvids = null,
    R2RMethodEntryPointsView? MethodEntryPoints = null,
    R2RAvailableTypesView? AvailableTypes = null,
    R2RInfoMapsView? InfoMaps = null,
    R2RManifestMetadataView? ManifestMetadata = null,
    R2RHotColdMapView? HotColdMap = null);

/// <summary>Decoded MethodDefEntryPoints (type 103) table.</summary>
/// <param name="MethodCount">
/// Number of MethodDef RID slots the table is sized for (not all are present).
/// </param>
/// <param name="ReturnedCount">Number of present entries actually returned (after the limit).</param>
/// <param name="Truncated"><c>true</c> when more present entries existed than were returned.</param>
/// <param name="Entries">The present entries, capped at the requested limit.</param>
public sealed record R2RMethodEntryPointsView(
    uint MethodCount,
    int ReturnedCount,
    bool Truncated,
    IReadOnlyList<R2RMethodEntryPointView> Entries);

/// <summary>One present entry of the MethodDefEntryPoints (type 103) table.</summary>
/// <param name="Rid">The 1-based MethodDef metadata RID this entry maps.</param>
/// <param name="RuntimeFunctionIndex">Index of the method's entry-point <c>RUNTIME_FUNCTION</c>.</param>
/// <param name="HasFixups"><c>true</c> when the entry carries import fixups to run before first call.</param>
public sealed record R2RMethodEntryPointView(
    int Rid,
    int RuntimeFunctionIndex,
    bool HasFixups);

/// <summary>Decoded AvailableTypes (type 108) table.</summary>
/// <param name="ReturnedCount">Number of type tokens actually returned (after the limit).</param>
/// <param name="Truncated"><c>true</c> when more entries existed than were returned.</param>
/// <param name="Types">The decoded type tokens, capped at the requested limit.</param>
public sealed record R2RAvailableTypesView(
    int ReturnedCount,
    bool Truncated,
    IReadOnlyList<R2RAvailableTypeView> Types);

/// <summary>One entry of the AvailableTypes (type 108) table.</summary>
/// <param name="MetadataToken">
/// The type's metadata token (hex): a TypeDef token (table 0x02) for a type defined in this module,
/// or an ExportedType token (table 0x27) for a forwarded type. Hand off to dotnet-assembly-mcp's
/// <c>get_type</c> to resolve the name and members.
/// </param>
/// <param name="IsExportedType"><c>true</c> when the token is an ExportedType (forwarder) token.</param>
public sealed record R2RAvailableTypeView(
    string MetadataToken,
    bool IsExportedType);

/// <summary>Decoded V9 RID-indexed info maps. Each field is <c>null</c> when its section is absent.</summary>
/// <param name="EnclosingTypeMap">Nested-type -> enclosing-type relationships (type 122).</param>
/// <param name="MethodIsGenericMap">Generic-method markers (type 121).</param>
/// <param name="TypeGenericInfoMap">Per-type generic arity / variance / constraints (type 123).</param>
public sealed record R2RInfoMapsView(
    R2REnclosingTypeMapView? EnclosingTypeMap,
    R2RMethodIsGenericMapView? MethodIsGenericMap,
    R2RTypeGenericInfoMapView? TypeGenericInfoMap);

/// <summary>Decoded EnclosingTypeMap (type 122).</summary>
/// <param name="TypeDefCount">Total number of TypeDef rows the map covers.</param>
/// <param name="ReturnedCount">Number of nested-type entries returned (after the limit).</param>
/// <param name="Truncated"><c>true</c> when more nested types existed than were returned.</param>
/// <param name="NestedTypes">Nested-type -> enclosing-type token pairs, capped at the requested limit.</param>
public sealed record R2REnclosingTypeMapView(
    int TypeDefCount,
    int ReturnedCount,
    bool Truncated,
    IReadOnlyList<R2RNestedTypeView> NestedTypes);

/// <summary>One nested-type relationship from the EnclosingTypeMap (type 122).</summary>
/// <param name="NestedTypeToken">TypeDef token (hex, table 0x02) of the nested type.</param>
/// <param name="EnclosingTypeToken">TypeDef token (hex, table 0x02) of the declaring type. Hand off to dotnet-assembly-mcp.</param>
public sealed record R2RNestedTypeView(
    string NestedTypeToken,
    string EnclosingTypeToken);

/// <summary>Decoded MethodIsGenericMap (type 121).</summary>
/// <param name="MethodDefCount">Total number of MethodDef rows the bit array covers.</param>
/// <param name="GenericMethodCount">Total number of generic methods (even past the limit).</param>
/// <param name="Truncated"><c>true</c> when more generic methods existed than were returned.</param>
/// <param name="GenericMethodTokens">MethodDef tokens (hex, table 0x06) of generic methods, capped at the requested limit.</param>
public sealed record R2RMethodIsGenericMapView(
    int MethodDefCount,
    int GenericMethodCount,
    bool Truncated,
    IReadOnlyList<string> GenericMethodTokens);

/// <summary>Decoded TypeGenericInfoMap (type 123).</summary>
/// <param name="TypeDefCount">Total number of TypeDef rows the nibble array covers.</param>
/// <param name="GenericTypeCount">Total number of generic types (even past the limit).</param>
/// <param name="Truncated"><c>true</c> when more generic types existed than were returned.</param>
/// <param name="GenericTypes">Per-type generic info, capped at the requested limit.</param>
public sealed record R2RTypeGenericInfoMapView(
    int TypeDefCount,
    int GenericTypeCount,
    bool Truncated,
    IReadOnlyList<R2RTypeGenericInfoView> GenericTypes);

/// <summary>One generic type's info from the TypeGenericInfoMap (type 123).</summary>
/// <param name="TypeToken">TypeDef token (hex, table 0x02) of the generic type. Hand off to dotnet-assembly-mcp.</param>
/// <param name="GenericArgCount">Generic-parameter count: 1, 2, or 3 meaning "more than two".</param>
/// <param name="HasVariance"><c>true</c> when any generic parameter is variant (in/out).</param>
/// <param name="HasConstraints"><c>true</c> when any generic parameter has a constraint.</param>
public sealed record R2RTypeGenericInfoView(
    string TypeToken,
    int GenericArgCount,
    bool HasVariance,
    bool HasConstraints);

/// <summary>Handoff descriptor for the ManifestMetadata (type 112) embedded ECMA-335 metadata blob.</summary>
/// <param name="FileOffset">Byte offset of the blob within the PE file (hex). Hand off to dotnet-assembly-mcp.</param>
/// <param name="Rva">Relative virtual address of the blob within the image (hex).</param>
/// <param name="Size">Byte size of the blob.</param>
/// <param name="MajorVersion">Metadata-root major version (ECMA-335 II.24.2.1).</param>
/// <param name="MinorVersion">Metadata-root minor version.</param>
/// <param name="Version">Runtime version string (e.g. <c>v4.0.30319</c>).</param>
/// <param name="Streams">The metadata stream directory (<c>#~</c>, <c>#Strings</c>, <c>#US</c>, <c>#GUID</c>, <c>#Blob</c>).</param>
public sealed record R2RManifestMetadataView(
    string FileOffset,
    string Rva,
    uint Size,
    ushort MajorVersion,
    ushort MinorVersion,
    string Version,
    IReadOnlyList<R2RMetadataStreamView> Streams);

/// <summary>One stream header from the embedded ECMA metadata root.</summary>
/// <param name="Name">Stream name (e.g. <c>#~</c>, <c>#Strings</c>).</param>
/// <param name="Offset">Stream offset relative to the metadata-root start (hex).</param>
/// <param name="Size">Stream byte size.</param>
public sealed record R2RMetadataStreamView(
    string Name,
    string Offset,
    uint Size);

/// <summary>Decoded HotColdMap (type 120) — (cold, hot) RUNTIME_FUNCTION index pairs.</summary>
/// <param name="PairCount">Total number of hot/cold pairs in the section.</param>
/// <param name="Truncated"><c>true</c> when more pairs existed than were returned.</param>
/// <param name="Pairs">The decoded pairs, capped at the requested limit.</param>
public sealed record R2RHotColdMapView(
    int PairCount,
    bool Truncated,
    IReadOnlyList<R2RHotColdPairView> Pairs);

/// <summary>One hot/cold mapping from the HotColdMap (type 120).</summary>
/// <param name="ColdRuntimeFunctionIndex">RUNTIME_FUNCTION index of the cold (split-out) partition.</param>
/// <param name="HotRuntimeFunctionIndex">RUNTIME_FUNCTION index of the hot (primary) partition.</param>
public sealed record R2RHotColdPairView(
    uint ColdRuntimeFunctionIndex,
    uint HotRuntimeFunctionIndex);

/// <summary>One decoded entry of the R2R ComponentAssemblies section (type 115).</summary>
/// <param name="Index">Zero-based index in the component-assemblies table.</param>
/// <param name="CorHeaderRva">RVA of the component's COR (CLI) header (hex).</param>
/// <param name="CorHeaderSize">Byte size of the component's COR header.</param>
/// <param name="AssemblyHeaderRva">RVA of the component's R2R assembly header (hex).</param>
/// <param name="AssemblyHeaderSize">Byte size of the component's R2R assembly header.</param>
public sealed record R2RComponentAssemblyView(
    int Index,
    string CorHeaderRva,
    uint CorHeaderSize,
    string AssemblyHeaderRva,
    uint AssemblyHeaderSize);

/// <summary>One decoded entry of the R2R ImportSections section (type 101).</summary>
/// <param name="Index">Zero-based index in the import-sections table.</param>
/// <param name="SectionRva">RVA of the fixup region this entry describes (hex).</param>
/// <param name="SectionSize">Byte size of the fixup region.</param>
/// <param name="Flags">Raw import-section flags value.</param>
/// <param name="FlagNames">Decoded names of the set import-section flags (e.g. Eager, PCode).</param>
/// <param name="Type">Raw import-section type value.</param>
/// <param name="TypeName">Human-readable type name (e.g. StubDispatch, StringHandle).</param>
/// <param name="EntrySize">Size, in bytes, of one fixup cell within the region.</param>
/// <param name="SignaturesRva">RVA of the optional signature descriptors (hex; <c>0x00000000</c> when absent).</param>
/// <param name="AuxiliaryDataRva">RVA of the optional auxiliary data (hex; <c>0x00000000</c> when absent).</param>
public sealed record R2RImportSectionView(
    int Index,
    string SectionRva,
    uint SectionSize,
    ushort Flags,
    IReadOnlyList<string> FlagNames,
    byte Type,
    string TypeName,
    byte EntrySize,
    string SignaturesRva,
    string AuxiliaryDataRva);

/// <summary>One section entry in the R2R header.</summary>
/// <param name="Type">Numeric section type.</param>
/// <param name="TypeName">Human-readable name (or raw number for unknown types).</param>
/// <param name="Rva">Section data RVA as a hex string.</param>
/// <param name="Size">Byte size of the section data.</param>
public sealed record R2RSectionView(
    uint Type,
    string TypeName,
    string Rva,
    uint Size);

/// <summary>Result of <c>list_r2r_runtime_functions</c>.</summary>
/// <param name="Functions">Entries on this page (or the single lookup result).</param>
/// <param name="Cursor">Cursor used to fetch this page.</param>
/// <param name="TotalCount">Total entries in the RuntimeFunctions table.</param>
/// <param name="NextCursor">Cursor for the next page, or <c>null</c> if this is the last page.</param>
/// <param name="IsLookup"><c>true</c> when the result is from an RVA lookup rather than a full page.</param>
public sealed record R2RRuntimeFunctionsResult(
    IReadOnlyList<R2RRuntimeFunctionView> Functions,
    int Cursor,
    int TotalCount,
    int? NextCursor,
    bool IsLookup);

/// <summary>One RUNTIME_FUNCTION row.</summary>
/// <param name="Index">Zero-based index in the table.</param>
/// <param name="BeginAddress">RVA of the first instruction (hex).</param>
/// <param name="EndAddress">RVA one past the last instruction (hex).</param>
/// <param name="UnwindInfoAddress">RVA of the unwind info record (hex).</param>
public sealed record R2RRuntimeFunctionView(
    int Index,
    string BeginAddress,
    string EndAddress,
    string UnwindInfoAddress);
