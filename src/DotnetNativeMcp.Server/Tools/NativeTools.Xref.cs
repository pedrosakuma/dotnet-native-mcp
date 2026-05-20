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
        "and returns every CALL/JMP/BL instruction whose target resolves to the requested symbol or address. " +
        "The full xref index is built lazily on the first call and cached per image handle; " +
        "subsequent calls for the same image are O(callers). " +
        "The index is persisted to disk under ~/.cache/dotnet-native-mcp/<build-id>.xref so large " +
        "NativeAOT binaries pay the scan cost only once across sessions. " +
        "Set DOTNET_NATIVE_MCP_XREF_CACHE=0 to disable the disk cache. " +
        "Use 'disassemble' to inspect any returned call site. " +
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
}

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
