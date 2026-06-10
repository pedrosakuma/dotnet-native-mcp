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
        "When includeImportSections is true, also decodes the ImportSections section (type 101) into " +
        "per-entry fixup-region metadata (RVA/size, decoded Type and Flags, EntrySize, Signatures and " +
        "AuxiliaryData RVAs); individual fixup signatures are not decoded. " +
        "Returns r2r_not_present when the image has no R2R header (pure managed assembly or NativeAOT binary). " +
        "Use list_r2r_runtime_functions to navigate the RuntimeFunctions section.")]
    public NativeResult<R2RHeaderResult> GetR2RHeader(
        [Description("ImageHandle returned by load_native_binary.")] string imageHandle,
        [Description("When true, also decode and return the ImportSections (type 101) entries. Default false.")]
        bool includeImportSections = false)
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
            importSections);

        var flagSummary = flagNames.Count > 0 ? $", flags [{string.Join(", ", flagNames)}]" : string.Empty;
        var importSummary = importSections is not null ? $", {importSections.Count} import sections" : string.Empty;
        return NativeResult.Ok(
            $"R2R header v{hdr.Version}: {hdr.Sections.Count} sections, architecture {image.Architecture}{flagSummary}{importSummary}.",
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
    IReadOnlyList<R2RImportSectionView>? ImportSections = null);

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
