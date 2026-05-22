using System.Buffers.Binary;
using System.Globalization;
using System.Reflection.PortableExecutable;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.R2R;

namespace DotnetNativeMcp.Core.Disassembly;

/// <summary>
/// Disassembles native machine code directly from a file path + RVA + size triple,
/// without requiring the binary to be registered via <c>load_native_binary</c>.
/// <para>
/// This is the asm-mcp → native-mcp handoff target for ReadyToRun method bodies
/// that live inside managed PEs (which <c>load_native_binary</c> rejects with
/// <c>not_a_native_dotnet_image</c>).
/// </para>
/// </summary>
public static class RawDisassembler
{
    /// <summary>
    /// Disassembles up to <paramref name="maxInstructions"/> instructions from a raw
    /// file at the file offset that corresponds to <paramref name="rva"/>.
    /// </summary>
    /// <param name="imagePath">Absolute path to a PE, ELF, or Mach-O binary.</param>
    /// <param name="rva">
    /// Start offset as a relative virtual address (RVA for PE; virtual address for ELF
    /// where imageBase is typically 0).
    /// </param>
    /// <param name="size">Number of bytes of code to supply to the decoder.</param>
    /// <param name="arch">
    /// CPU architecture override. When <c>null</c> the architecture is detected from
    /// the binary's PE/ELF/Mach-O header.
    /// </param>
    /// <param name="baseAddress">
    /// Optional image base used to format absolute virtual addresses in the output.
    /// When <c>null</c> the value from the parsed header is used.
    /// </param>
    /// <param name="maxInstructions">Maximum number of instructions to decode.</param>
    public static NativeResult<IReadOnlyList<InstructionView>> Disassemble(
        string imagePath,
        int rva,
        int size,
        Architecture? arch,
        ulong? baseAddress,
        int maxInstructions)
    {
        if (!File.Exists(imagePath))
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.BinaryNotFound,
                $"File not found: '{imagePath}'.");

