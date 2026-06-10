using System.Globalization;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;

namespace DotnetNativeMcp.Core.Symbols;

/// <summary>
/// Input row for batch native stack symbolication.
/// </summary>
/// <param name="Address">Hex RVA or absolute VA. A <c>0x</c> prefix is accepted and stripped.</param>
/// <param name="ImageHandle">Optional image handle override for this frame.</param>
/// <param name="BuildId">Optional producer build-id carried through from a <c>NativeFrame</c> handoff.</param>
/// <param name="Binary">Optional producer binary path carried through from a <c>NativeFrame</c> handoff.</param>
/// <param name="LoadBase">
/// Optional module load base observed by the producer (<c>NativeFrame.loadBase</c>). When supplied,
/// <see cref="Address"/> is treated as a runtime absolute VA and rebased as <c>rva = address - loadBase</c>,
/// which is required to resolve ASLR'd position-independent (PIE) binaries whose on-disk image base is 0.
/// When <c>null</c> the on-disk image base is used (the non-ASLR / PE default).
/// </param>
public sealed record NativeFrameInput(
    string Address,
    string? ImageHandle = null,
    string? BuildId = null,
    string? Binary = null,
    ulong? LoadBase = null);

/// <summary>
/// One row returned by batch address resolution via <c>resolve_symbols</c>.
/// </summary>
/// <param name="InputAddress">The original address string as supplied by the caller.</param>
/// <param name="ResolvedRvaHex">Normalized 16-digit hex RVA after successful address parsing, or <c>null</c> on error.</param>
/// <param name="MangledName">Resolved raw (mangled) symbol name on success.</param>
/// <param name="DemangledName">Best-effort demangled symbol name on success.</param>
/// <param name="SectionName">Containing section name when available.</param>
/// <param name="Displacement">Byte offset from the start of the resolved symbol.</param>
/// <param name="Error">Per-row error payload. The batch still succeeds when individual rows fail.</param>
public sealed record ResolvedAddress(
    string InputAddress,
    string? ResolvedRvaHex,
    string? MangledName,
    string? DemangledName,
    string? SectionName,
    ulong? Displacement,
    NativeError? Error = null)
{
    /// <summary>True when the row represents a per-address failure.</summary>
    public bool IsError => Error is not null;
}

/// <summary>
/// One row returned by batch native stack symbolication.
/// </summary>
/// <param name="Index">Zero-based position of the input frame.</param>
/// <param name="InputAddressHex">Normalized hex form of the input address, or the trimmed raw value when parsing failed.</param>
/// <param name="ImageHandle">Image handle used for this row, if any.</param>
/// <param name="ResolvedRvaHex">Resolved RVA for the address, if parsing and image lookup succeeded.</param>
/// <param name="MangledName">Resolved mangled symbol name on success.</param>
/// <param name="DemangledName">Best-effort demangled symbol name on success.</param>
/// <param name="SectionName">Containing section name when available.</param>
/// <param name="OffsetFromSymbolStart">Byte offset from the start of the resolved symbol.</param>
/// <param name="Error">Per-row error payload. The batch still succeeds when individual rows fail.</param>
public sealed record SymbolicatedFrame(
    int Index,
    string InputAddressHex,
    string? ImageHandle,
    string? ResolvedRvaHex,
    string? MangledName,
    string? DemangledName,
    string? SectionName,
    ulong? OffsetFromSymbolStart,
    NativeError? Error = null)
{
    /// <summary>True when the row represents a per-frame failure.</summary>
    public bool IsError => Error is not null;
}

/// <summary>
/// Resolves batches of native stack frames against loaded native images.
/// </summary>
public static class StackSymbolicator
{
    /// <summary>Maximum number of frames accepted by a single batch.</summary>
    public const int MaxFrameCount = 200;

