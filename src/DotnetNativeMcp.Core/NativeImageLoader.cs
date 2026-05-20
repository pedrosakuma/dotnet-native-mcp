using DotnetNativeMcp.Core.Dgml;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Mstat;

namespace DotnetNativeMcp.Core;

/// <summary>
/// Loads and validates native binaries (ELF, PE, or Mach-O).
/// Verifies that the binary is a managed-flavored native build (NativeAOT or ReadyToRun)
/// before accepting it. Arbitrary system .so/.dll/.dylib files are rejected with
/// <see cref="ErrorKinds.NotANativeDotnetImage"/>.
/// </summary>
public static class NativeImageLoader
{
    /// <summary>
    /// Opens a native binary at <paramref name="path"/>, validates it is a managed-native
    /// build, and returns a <see cref="NativeImage"/> on success.
    /// </summary>
    public static NativeResult<NativeImage> Load(string path, string? expectedBuildId = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return NativeResult.Fail<NativeImage>(ErrorKinds.InvalidArgument, "path must not be empty.");

        if (!File.Exists(path))
            return NativeResult.Fail<NativeImage>(ErrorKinds.BinaryNotFound, $"Binary not found: '{path}'.");

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            return NativeResult.Fail<NativeImage>(ErrorKinds.InternalError,
                $"Failed to read '{path}': {ex.Message}", ex.ToString());
        }

        var memory = new ReadOnlyMemory<byte>(bytes);

        // Detect format and parse
        NativeImage? image = null;
        if (bytes.Length >= 4 &&
            bytes[0] == 0x7F && bytes[1] == (byte)'E' &&
            bytes[2] == (byte)'L' && bytes[3] == (byte)'F')
        {
            image = ElfReader.Read(memory, path);
        }
        else if (bytes.Length >= 2 && bytes[0] == 0x4D && bytes[1] == 0x5A)
        {
            image = PeNativeReader.Read(memory, path);
        }
        else if (MachOReader.IsFatBinary(bytes))
        {
            // Parse fat header and pick the best available slice (arm64 preferred, then x64).
            var sliceResult = MachOReader.ParseFatSlice(bytes);
            if (sliceResult.IsError)
                return NativeResult.Fail<NativeImage>(sliceResult.Error!.Kind, sliceResult.Error.Message, sliceResult.Error.Detail);

            var (offset, size, _) = sliceResult.Data;
            var sliceMemory = memory.Slice((int)offset, (int)size);

            // Check unsupported features on the selected slice before parsing
            var unsupported = MachOReader.CheckUnsupportedFeatures(sliceMemory.Span);
            if (unsupported is not null)
                return NativeResult.Fail<NativeImage>(ErrorKinds.MachoFeatureUnsupported, unsupported);

            image = MachOReader.Read(sliceMemory, path);
        }
        else if (MachOReader.IsMachO(bytes))
        {
            // Check unsupported features before parsing the thin binary
            var unsupported = MachOReader.CheckUnsupportedFeatures(bytes);
            if (unsupported is not null)
                return NativeResult.Fail<NativeImage>(ErrorKinds.MachoFeatureUnsupported, unsupported);

            image = MachOReader.Read(memory, path);
        }

        if (image is null)
            return NativeResult.Fail<NativeImage>(ErrorKinds.NotANativeDotnetImage,
                $"'{path}' is not a supported ELF, PE, or Mach-O binary.");

        // Build-id verification
        if (expectedBuildId is not null)
        {
            var actualId = image.Handle.BuildIdHex;
            if (!string.Equals(actualId, expectedBuildId, StringComparison.OrdinalIgnoreCase))
                return NativeResult.Fail<NativeImage>(ErrorKinds.BinaryMismatch,
                    $"Build-id mismatch: expected '{expectedBuildId}', got '{actualId}'.");
        }

        // Managed-native detection heuristic
        var looksManaged = image.Format switch
        {
            BinaryFormat.Elf => ElfReader.LooksLikeManagedNativeBuild(image),
            BinaryFormat.MachO => MachOReader.LooksLikeManagedNativeBuild(image),
            _ => PeNativeReader.LooksLikeManagedNativeBuild(image, bytes),
        };

        if (!looksManaged)
            return NativeResult.Fail<NativeImage>(ErrorKinds.NotANativeDotnetImage,
                $"'{Path.GetFileName(path)}' does not appear to be a NativeAOT or ReadyToRun binary. " +
                "No NativeAOT marker symbols, R2R header, or managed section were found.");

        // Attempt to merge .map sidecar if present
        var mapPath = MapFileReader.FindSidecar(path);
        if (mapPath is not null)
        {
            var merged = MapFileReader.TryMerge(mapPath, image.Symbols);
            if (merged is not null)
            {
                image = new NativeImage(
                    image.Handle, image.FilePath, image.Format, image.Architecture,
                    image.Sections, merged, image.RawBytes, image.ImageBase);
            }
        }

        return NativeResult.Ok(
            $"Loaded {image.Format} {image.Architecture} binary '{Path.GetFileName(path)}' " +
            $"with {image.Symbols.Count} symbols. Handle: {image.Handle.Value}.",
            image,
            BuildLoadHints(image));
    }

    /// <summary>Builds follow-up hints for a loaded image.</summary>
    public static IReadOnlyList<NextActionHint> BuildLoadHints(NativeImage image)
    {
        var hints = new List<NextActionHint>
        {
            new("list_native_symbols", "Enumerate all symbols in this image.",
                new Dictionary<string, object?> { ["imageHandle"] = image.Handle.Value }),
            new("resolve_symbol", "Resolve a specific symbol by name or address.",
                new Dictionary<string, object?> { ["imageHandle"] = image.Handle.Value }),
        };

        if (MstatReader.HasSiblingMstat(image.FilePath))
        {
            hints.Add(new NextActionHint(
                "get_size_breakdown",
                "A sibling .mstat sidecar is available for NativeAOT size analysis.",
                new Dictionary<string, object?> { ["imageHandle"] = image.Handle.Value }));
        }

        if (DgmlReader.HasSiblingDgml(image.FilePath))
        {
            hints.Add(new NextActionHint(
                "explain_retention",
                "A sibling .dgml sidecar is available for NativeAOT retention-path analysis.",
                new Dictionary<string, object?> { ["imageHandle"] = image.Handle.Value }));
        }

        return hints;
    }
}
