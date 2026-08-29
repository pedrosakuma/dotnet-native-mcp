using System.CommandLine;
using System.Globalization;
using System.Text;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Disassembly;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;

namespace DotnetNativeMcp.Cli;

public static class DisasmCommandFactory
{
    public static Command Create(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var pathArgument = new Argument<string?>("path")
        {
            Description = "Path mode: managed-native PE/ELF/Mach-O to load and disassemble.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var addressOption = new Option<string?>("--address")
        {
            Description = "Hex VA/RVA to start disassembly in path mode.",
        };

        var lengthOption = new Option<int?>("--length")
        {
            Description = "Number of code bytes to decode in path mode.",
        };

        var bytesOption = new Option<string?>("--bytes")
        {
            Description = "Raw code bytes as lowercase hex, spaced hex, base64, or '-' to read stdin.",
        };

        var blobOption = new Option<string?>("--blob")
        {
            Description = "Raw instruction blob path for blob mode.",
        };

        var architectureOption = new Option<string?>("--architecture")
        {
            Description = "Architecture: x64, x86, or arm64. Required for --bytes and --blob.",
        };

        var baseAddressOption = new Option<string?>("--base-address")
        {
            Description = "Hex virtual address used to format instruction addresses.",
        };

        var sizeOption = new Option<int?>("--size")
        {
            Description = "Blob mode: number of bytes to decode from --blob.",
        };

        var rvaOption = new Option<int?>("--rva")
        {
            Description = "Blob mode: byte offset within --blob to begin decoding. Defaults to 0.",
        };

        var ilMapPathOption = new Option<string?>("--il-map-path")
        {
            Description = "Blob mode: optional UTF-8 .ilmap sidecar path.",
        };

        var maxInstructionsOption = new Option<int>("--max-instructions")
        {
            Description = "Maximum instructions to decode. Default 64, capped at 2048.",
            DefaultValueFactory = _ => IcedDisassembler.DefaultMaxInstructions,
        };

        var resolveSourceOption = new Option<bool>("--resolve-source")
        {
            Description = "Path mode: annotate decoded instructions with source file and line when available.",
        };

        var command = new Command("disasm", "Disassemble a managed-native image, inline bytes, or a raw instruction blob.");
        command.Arguments.Add(pathArgument);
        command.Options.Add(addressOption);
        command.Options.Add(lengthOption);
        command.Options.Add(bytesOption);
        command.Options.Add(blobOption);
        command.Options.Add(architectureOption);
        command.Options.Add(baseAddressOption);
        command.Options.Add(sizeOption);
        command.Options.Add(rvaOption);
        command.Options.Add(ilMapPathOption);
        command.Options.Add(maxInstructionsOption);
        command.Options.Add(resolveSourceOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var invocation = CliApplication.BuildInvocationContext(parseResult, options);

            var path = parseResult.GetValue(pathArgument);
            var address = parseResult.GetValue(addressOption);
            var length = parseResult.GetValue(lengthOption);
            var inlineBytes = parseResult.GetValue(bytesOption);
            var blobPath = parseResult.GetValue(blobOption);
            var architecture = parseResult.GetValue(architectureOption);
            var baseAddress = parseResult.GetValue(baseAddressOption);
            var size = parseResult.GetValue(sizeOption);
            var rva = parseResult.GetValue(rvaOption);
            var ilMapPath = parseResult.GetValue(ilMapPathOption);
            var maxInstructions = parseResult.GetValue(maxInstructionsOption);
            var resolveSource = parseResult.GetValue(resolveSourceOption);

            var result = await ExecuteAsync(
                    invocation,
                    path,
                    address,
                    length,
                    inlineBytes,
                    blobPath,
                    architecture,
                    baseAddress,
                    size,
                    rva,
                    ilMapPath,
                    maxInstructions,
                    resolveSource,
                    cancellationToken)
                .ConfigureAwait(false);

            await invocation.OutputWriter.WriteAsync(result, cancellationToken).ConfigureAwait(false);
            return result.IsError ? 1 : 0;
        });

        return command;
    }