    /// <summary>
    /// Resolves a batch of address strings against a single loaded native image.
    /// Accepts hex (<c>0x</c>-prefixed or bare hex digits) and decimal strings.
    /// An empty list returns an empty result without error.
    /// Per-address failures (bad parse, symbol not found) are reported inline; the
    /// top-level result only errors when the image itself is <c>null</c>.
    /// </summary>
    /// <param name="image">The loaded native image to resolve against.</param>
    /// <param name="addresses">The address strings to resolve.</param>
    /// <param name="loadBase">
    /// Optional producer-observed module load base (<c>NativeFrame.loadBase</c>). When supplied each
    /// address is treated as a runtime absolute VA and rebased as <c>rva = address - loadBase</c>;
    /// this is required for ASLR'd PIE binaries whose on-disk image base is 0. When <c>null</c> the
    /// image's on-disk base is used.
    /// </param>
    public static NativeResult<IReadOnlyList<ResolvedAddress>> ResolveAddresses(
        NativeImage image,
        IReadOnlyList<string>? addresses,
        ulong? loadBase = null)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (addresses is null || addresses.Count == 0)
        {
            return NativeResult.Ok(
                "Resolved 0 of 0 addresses.",
                (IReadOnlyList<ResolvedAddress>)[]);
        }

        var rows = new List<ResolvedAddress>(addresses.Count);
        var resolvedCount = 0;
        string? hintAddressHex = null;
        string? hintSymbolName = null;

        foreach (var raw in addresses)
        {
            var row = ResolveAddress(image, raw, loadBase, out var resolvedFunction);
            rows.Add(row);
            if (!row.IsError)
                resolvedCount++;

            if (resolvedFunction is not null && hintAddressHex is null)
            {
                hintAddressHex = resolvedFunction.Value.AddressHex;
                hintSymbolName = resolvedFunction.Value.SymbolName;
            }
        }

        var hints = new List<NextActionHint>();
        if (hintAddressHex is not null)
        {
            hints.Add(new NextActionHint(
                "disassemble",
                $"Disassemble the first resolved function ('{hintSymbolName}').",
                new Dictionary<string, object?>
                {
                    ["imageHandle"] = image.Handle.Value,
                    ["address"] = hintAddressHex,
                }));
        }

