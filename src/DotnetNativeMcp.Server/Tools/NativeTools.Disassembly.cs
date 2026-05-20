using System.ComponentModel;
using System.Globalization;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Disassembly;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using ModelContextProtocol.Server;

namespace DotnetNativeMcp.Server.Tools;

public sealed partial class NativeTools
{
    [McpServerTool(Name = "disassemble")]
    [Description(
        "Disassembles native machine code using Iced (x86/x64) or AsmArm64 (ARM64/AArch64). " +
        "Two modes: (1) registered-handle mode — supply imageHandle (returned by load_native_binary) " +
        "plus address or symbolName; (2) raw-bytes mode — supply imagePath + rva + size directly, " +
        "bypassing load_native_binary (works on any PE/ELF/Mach-O including managed PEs with R2R bodies). " +
        "Exactly one of {imageHandle, imagePath} must be present. " +
        "Each instruction includes absolute address, raw bytes, mnemonic, operands, " +
        "and a cross-ref hint for CALL/JMP/BL/B targets that can be resolved against the symbol table. " +
        "When resolveSource is true each instruction is optionally annotated with file:line from DWARF debug info. " +
        "Default: 64 instructions. Max: 2048. " +
        "ARM64 decodes BL, B, B.cond, CBZ, CBNZ, TBZ, TBNZ and surfaces their targets as cross-ref hints.")]
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
}
