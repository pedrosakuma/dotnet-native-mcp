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
        "Three modes: (1) registered-handle mode — supply imageHandle (returned by load_native_binary) " +
        "plus address or symbolName; (2) raw-bytes mode — supply imagePath + rva + size directly, " +
        "bypassing load_native_binary (works on any PE/ELF/Mach-O including managed PEs with R2R bodies); " +
        "(3) raw-blob mode — supply imagePath + size + architecture + baseAddress + rawBlob=true to decode " +
        "a plain byte buffer with no PE/ELF/Mach-O header (e.g. JIT-emitted code from dotnet-diagnostics-mcp.capture_method_bytes). " +
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
        [Description("Absolute path to a PE, ELF, or Mach-O binary (raw-bytes mode), or to a raw instruction blob (rawBlob=true). Mutually exclusive with imageHandle.")] string? imagePath = null,
        [Description("Start RVA within imagePath (required when imagePath is supplied without rawBlob); byte offset into the blob when rawBlob=true (defaults to 0).")] int? rva = null,
        [Description("Number of code bytes to decode (required when imagePath is supplied; required when rawBlob=true).")] int? size = null,
        [Description("CPU architecture override: 'x64', 'x86', or 'arm64'. Detected from the binary header in raw-bytes mode; required when rawBlob=true.")] string? architecture = null,
        [Description("Image base for absolute-address formatting. Detected from the binary header in raw-bytes mode; required when rawBlob=true.")] ulong? baseAddress = null,
        [Description("Optional path to a UTF-8 .ilmap sidecar for rawBlob=true. Each line is '<nativeOffsetHex>\\t<ilOffsetHex|prolog|epilog|noinfo>'. Invalid for non-rawBlob modes.")] string? ilMapPath = null,
        [Description("When true, treats imagePath as a raw instruction blob with no PE/ELF/Mach-O header. Requires size, architecture, and baseAddress. Mutually exclusive with imageHandle.")] bool rawBlob = false)
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

        if (!rawBlob && !string.IsNullOrWhiteSpace(ilMapPath))
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.InvalidArgument,
                "'ilMapPath' is only supported when rawBlob=true.");

        // Path hints off the wire are untrusted: canonicalise + allowlist-check before any file open.
        string? canonicalImagePath = null;
        if (hasPath)
        {
            var imageValidation = _pathPolicy.Validate(imagePath!);
            if (imageValidation.IsError)
                return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                    imageValidation.Error!.Kind, imageValidation.Error.Message, imageValidation.Error.Detail);
            canonicalImagePath = imageValidation.Data!;
        }

        // ── Raw-blob mode ─────────────────────────────────────────────────────────
        if (rawBlob)
        {
            if (hasHandle)
                return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                    ErrorKinds.InvalidArgument,
                    "'imageHandle' and 'rawBlob=true' are mutually exclusive. Supply 'imagePath' with rawBlob.");

            if (size is null || size.Value <= 0)
                return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                    ErrorKinds.RawBlobMissingSize,
                    "'size' is required when rawBlob=true and must be > 0.");

            if (string.IsNullOrEmpty(architecture))
                return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                    ErrorKinds.RawBlobMissingArchitecture,
                    "'architecture' is required when rawBlob=true. Supply 'x64', 'x86', or 'arm64'.");

            if (baseAddress is null)
                return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                    ErrorKinds.RawBlobMissingBaseAddress,
                    "'baseAddress' is required when rawBlob=true so that call/jmp target addresses render correctly.");

            var parsedBlobArch = architecture.Trim().ToLowerInvariant() switch
            {
                "x64" or "amd64" => (Core.Imaging.Architecture?)Core.Imaging.Architecture.X64,
                "x86" or "i386" => Core.Imaging.Architecture.X86,
                "arm64" or "aarch64" => Core.Imaging.Architecture.Arm64,
                _ => null,
            };
            if (parsedBlobArch is null)
                return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                    ErrorKinds.DisassemblyUnsupported,
                    $"Unknown architecture '{architecture}' for rawBlob mode. Valid values: x64, x86, arm64.");

            JitIlMap? ilMap = null;
            if (!string.IsNullOrWhiteSpace(ilMapPath))
            {
                var ilMapValidation = _pathPolicy.Validate(ilMapPath!);
                if (ilMapValidation.IsError)
                    return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                        ilMapValidation.Error!.Kind, ilMapValidation.Error.Message, ilMapValidation.Error.Detail);

                var ilMapResult = JitIlMap.Load(ilMapValidation.Data!);
                if (ilMapResult.IsError)
                    return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                        ilMapResult.Error!.Kind,
                        ilMapResult.Error.Message,
                        ilMapResult.Error.Detail);

                ilMap = ilMapResult.Data;
            }

            // resolveSource is silently ignored for raw blobs (no PDB/DWARF available).
            var blobOffset = rva ?? 0;
            return RawDisassembler.DisassembleBlob(
                canonicalImagePath!,
                blobOffset,
                size.Value,
                parsedBlobArch.Value,
                baseAddress.Value,
                maxInstructions,
                ilMap);
        }

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
                canonicalImagePath!,
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
