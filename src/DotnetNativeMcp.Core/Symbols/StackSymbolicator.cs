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
public sealed record NativeFrameInput(
    string Address,
    string? ImageHandle = null,
    string? BuildId = null,
    string? Binary = null);

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

        var rva = SymbolResolution.VaToRva(virtualAddress, image.ImageBase);
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

        if (symbol.IsFunction)
        {
            resolvedFunction = (selectedImageHandle, normalizedAddressHex, symbol.Name);
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

    private static bool TryParseHexAddress(string? address, out ulong value, out string normalizedHex)
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