        var errorCount = rows.Count - resolvedCount;
        return NativeResult.Ok(
            $"Resolved {resolvedCount} of {rows.Count} address(es); {errorCount} row(s) returned errors.",
            (IReadOnlyList<ResolvedAddress>)rows,
            hints);
    }

    private static ResolvedAddress ResolveAddress(
        NativeImage image,
        string raw,
        ulong? loadBase,
        out (string AddressHex, string SymbolName)? resolvedFunction)
    {
        resolvedFunction = null;

        if (!TryParseAddress(raw, out var virtualAddress, out _))
        {
            return new ResolvedAddress(
                raw,
                null, null, null, null, null,
                new NativeError(ErrorKinds.InvalidArgument, $"Cannot parse address '{raw}' as a hex or decimal value."));
        }

        // When the producer supplied an explicit loadBase the address is a runtime absolute VA, so
        // an address below the load base cannot map into the image — reject it rather than letting
        // VaToRva's "below base ⇒ already an RVA" fallback resolve a bogus nearest symbol.
        if (loadBase is { } lb && virtualAddress < lb)
        {
            return new ResolvedAddress(
                raw,
                null, null, null, null, null,
                new NativeError(ErrorKinds.AddressOutOfRange,
                    $"Address 0x{virtualAddress:x} is below the supplied loadBase 0x{lb:x}."));
        }

        var rva = SymbolResolution.VaToRva(virtualAddress, loadBase ?? image.ImageBase);
        var resolvedRvaHex = rva.ToString("x16", CultureInfo.InvariantCulture);
        var section = image.FindSection(rva);
        var symbol = SymbolResolution.FindByRva(image.Symbols, rva);

        if (symbol is null)
        {
            if (section is null)
            {
                return new ResolvedAddress(
                    raw,
                    resolvedRvaHex, null, null, null, null,
                    new NativeError(ErrorKinds.AddressOutOfRange,
                        $"Address 0x{virtualAddress:x} is outside the known sections of the binary."));
            }

            return new ResolvedAddress(
                raw,
                resolvedRvaHex, null, null, section.Name, null,
                new NativeError(ErrorKinds.SymbolNotFound,
                    $"No symbol found for address 0x{virtualAddress:x} in section '{section.Name}'."));
        }

        var sectionName = symbol.Section ?? section?.Name;
        var demangledName = string.IsNullOrEmpty(symbol.DemangledName)
            ? NativeAotSymbolDemangler.Demangle(symbol.Name)
            : symbol.DemangledName;
        var displacement = rva - symbol.Rva;

        // The disassemble hint must point at an address disassemble can rebase on its own (it only
        // knows image.ImageBase, never loadBase). Hand it the on-disk VA of the function start.
        if (symbol.IsFunction)
        {
            var onDiskVaHex = (image.ImageBase + symbol.Rva).ToString("x16", CultureInfo.InvariantCulture);
            resolvedFunction = (onDiskVaHex, symbol.Name);
        }

        return new ResolvedAddress(raw, resolvedRvaHex, symbol.Name, demangledName, sectionName, displacement);
    }

    /// <summary>
    /// Attempts to parse <paramref name="raw"/> as an address.
    /// Accepts <c>0x</c>-prefixed hex, bare hex strings, and decimal strings.
    /// </summary>
    public static bool TryParseAddress(string? raw, out ulong value, out string normalizedHex)
    {
        value = 0;
        normalizedHex = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var candidate = raw.Trim();

        // Explicit hex prefix → always hex.
        if (candidate.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var hexPart = candidate[2..];
            if (hexPart.Length == 0 || !ulong.TryParse(hexPart, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                return false;

            normalizedHex = value.ToString("x16", CultureInfo.InvariantCulture);
            return true;
        }

        // No prefix: try decimal first, then hex.
        if (ulong.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out value))
        {
            normalizedHex = value.ToString("x16", CultureInfo.InvariantCulture);
            return true;
        }

        if (ulong.TryParse(candidate, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
        {
            normalizedHex = value.ToString("x16", CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Symbolicates a batch of native frames using images already present in the registry.
    /// </summary>
    public static NativeResult<IReadOnlyList<SymbolicatedFrame>> SymbolicateStack(
        INativeBinaryRegistry registry,
        IReadOnlyList<NativeFrameInput>? frames,
        string? defaultImageHandle = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (frames is null || frames.Count == 0)
        {
            return NativeResult.Fail<IReadOnlyList<SymbolicatedFrame>>(
                ErrorKinds.InvalidArgument,
                $"Supply between 1 and {MaxFrameCount} frames.");
        }

        if (frames.Count > MaxFrameCount)
        {
            return NativeResult.Fail<IReadOnlyList<SymbolicatedFrame>>(
                ErrorKinds.InvalidArgument,
                $"Frame count {frames.Count} exceeds the maximum of {MaxFrameCount}.");
        }

        var rows = new List<SymbolicatedFrame>(frames.Count);
        string? hintImageHandle = null;
        string? hintAddressHex = null;
        string? hintSymbolName = null;
        var resolvedCount = 0;

        for (var i = 0; i < frames.Count; i++)
        {
            var row = SymbolicateFrame(registry, frames[i], i, defaultImageHandle, out var resolvedFunction);
            rows.Add(row);

            if (!row.IsError)
            {
                resolvedCount++;
            }

            if (resolvedFunction is not null && hintImageHandle is null)
            {
                hintImageHandle = resolvedFunction.Value.ImageHandle;
                hintAddressHex = resolvedFunction.Value.InputAddressHex;
                hintSymbolName = resolvedFunction.Value.SymbolName;
            }
        }

        var hints = new List<NextActionHint>();
        if (hintImageHandle is not null && hintAddressHex is not null)
        {
            hints.Add(new NextActionHint(
                "disassemble",
                $"Disassemble the first resolved function frame ('{hintSymbolName}').",
                new Dictionary<string, object?>
                {
                    ["imageHandle"] = hintImageHandle,
                    ["address"] = hintAddressHex,
                }));
        }

        var errorCount = rows.Count - resolvedCount;
        return NativeResult.Ok(
            $"Symbolicated {resolvedCount} of {rows.Count} frame(s); {errorCount} row(s) returned errors.",
            (IReadOnlyList<SymbolicatedFrame>)rows,
            hints);
    }

    private static SymbolicatedFrame SymbolicateFrame(
        INativeBinaryRegistry registry,
        NativeFrameInput frame,
        int index,
        string? defaultImageHandle,
        out (string ImageHandle, string InputAddressHex, string SymbolName)? resolvedFunction)
    {
        resolvedFunction = null;

        var selectedImageHandle = FirstNonEmpty(frame.ImageHandle, defaultImageHandle);
        var rawAddress = NormalizeAddressText(frame.Address);

        if (selectedImageHandle is null)
        {
            return ErrorRow(
                index,
                rawAddress,
                null,
                null,
                null,
                ErrorKinds.InvalidArgument,
                "No image handle was supplied for this frame. Set frame.imageHandle or defaultImageHandle.");
        }

        if (!registry.TryGet(selectedImageHandle, out var image) || image is null)
        {
            return ErrorRow(
                index,
                rawAddress,
                selectedImageHandle,
                null,
                null,
                ErrorKinds.BinaryNotFound,
                $"No image found for handle '{selectedImageHandle}'. Call load_native_binary first.");
        }

        if (!TryParseHexAddress(frame.Address, out var virtualAddress, out var normalizedAddressHex))
        {
            return ErrorRow(
                index,
                rawAddress,
                selectedImageHandle,
                null,
                null,
                ErrorKinds.InvalidArgument,
                $"Cannot parse address '{frame.Address}' as a hex value.");
        }

        if (frame.LoadBase is { } lb && virtualAddress < lb)
        {
            return ErrorRow(
                index,
                normalizedAddressHex,
                selectedImageHandle,
                null,
                null,
                ErrorKinds.AddressOutOfRange,
                $"Address 0x{virtualAddress:x} is below the supplied loadBase 0x{lb:x}.");
        }

        var rva = SymbolResolution.VaToRva(virtualAddress, frame.LoadBase ?? image.ImageBase);
        var resolvedRvaHex = rva.ToString("x16", CultureInfo.InvariantCulture);
        var section = image.FindSection(rva);
        var symbol = SymbolResolution.FindByRva(image.Symbols, rva);

        if (symbol is null)
        {
            if (section is null)
            {
                return ErrorRow(
                    index,
                    normalizedAddressHex,
                    selectedImageHandle,
                    resolvedRvaHex,
                    null,
                    ErrorKinds.AddressOutOfRange,
                    $"Address 0x{virtualAddress:x} is outside the known sections of '{selectedImageHandle}'.");
            }

            return ErrorRow(
                index,
                normalizedAddressHex,
                selectedImageHandle,
                resolvedRvaHex,
                section.Name,
                ErrorKinds.SymbolNotFound,
                $"No symbol found for address 0x{virtualAddress:x} in section '{section.Name}'.");
        }

        var sectionName = symbol.Section ?? section?.Name;
        var demangledName = string.IsNullOrEmpty(symbol.DemangledName)
            ? NativeAotSymbolDemangler.Demangle(symbol.Name)
            : symbol.DemangledName;
        var offsetFromSymbolStart = rva - symbol.Rva;

        // Hand disassemble the on-disk VA of the function start; it rebases against image.ImageBase
        // only and has no knowledge of the producer's loadBase.
        if (symbol.IsFunction)
        {
            var onDiskVaHex = (image.ImageBase + symbol.Rva).ToString("x16", CultureInfo.InvariantCulture);
            resolvedFunction = (selectedImageHandle, onDiskVaHex, symbol.Name);
        }

        return new SymbolicatedFrame(
            index,
            normalizedAddressHex,
            selectedImageHandle,
            resolvedRvaHex,
            symbol.Name,
            demangledName,
            sectionName,
            offsetFromSymbolStart);
    }

    private static SymbolicatedFrame ErrorRow(
        int index,
        string inputAddressHex,
        string? imageHandle,
        string? resolvedRvaHex,
        string? sectionName,
        string kind,
        string message) =>
        new(index, inputAddressHex, imageHandle, resolvedRvaHex, null, null, sectionName, null, new NativeError(kind, message));

    private static string? FirstNonEmpty(string? preferred, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(preferred)) return preferred;
        if (!string.IsNullOrWhiteSpace(fallback)) return fallback;
        return null;
    }

    private static string NormalizeAddressText(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return string.Empty;
        var candidate = address.Trim();
        return candidate.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? candidate[2..]
            : candidate;
    }

    /// <summary>
    /// Attempts to parse <paramref name="address"/> as a hex address. Accepts an optional
    /// <c>0x</c> prefix and bare hex digits. Unlike <see cref="TryParseAddress"/> this never
    /// interprets the value as decimal, matching the handoff contract's hex transport format
    /// for addresses and load bases.
    /// </summary>
    public static bool TryParseHexAddress(string? address, out ulong value, out string normalizedHex)
    {
        value = 0;
        normalizedHex = string.Empty;

        var candidate = NormalizeAddressText(address);
        if (candidate.Length == 0)
        {
            return false;
        }

        if (!ulong.TryParse(candidate, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        normalizedHex = value.ToString("x16", CultureInfo.InvariantCulture);
        return true;
    }
}