        byte[] rawBytes;
        try
        {
            rawBytes = File.ReadAllBytes(imagePath);
        }
        catch (Exception ex)
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.BinaryNotFound,
                $"Failed to read '{imagePath}': {ex.Message}");
        }

        // Reject negative or zero sizes and negative RVAs up-front: in unknown-format
        // raw-bytes mode we treat `rva` as a direct file offset, and `int` underflow
        // would let an attacker bypass the `fileOffset + size > rawBytes.Length`
        // check below.
        if (size <= 0)
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.AddressOutOfRange,
                $"size must be positive (got {size}).");
        if (rva < 0)
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.AddressOutOfRange,
                $"RVA must be non-negative (got {rva}).");

        // Parse just enough to get sections and architecture — no managed-native check.
        var parsed = TryParseHeaders(rawBytes, imagePath);
        if (parsed is null && arch is null)
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.DisassemblyUnsupported,
                $"'{imagePath}' is not a recognised PE, ELF, or Mach-O binary. " +
                "Supply 'architecture' explicitly to disassemble an unknown format.");

        var parsedImage = arch is null ? TryApplyReadyToRunArchitectureFallback(parsed, rva, size) : parsed;
        var resolvedArch = arch ?? parsedImage!.Architecture;

        // Resolve RVA → file offset using section table when available.
        int fileOffset;
        if (parsedImage is not null)
        {
            var fo = parsedImage.RvaToFileOffset((ulong)rva);
            if (fo is null)
                return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                    ErrorKinds.AddressOutOfRange,
                    $"RVA 0x{rva:x} is not inside any known section in '{imagePath}'.");
            fileOffset = fo.Value;
        }
        else
        {
            // Unknown format with user-supplied arch: treat RVA as a direct file offset.
            fileOffset = rva;
        }

        // Use long arithmetic so a crafted (rva, size) pair can't wrap past int.MaxValue.
        if ((long)fileOffset + size > rawBytes.Length)
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.AddressOutOfRange,
                $"RVA 0x{rva:x} + size {size} (file offset 0x{fileOffset:x}..0x{(long)fileOffset + size:x}) " +
                $"exceeds the file length of {rawBytes.Length} bytes.");

        // Build a minimal synthetic NativeImage containing only the requested code slice.
        // Section VirtualAddress = rva, FileOffset = 0 so that RvaToFileOffset(rva) → 0.
        var codeBytes = new ReadOnlyMemory<byte>(rawBytes, fileOffset, size);
        var handle = ImageHandle.From(
            $"raw-{Math.Abs(imagePath.GetHashCode()):x}",
            Path.GetFileName(imagePath));
        var synthSection = new NativeSection(".text", (ulong)rva, (ulong)size, 0, (ulong)size);
        var imageBase = baseAddress ?? (parsedImage?.ImageBase ?? 0UL);
        var synthImage = new NativeImage(
            handle,
            imagePath,
            parsedImage?.Format ?? BinaryFormat.Pe,
            resolvedArch,
            [synthSection],
            [],
            codeBytes,
            imageBase);

        return IcedDisassembler.Disassemble(synthImage, (ulong)rva, maxInstructions);
    }

    /// <summary>
    /// Disassembles up to <paramref name="maxInstructions"/> instructions from a raw
    /// byte blob (no PE/ELF/Mach-O header). All parameters are required because there
    /// is no header from which to infer them.
    /// </summary>
    /// <param name="blobPath">Absolute path to the raw instruction bytes.</param>
    /// <param name="offset">Byte offset within the blob to begin decoding (typically 0).</param>
    /// <param name="size">Number of bytes of code to supply to the decoder.</param>
    /// <param name="arch">CPU architecture — must be supplied.</param>
    /// <param name="baseAddress">
    /// Absolute virtual address of byte 0 of the blob. Used to format absolute
    /// addresses for call/jmp targets correctly.
    /// </param>
    /// <param name="maxInstructions">Maximum number of instructions to decode.</param>
    /// <param name="ilMap">Optional IL-to-native map used to annotate each decoded instruction with its IL offset.</param>
    public static NativeResult<IReadOnlyList<InstructionView>> DisassembleBlob(
        string blobPath,
        int offset,
        int size,
        Architecture arch,
        ulong baseAddress,
        int maxInstructions,
        JitIlMap? ilMap = null)
    {
        if (!File.Exists(blobPath))
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.BinaryNotFound,
                $"File not found: '{blobPath}'.");

        byte[] rawBytes;
        try
        {
            rawBytes = File.ReadAllBytes(blobPath);
        }
        catch (Exception ex)
        {
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.BinaryNotFound,
                $"Failed to read '{blobPath}': {ex.Message}");
        }

        if (offset < 0 || size <= 0 || offset > rawBytes.Length || size > rawBytes.Length - offset)
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.AddressOutOfRange,
                $"Offset {offset} + size {size} (end offset {offset + size}) exceeds blob length {rawBytes.Length}.");

        // Build a minimal synthetic NativeImage: section VirtualAddress=offset, FileOffset=0
        // so RvaToFileOffset(offset) → 0 inside the code slice.
        // imageBase = baseAddress so ip = imageBase + rva = baseAddress + offset,
        // giving the correct absolute VA for each decoded instruction.
        var codeBytes = new ReadOnlyMemory<byte>(rawBytes, offset, size);
        var handle = ImageHandle.From(
            $"blob-{Math.Abs(blobPath.GetHashCode()):x}",
            Path.GetFileName(blobPath));
        var synthSection = new NativeSection(".text", (ulong)offset, (ulong)size, 0, (ulong)size);
        var synthImage = new NativeImage(
            handle,
            blobPath,
            BinaryFormat.Pe,
            arch,
            [synthSection],
            [],
            codeBytes,
            baseAddress);

        var disassembly = IcedDisassembler.Disassemble(synthImage, (ulong)offset, maxInstructions);
        if (disassembly.IsError || ilMap is null)
            return disassembly;

        var annotated = disassembly.Data!
            .Select(instruction =>
            {
                if (!ulong.TryParse(instruction.AddressHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address) ||
                    address < baseAddress)
                    return instruction;

                return instruction with { IlOffset = ilMap.FindIlOffset(address - baseAddress) };
            })
            .ToList();

        return NativeResult.Ok(disassembly.Summary, (IReadOnlyList<InstructionView>)annotated, disassembly.Hints);
    }

    private static NativeImage? TryParseHeaders(byte[] bytes, string filePath)
    {
        var mem = new ReadOnlyMemory<byte>(bytes);

        if (bytes.Length >= 4 &&
            bytes[0] == 0x7F && bytes[1] == (byte)'E' &&
            bytes[2] == (byte)'L' && bytes[3] == (byte)'F')
        {
            return ElfReader.Read(mem, filePath);
        }

        if (bytes.Length >= 2 && bytes[0] == 0x4D && bytes[1] == 0x5A)
        {
            return PeNativeReader.Read(mem, filePath);
        }

        if (MachOReader.IsMachO(bytes))
        {
            return MachOReader.Read(mem, filePath);
        }

        return null;
    }

    private static NativeImage? TryApplyReadyToRunArchitectureFallback(NativeImage? image, int requestedRva, int requestedSize)
    {
        if (image is null || image.Format != BinaryFormat.Pe || image.Architecture != Architecture.Unknown)
            return image;

        var inferredArchitecture = TryInferReadyToRunArchitecture(image, requestedRva, requestedSize);
        return inferredArchitecture is null ? image : WithArchitecture(image, inferredArchitecture.Value);
    }

    private static Architecture? TryInferReadyToRunArchitecture(NativeImage image, int requestedRva, int requestedSize)
    {
        var headerResult = ReadyToRunReader.ReadHeader(image);
        if (headerResult.IsError)
            return null;

        var machineArchitecture = ReadyToRunReader.TryReadTargetArchitecture(image);
        if (machineArchitecture is not null)
            return machineArchitecture;

        var header = headerResult.Data!;
        var runtimeFunctions = header.FindSection(ReadyToRunSectionType.RuntimeFunctions);
        if (runtimeFunctions is not null)
        {
            if (LooksLikeX64RuntimeFunctions(image, runtimeFunctions))
                return Architecture.X64;

            if (LooksLikeArm64RuntimeFunctions(image, header, runtimeFunctions))
                return Architecture.Arm64;
        }

        if (header.FindSection(ReadyToRunSectionType.MethodHeaderAndCodeInfo) is not null)
        {
            var probeArchitecture = TryInferArchitectureFromDecodeProbe(image, requestedRva, requestedSize);
            if (probeArchitecture is not null)
                return probeArchitecture;
        }

        try
        {
            using var stream = new MemoryStream(image.RawBytes.ToArray(), writable: false);
            using var peReader = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);
            if (peReader.PEHeaders.PEHeader?.Magic == PEMagic.PE32)
                return Architecture.X86;
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static bool LooksLikeX64RuntimeFunctions(NativeImage image, ReadyToRunSection runtimeFunctions)
    {
        const int RuntimeFunctionSize = 12;

        if (runtimeFunctions.Size < RuntimeFunctionSize || runtimeFunctions.Size % RuntimeFunctionSize != 0)
            return false;

        var fileOffset = image.RvaToFileOffset(runtimeFunctions.VirtualAddress);
        if (fileOffset is null)
            return false;

        var bytes = image.RawBytes.Span;
        var sampleCount = Math.Min(5, (int)(runtimeFunctions.Size / RuntimeFunctionSize));
        if (sampleCount == 0)
            return false;

        for (var i = 0; i < sampleCount; i++)
        {
            var entryOffset = fileOffset.Value + (i * RuntimeFunctionSize);
            if (entryOffset + RuntimeFunctionSize > bytes.Length)
                return false;

            var beginAddress = BinaryPrimitives.ReadUInt32LittleEndian(bytes[entryOffset..]);
            var endAddress = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(entryOffset + 4)..]);
            var unwindInfoAddress = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(entryOffset + 8)..]);

            if (beginAddress == 0 || endAddress <= beginAddress)
                return false;

            if (image.RvaToFileOffset(beginAddress) is null ||
                image.RvaToFileOffset(endAddress - 1u) is null ||
                image.RvaToFileOffset(unwindInfoAddress) is null)
            {
                return false;
            }
        }

        return true;
    }

    private static bool LooksLikeArm64RuntimeFunctions(
        NativeImage image,
        ReadyToRunHeader header,
        ReadyToRunSection runtimeFunctions)
    {
        const int RuntimeFunctionSize = 8;

        if (runtimeFunctions.Size < RuntimeFunctionSize || runtimeFunctions.Size % RuntimeFunctionSize != 0)
            return false;

        var fileOffset = image.RvaToFileOffset(runtimeFunctions.VirtualAddress);
        if (fileOffset is null)
            return false;

        var arm64Image = WithArchitecture(image, Architecture.Arm64);
        var bytes = image.RawBytes.Span;
        var sampleCount = Math.Min(5, (int)(runtimeFunctions.Size / RuntimeFunctionSize));
        if (sampleCount == 0)
            return false;

        for (var i = 0; i < sampleCount; i++)
        {
            var entryOffset = fileOffset.Value + (i * RuntimeFunctionSize);
            if (entryOffset + RuntimeFunctionSize > bytes.Length)
                return false;

            var beginAddress = BinaryPrimitives.ReadUInt32LittleEndian(bytes[entryOffset..]);
            if (beginAddress == 0 || arm64Image.RvaToFileOffset(beginAddress) is null)
                return false;

            var functionResult = ReadyToRunReader.FindRuntimeFunction(arm64Image, header, beginAddress);
            if (functionResult.IsError)
                return false;

            var function = functionResult.Data!;
            if (function.Index != i ||
                function.BeginAddress != beginAddress ||
                function.EndAddress <= beginAddress ||
                arm64Image.RvaToFileOffset(function.EndAddress - 1u) is null)
            {
                return false;
            }
        }

        return true;
    }

    private static Architecture? TryInferArchitectureFromDecodeProbe(NativeImage image, int requestedRva, int requestedSize)
    {
        if (requestedSize <= 0)
            return null;

        var fileOffset = image.RvaToFileOffset((ulong)requestedRva);
        if (fileOffset is null)
            return null;

        var sampleSize = Math.Min(Math.Min(requestedSize, 16), image.RawBytes.Length - fileOffset.Value);
        if (sampleSize < 4)
            return null;

        var sample = image.RawBytes.Slice(fileOffset.Value, sampleSize);
        var x64Score = ScoreDecodeProbe(BuildProbeImage(image, sample, requestedRva, Architecture.X64));
        var arm64Score = ScoreDecodeProbe(BuildProbeImage(image, sample, requestedRva, Architecture.Arm64));

        if (x64Score >= 3 && x64Score >= arm64Score + 2)
            return Architecture.X64;

        if (arm64Score >= 3 && arm64Score >= x64Score + 2)
            return Architecture.Arm64;

        return null;
    }

    private static int ScoreDecodeProbe(NativeImage image)
    {
        var result = IcedDisassembler.Disassemble(image, image.Sections[0].VirtualAddress, maxInstructions: 4);
        if (result.IsError || result.Data is null || result.Data.Count == 0)
            return 0;

        var instructions = result.Data;
        var score = instructions.Count;
        score += instructions.Count(instruction => instruction.Mnemonic != "unknown");
        score += instructions.Count(instruction => instruction.Bytes.Length >= 6);
        score -= instructions.Count(instruction => instruction.Mnemonic == "unknown") * 2;

        return score;
    }

    private static NativeImage BuildProbeImage(
        NativeImage sourceImage,
        ReadOnlyMemory<byte> sample,
        int requestedRva,
        Architecture architecture) =>
        new(
            sourceImage.Handle,
            sourceImage.FilePath,
            sourceImage.Format,
            architecture,
            [new NativeSection(".text", (ulong)requestedRva, (ulong)sample.Length, 0, (ulong)sample.Length)],
            [],
            sample,
            sourceImage.ImageBase);

    private static NativeImage WithArchitecture(NativeImage image, Architecture architecture) =>
        new(
            image.Handle,
            image.FilePath,
            image.Format,
            architecture,
            image.Sections,
            image.Symbols,
            image.RawBytes,
            image.ImageBase);
}
