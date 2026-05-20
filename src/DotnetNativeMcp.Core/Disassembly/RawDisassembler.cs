using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;

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

        // Parse just enough to get sections and architecture — no managed-native check.
        var parsed = TryParseHeaders(rawBytes, imagePath);
        if (parsed is null && arch is null)
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.DisassemblyUnsupported,
                $"'{imagePath}' is not a recognised PE, ELF, or Mach-O binary. " +
                "Supply 'architecture' explicitly to disassemble an unknown format.");

        var resolvedArch = arch ?? parsed!.Architecture;

        // Resolve RVA → file offset using section table when available.
        int fileOffset;
        if (parsed is not null)
        {
            var fo = parsed.RvaToFileOffset((ulong)rva);
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

        if (fileOffset + size > rawBytes.Length)
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.AddressOutOfRange,
                $"RVA 0x{rva:x} + size {size} (file offset 0x{fileOffset:x}..0x{fileOffset + size:x}) " +
                $"exceeds the file length of {rawBytes.Length} bytes.");

        // Build a minimal synthetic NativeImage containing only the requested code slice.
        // Section VirtualAddress = rva, FileOffset = 0 so that RvaToFileOffset(rva) → 0.
        var codeBytes = new ReadOnlyMemory<byte>(rawBytes, fileOffset, size);
        var handle = ImageHandle.From(
            $"raw-{Math.Abs(imagePath.GetHashCode()):x}",
            Path.GetFileName(imagePath));
        var synthSection = new NativeSection(".text", (ulong)rva, (ulong)size, 0, (ulong)size);
        var imageBase = baseAddress ?? (parsed?.ImageBase ?? 0UL);
        var synthImage = new NativeImage(
            handle,
            imagePath,
            parsed?.Format ?? BinaryFormat.Pe,
            resolvedArch,
            [synthSection],
            [],
            codeBytes,
            imageBase);

        return IcedDisassembler.Disassemble(synthImage, (ulong)rva, maxInstructions);
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
}