    private static async Task<NativeResult<IReadOnlyList<InstructionView>>> ExecuteAsync(
        CliInvocationContext invocation,
        string? path,
        string? address,
        int? length,
        string? inlineBytes,
        string? blobPath,
        string? architecture,
        string? baseAddress,
        int? size,
        int? rva,
        string? ilMapPath,
        int maxInstructions,
        bool resolveSource,
        CancellationToken cancellationToken)
    {
        var modeCount = CountSupplied(path, inlineBytes, blobPath);
        if (modeCount != 1)
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.InvalidArgument,
                "Choose exactly one input mode: positional 'path', '--bytes', or '--blob'.");
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            return DisassemblePathMode(invocation, path!, address, length, maxInstructions, resolveSource);
        }

        if (!string.IsNullOrWhiteSpace(inlineBytes))
        {
            return await DisassembleInlineBytesModeAsync(
                    inlineBytes!,
                    architecture,
                    baseAddress,
                    maxInstructions,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return DisassembleBlobMode(invocation, blobPath!, architecture, baseAddress, size, rva, ilMapPath, maxInstructions);
    }

    private static NativeResult<IReadOnlyList<InstructionView>> DisassemblePathMode(
        CliInvocationContext invocation,
        string path,
        string? address,
        int? length,
        int maxInstructions,
        bool resolveSource)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.InvalidArgument,
                "'--address' is required in path mode.");
        }

        if (length is null || length.Value <= 0)
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.InvalidArgument,
                "'--length' is required in path mode and must be > 0.");
        }

        var imageValidation = invocation.PathPolicy.Validate(path);
        if (imageValidation.IsError)
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                imageValidation.Error!.Kind,
                imageValidation.Error.Message,
                imageValidation.Error.Detail);
        }

        var loadResult = NativeImageLoader.Load(imageValidation.Data!);
        if (loadResult.IsError)
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                loadResult.Error!.Kind,
                loadResult.Error.Message,
                loadResult.Error.Detail);
        }

        if (!TryParseHex(address!, out var virtualAddress))
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.InvalidArgument,
                $"Cannot parse address '{address}' as a hex value.");
        }

        var image = loadResult.Data!;
        var rva = SymbolResolution.VaToRva(virtualAddress, image.ImageBase);
        var fileOffset = image.RvaToFileOffset(rva);
        if (fileOffset is null)
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.AddressOutOfRange,
                $"RVA 0x{rva:x} is not inside any known section in '{Path.GetFileName(image.FilePath)}'.");
        }

        if ((long)fileOffset.Value + length.Value > image.RawBytes.Length)
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.AddressOutOfRange,
                $"RVA 0x{rva:x} + length {length.Value} exceeds the file length of {image.RawBytes.Length} bytes.");
        }

        var section = image.FindSection(rva);
        if (section is null)
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.AddressOutOfRange,
                $"RVA 0x{rva:x} is not inside any known section.");
        }

        var sectionBytesRemaining = (section.VirtualAddress + section.VirtualSize) - rva;
        if ((ulong)length.Value > sectionBytesRemaining)
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.AddressOutOfRange,
                $"RVA 0x{rva:x} + length {length.Value} exceeds section '{section.Name}'.");
        }

        var rangeImage = new NativeImage(
            image.Handle,
            image.FilePath,
            image.Format,
            image.Architecture,
            [new NativeSection(section.Name, rva, (ulong)length.Value, 0, (ulong)length.Value)],
            image.Symbols,
            image.RawBytes.Slice(fileOffset.Value, length.Value),
            image.ImageBase);

        var disassembly = IcedDisassembler.Disassemble(rangeImage, rva, maxInstructions);
        return !resolveSource || disassembly.IsError
            ? disassembly
            : AnnotateSources(image, disassembly);
    }

    private static async Task<NativeResult<IReadOnlyList<InstructionView>>> DisassembleInlineBytesModeAsync(
        string inlineBytes,
        string? architecture,
        string? baseAddress,
        int maxInstructions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(architecture))
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.InvalidArgument,
                "'--architecture' is required when '--bytes' is supplied.");
        }

        var architectureResult = ParseArchitecture(architecture!, rawBlobMode: false);
        if (architectureResult.IsError)
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                architectureResult.Error!.Kind,
                architectureResult.Error.Message,
                architectureResult.Error.Detail);
        }

        var baseAddressResult = ParseBaseAddress(baseAddress);
        if (baseAddressResult.IsError)
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                baseAddressResult.Error!.Kind,
                baseAddressResult.Error.Message,
                baseAddressResult.Error.Detail);
        }

        var bytesText = inlineBytes == "-"
            ? await Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false)
            : inlineBytes;

        var bytesResult = ParseInlineBytes(bytesText);
        if (bytesResult.IsError)
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                bytesResult.Error!.Kind,
                bytesResult.Error.Message,
                bytesResult.Error.Detail);
        }

        var image = new NativeImage(
            DotnetNativeMcp.Core.Identity.ImageHandle.From("cli-inline-bytes", "inline-bytes"),
            "inline-bytes",
            BinaryFormat.Pe,
            architectureResult.Data!.Value,
            [new NativeSection(".text", 0, (ulong)bytesResult.Data!.Length, 0, (ulong)bytesResult.Data.Length)],
            [],
            bytesResult.Data,
            baseAddressResult.Data ?? 0UL);

        return IcedDisassembler.Disassemble(image, 0, maxInstructions);
    }

    private static NativeResult<IReadOnlyList<InstructionView>> DisassembleBlobMode(
        CliInvocationContext invocation,
        string blobPath,
        string? architecture,
        string? baseAddress,
        int? size,
        int? rva,
        string? ilMapPath,
        int maxInstructions)
    {
        var blobValidation = invocation.PathPolicy.Validate(blobPath);
        if (blobValidation.IsError)
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                blobValidation.Error!.Kind,
                blobValidation.Error.Message,
                blobValidation.Error.Detail);
        }

        if (size is null || size.Value <= 0)
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.RawBlobMissingSize,
                "'size' is required when rawBlob=true and must be > 0.");
        }

        if (string.IsNullOrWhiteSpace(architecture))
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.RawBlobMissingArchitecture,
                "'architecture' is required when rawBlob=true. Supply 'x64', 'x86', or 'arm64'.");
        }

        var architectureResult = ParseArchitecture(architecture!, rawBlobMode: true);
        if (architectureResult.IsError)
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                architectureResult.Error!.Kind,
                architectureResult.Error.Message,
                architectureResult.Error.Detail);
        }

        if (string.IsNullOrWhiteSpace(baseAddress))
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.RawBlobMissingBaseAddress,
                "'baseAddress' is required when rawBlob=true so that call/jmp target addresses render correctly.");
        }

        var baseAddressResult = ParseBaseAddress(baseAddress);
        if (baseAddressResult.IsError)
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                baseAddressResult.Error!.Kind,
                baseAddressResult.Error.Message,
                baseAddressResult.Error.Detail);
        }

        string? canonicalIlMapPath = null;
        if (!string.IsNullOrWhiteSpace(ilMapPath))
        {
            var ilMapValidation = invocation.PathPolicy.Validate(ilMapPath!);
            if (ilMapValidation.IsError)
            {
                return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                    ilMapValidation.Error!.Kind,
                    ilMapValidation.Error.Message,
                    ilMapValidation.Error.Detail);
            }

            canonicalIlMapPath = ilMapValidation.Data;
        }

        JitIlMap? ilMap = null;
        if (!string.IsNullOrWhiteSpace(canonicalIlMapPath))
        {
            var ilMapResult = JitIlMap.Load(canonicalIlMapPath!);
            if (ilMapResult.IsError)
            {
                return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                    ilMapResult.Error!.Kind,
                    ilMapResult.Error.Message,
                    ilMapResult.Error.Detail);
            }

            ilMap = ilMapResult.Data;
        }

        return RawDisassembler.DisassembleBlob(
            blobValidation.Data!,
            rva ?? 0,
            size.Value,
            architectureResult.Data!.Value,
            baseAddressResult.Data!.Value,
            maxInstructions,
            ilMap);
    }

    private static NativeResult<IReadOnlyList<InstructionView>> AnnotateSources(
        NativeImage image,
        NativeResult<IReadOnlyList<InstructionView>> result)
    {
        var resolver = new SourceResolver();
        var annotated = result.Data!
            .Select(instruction =>
            {
                if (!ulong.TryParse(instruction.AddressHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address))
                {
                    return instruction;
                }

                var source = resolver.TrySourceFor(image, address);
                return source is null ? instruction : instruction with { Source = source };
            })
            .ToList();

        return NativeResult.Ok(result.Summary, (IReadOnlyList<InstructionView>)annotated, result.Hints);
    }

    private static NativeResult<byte[]> ParseInlineBytes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return NativeResult.Fail<byte[]>(
                ErrorKinds.InvalidArgument,
                "'--bytes' must not be empty.");
        }

        var trimmed = text.Trim();
        var hexCandidate = RemoveHexSeparators(trimmed);
        if (LooksLikeHex(hexCandidate))
        {
            if (hexCandidate.Length % 2 != 0)
            {
                return NativeResult.Fail<byte[]>(
                    ErrorKinds.InvalidArgument,
                    "Hex byte input must contain an even number of digits.");
            }

            try
            {
                return NativeResult.Ok("Parsed inline hex bytes.", Convert.FromHexString(hexCandidate));
            }
            catch (FormatException ex)
            {
                return NativeResult.Fail<byte[]>(
                    ErrorKinds.InvalidArgument,
                    "Could not parse '--bytes' as hex or base64.",
                    ex.Message);
            }
        }

        var base64Candidate = RemoveWhitespace(trimmed);
        try
        {
            return NativeResult.Ok("Parsed inline base64 bytes.", Convert.FromBase64String(base64Candidate));
        }
        catch (FormatException ex)
        {
            return NativeResult.Fail<byte[]>(
                ErrorKinds.InvalidArgument,
                "Could not parse '--bytes' as hex or base64.",
                ex.Message);
        }
    }

    private static NativeResult<Architecture?> ParseArchitecture(string architecture, bool rawBlobMode)
    {
        var parsed = architecture.Trim().ToLowerInvariant() switch
        {
            "x64" or "amd64" => Architecture.X64,
            "x86" or "i386" => Architecture.X86,
            "arm64" or "aarch64" => Architecture.Arm64,
            _ => Architecture.Unknown,
        };

        if (parsed != Architecture.Unknown)
        {
            return NativeResult.Ok("Parsed architecture.", (Architecture?)parsed);
        }

        return rawBlobMode
            ? NativeResult.Fail<Architecture?>(
                ErrorKinds.DisassemblyUnsupported,
                $"Unknown architecture '{architecture}' for rawBlob mode. Valid values: x64, x86, arm64.")
            : NativeResult.Fail<Architecture?>(
                ErrorKinds.InvalidArgument,
                $"Unknown architecture '{architecture}'. Valid values: x64, x86, arm64.");
    }

    private static NativeResult<ulong?> ParseBaseAddress(string? baseAddress)
    {
        if (string.IsNullOrWhiteSpace(baseAddress))
        {
            return NativeResult.Ok("Using default base address.", (ulong?)null);
        }

        if (TryParseHex(baseAddress!, out var value))
        {
            return NativeResult.Ok("Parsed base address.", (ulong?)value);
        }

        return NativeResult.Fail<ulong?>(
            ErrorKinds.InvalidArgument,
            $"Cannot parse base address '{baseAddress}' as a hex value.");
    }

    private static bool TryParseHex(string text, out ulong value)
    {
        var candidate = text.Trim();
        if (candidate.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[2..];
        }

        return ulong.TryParse(candidate, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static int CountSupplied(params string?[] values) =>
        values.Count(value => !string.IsNullOrWhiteSpace(value));

    private static string RemoveHexSeparators(string value)
    {
        var builder = new StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsWhiteSpace(c) || c == '_' || c == '-' || c == ':')
            {
                continue;
            }

            builder.Append(c);
        }

        var normalized = builder.ToString();
        return normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? normalized[2..]
            : normalized;
    }

    private static string RemoveWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var c in value)
        {
            if (!char.IsWhiteSpace(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static bool LooksLikeHex(string value) =>
        value.Length > 0 && value.All(Uri.IsHexDigit);
}
