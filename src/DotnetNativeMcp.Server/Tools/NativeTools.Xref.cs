using System.ComponentModel;
using System.Globalization;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using DotnetNativeMcp.Core.Xref;
using ModelContextProtocol.Server;

namespace DotnetNativeMcp.Server.Tools;

public sealed partial class NativeTools
{
    [McpServerTool(Name = "find_native_callers")]
    [Description(
        "Scans all executable sections of a loaded native image using static disassembly (Iced for x86/x64, AsmArm64 for ARM64) " +
        "and returns every CALL/JMP/BL instruction whose target resolves to the requested symbol or address. Results are capped at 100000 call sites per response. " +
        "The full xref index is built lazily on the first call and cached per image handle; " +
        "subsequent calls for the same image are O(callers). " +
        "The index is persisted to disk under ~/.cache/dotnet-native-mcp/<build-id>.xref so large " +
        "NativeAOT binaries pay the scan cost only once across sessions. " +
        "Set DOTNET_NATIVE_MCP_XREF_CACHE=0 to disable the disk cache. " +
        "When crossImage is true, also scans every other loaded image for call sites that resolve " +
        "via PLT (ELF), import thunks (PE), or Mach-O stubs to the callee's exported name. " +
        "Set DOTNET_NATIVE_MCP_CROSS_XREF=0 to disable cross-image scanning globally. " +
        "Use 'disassemble' to inspect any returned call site. " +
        "When resolveSource is true (default) each call site is annotated with file:line from DWARF/PDB debug info. " +
        "Set resolveSource=false to skip debug-info I/O for large binaries where PDB reads are slow.")]
    public NativeResult<FindCallersResult> FindNativeCallers(
        [Description("ImageHandle returned by load_native_binary.")] string imageHandle,
        [Description(
            "Target to find callers of. " +
            "Accepts a raw mangled or demangled symbol name, a hex address (0x prefix optional), or a decimal address.")] string target,
        [Description("When true (default), annotates each call site with file:line from DWARF/PDB debug info. Set false to skip debug-info I/O.")] bool resolveSource = true,
        [Description(
            "When true, also scans every other loaded image for call sites that target the callee's exported symbol " +
            "via PLT (ELF), import thunks (PE), or Mach-O stubs. Cross-image rows have isCrossImage=true and carry callerImageBuildId/callerImagePath. " +
            "Ignored when DOTNET_NATIVE_MCP_CROSS_XREF=0. Default: false.")] bool crossImage = false)
    {
        if (!registry.TryGet(imageHandle, out var image) || image is null)
            return NativeResult.Fail<FindCallersResult>(
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{imageHandle}'. Call load_native_binary first.");

        if (string.IsNullOrWhiteSpace(target))
            return NativeResult.Fail<FindCallersResult>(
                ErrorKinds.InvalidArgument,
                "target must not be empty.");

        if (image.Architecture is not (Architecture.X64 or Architecture.X86 or Architecture.Arm64))
            return NativeResult.Fail<FindCallersResult>(
                ErrorKinds.DisassemblyUnsupported,
                $"Disassembly for {image.Architecture} is not supported. Only x86/x64 and ARM64 are implemented.");

        // Resolve target to an absolute virtual address.
        ulong targetVa;
        NativeSymbol? targetSym;

        if (StackSymbolicator.TryParseAddress(target, out var parsedValue, out _))
        {
            // Normalise: if parsedValue < imageBase treat as RVA, otherwise as VA.
            var rva = SymbolResolution.VaToRva(parsedValue, image.ImageBase);
            targetVa = image.ImageBase + rva;

            // Try to attribute the address to a symbol (best-effort; null is fine).
            targetSym = SymbolResolution.FindByRva(image.Symbols, rva) ??
                TryResolveMachOExportByRva(image, rva);

            // Validate that the resolved RVA is inside a known section.
            if (image.FindSection(rva) is null)
                return NativeResult.Fail<FindCallersResult>(
                    ErrorKinds.AddressOutOfRange,
                    $"Address 0x{parsedValue:x} is outside the known sections of '{imageHandle}'.");
        }
        else
        {
            // Try by symbol name (mangled or demangled).
            targetSym = SymbolResolution.FindByName(image.Symbols, target) ??
                TryResolveMachOExportByName(image, target);
            if (targetSym is null)
                return NativeResult.Fail<FindCallersResult>(
                    ErrorKinds.SymbolNotFound,
                    $"Symbol '{target}' not found in '{imageHandle}'. Use list_native_symbols to browse.");

            targetVa = image.ImageBase + targetSym.Rva;
        }

        var callers = callGraphCache.FindCallers(image, targetVa);
        var sameImageCount = callers.Count;
        var crossBudget = Math.Max(0, ResourceLimits.MaxCallerSites - sameImageCount + 1); // +1 to detect overflow
        var crossCallers = crossImage && targetSym is not null && NativeCallGraphCache.IsCrossXrefEnabled
            ? callGraphCache.FindCrossImageCallers(image, targetSym.Name, null, registry, crossBudget)
            : [];

        var targetAddrHex = targetVa.ToString("x16", CultureInfo.InvariantCulture);
        var displayName = targetSym?.Name ?? target;
        var totalCallers = callers.Count + crossCallers.Count;
        var truncated = totalCallers > ResourceLimits.MaxCallerSites;
        var rows = new List<CallSiteRow>(Math.Min(totalCallers, ResourceLimits.MaxCallerSites));

        foreach (var site in callers)
        {
            if (rows.Count >= ResourceLimits.MaxCallerSites)
                break;

            SourceLocation? src = null;
            if (resolveSource && ulong.TryParse(site.SourceAddressHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var siteVa))
                src = sourceResolver.TrySourceFor(image, siteVa);

            rows.Add(new CallSiteRow(
                site.SourceAddressHex,
                site.CallerSymbol,
                site.CallerDemangled,
                site.Mnemonic,
                site.Operands,
                site.RawBytes,
                src,
                image.Handle.BuildIdHex,
                image.FilePath,
                false));
        }

        foreach (var xsite in crossCallers)
        {
            if (rows.Count >= ResourceLimits.MaxCallerSites)
                break;

            SourceLocation? crossSrc = null;
            if (resolveSource)
            {
                var callerImg = registry.List()
                    .FirstOrDefault(img => string.Equals(img.FilePath, xsite.CallerImagePath, StringComparison.OrdinalIgnoreCase));
                if (callerImg is not null &&
                    ulong.TryParse(xsite.SourceAddressHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var xsiteVa))
                {
                    crossSrc = sourceResolver.TrySourceFor(callerImg, xsiteVa);
                }
            }

            rows.Add(new CallSiteRow(
                xsite.SourceAddressHex,
                xsite.CallerSymbol,
                xsite.CallerDemangled,
                xsite.Mnemonic,
                xsite.Operands,
                xsite.RawBytes,
                crossSrc,
                xsite.CallerImageBuildId,
                xsite.CallerImagePath,
                true));
        }

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

        var summary = truncated
            ? $"Found {totalCallers} caller(s) of '{displayName}' in '{imageHandle}' (truncated to {rows.Count})."
            : $"Found {rows.Count} caller(s) of '{displayName}' in '{imageHandle}'.";

        return NativeResult.Ok(
            summary,
            new FindCallersResult(targetAddrHex, targetSym?.Name, targetSym?.DemangledName, totalCallers, rows, truncated),
            hints);
    }

    private NativeSymbol? TryResolveMachOExportByName(NativeImage image, string target)
    {
        if (image.Format != BinaryFormat.MachO)
            return null;

        var exports = callGraphCache.GetOrBuildMachOExports(image);
        return exports.TryGetValue(target, out var exportRva)
            ? new NativeSymbol(-1, target, target, exportRva, 0, null, true)
            : null;
    }

    private NativeSymbol? TryResolveMachOExportByRva(NativeImage image, ulong rva)
    {
        if (image.Format != BinaryFormat.MachO)
            return null;

        foreach (var (name, exportRva) in callGraphCache.GetOrBuildMachOExports(image))
        {
            if (exportRva == rva)
                return new NativeSymbol(-1, name, name, exportRva, 0, null, true);
        }

        return null;
    }
}

/// <summary>One call-site row returned by <c>find_native_callers</c>.</summary>
/// <param name="SourceAddressHex">Absolute virtual address of the calling instruction, lowercase hex.</param>
/// <param name="CallerSymbol">Raw mangled name of the enclosing function, or <c>null</c>.</param>
/// <param name="CallerDemangled">Best-effort demangled name of the enclosing function, or <c>null</c>.</param>
/// <param name="Mnemonic">Lowercase transfer-of-control mnemonic (e.g. <c>call</c>, <c>jmp</c>).</param>
/// <param name="Operands">Formatted operand text.</param>
/// <param name="RawBytes">Hex-encoded raw bytes of the instruction.</param>
/// <param name="Source">Source file+line from DWARF/PDB debug info, when available.</param>
/// <param name="CallerImageBuildId">Build-id of the image that contains this call site.</param>
/// <param name="CallerImagePath">Absolute path of the image that contains this call site.</param>
/// <param name="IsCrossImage"><c>true</c> when the caller resides in a different image than the callee.</param>
public sealed record CallSiteRow(
    string SourceAddressHex,
    string? CallerSymbol,
    string? CallerDemangled,
    string Mnemonic,
    string Operands,
    string RawBytes,
    SourceLocation? Source = null,
    string? CallerImageBuildId = null,
    string? CallerImagePath = null,
    bool IsCrossImage = false);

/// <summary>Result payload for <c>find_native_callers</c>.</summary>
/// <param name="TargetAddressHex">Resolved absolute virtual address of the target, lowercase hex.</param>
/// <param name="TargetSymbol">Raw mangled name of the target symbol, or <c>null</c> when only an address was supplied.</param>
/// <param name="TargetDemangled">Best-effort demangled name of the target symbol, or <c>null</c>.</param>
/// <param name="TotalCallers">Total number of call-sites found.</param>
/// <param name="Callers">The capped list of call-sites returned for this address.</param>
/// <param name="Truncated"><c>true</c> when additional call-sites were omitted after reaching the response cap.</param>
public sealed record FindCallersResult(
    string TargetAddressHex,
    string? TargetSymbol,
    string? TargetDemangled,
    int TotalCallers,
    IReadOnlyList<CallSiteRow> Callers,
    bool Truncated);
