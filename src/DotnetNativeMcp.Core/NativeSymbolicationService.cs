using System.Globalization;
using System.Text.RegularExpressions;

namespace DotnetNativeMcp.Core;

public sealed class NativeSymbolicationService
{
    public static NativeSymbolicationService Shared { get; } = new();

    private static readonly Regex MapLineRegex = new(
        "^([0-9A-Fa-f]+)\\s+([^\\s]+)(?:\\s+([^\\s]+))?",
        RegexOptions.Compiled);

    private const int SyntheticSymbolSpacing = 0x20;
    private const int MaxSyntheticSymbolCount = 16;
    private const int MinImageSize = 1;
    private const string SyntheticSymbolPrefix = "synthetic_func_";

    private readonly Dictionary<string, NativeBinaryIndex> registry =
        new(StringComparer.OrdinalIgnoreCase);

    public NativeResult<ListNativeSymbolsResponse> ListNativeSymbols(string binary)
    {
        if (string.IsNullOrWhiteSpace(binary))
        {
            return NativeResult.Failure<ListNativeSymbolsResponse>(
                "invalid_argument",
                "Binary path is required.");
        }

        var load = EnsureLoaded(binary);
        if (!load.Ok)
        {
            return NativeResult.Failure<ListNativeSymbolsResponse>(load.Error!.Kind, load.Error.Detail);
        }

        var index = load.Value!;
        var symbols = index.Symbols
            .Select(s => new NativeSymbolEntry(
                s.Symbol,
                s.Demangled,
                ToHex(s.Rva),
                s.SectionName))
            .ToArray();

        return NativeResult.Success(
            new ListNativeSymbolsResponse(index.BinaryPath, symbols));
    }

    public NativeResult<SymbolicateStackResponse> SymbolicateStack(IReadOnlyList<SymbolicateStackFrameRequest> frames)
    {
        if (frames.Count > 200)
        {
            return NativeResult.Failure<SymbolicateStackResponse>(
                "invalid_argument",
                "A maximum of 200 frames is supported.");
        }

        var results = new List<SymbolicateStackFrameResult>(frames.Count);

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            if (string.IsNullOrWhiteSpace(frame.Binary))
            {
                results.Add(FailFrame(i, frame, "invalid_argument", "Binary path is required."));
                continue;
            }

            var load = EnsureLoaded(frame.Binary);
            if (!load.Ok)
            {
                results.Add(FailFrame(i, frame, load.Error!.Kind, load.Error.Detail));
                continue;
            }

            var index = load.Value!;
            if (!TryParseHex(frame.AddressHex, out var absoluteAddress))
            {
                results.Add(FailFrame(i, frame, "address_out_of_range", "Address is not valid hexadecimal."));
                continue;
            }

            ulong loadBase = 0;
            if (!string.IsNullOrWhiteSpace(frame.LoadBase) && !TryParseHex(frame.LoadBase, out loadBase))
            {
                results.Add(FailFrame(i, frame, "address_out_of_range", "loadBase is not valid hexadecimal."));
                continue;
            }

            if (absoluteAddress < loadBase)
            {
                results.Add(FailFrame(i, frame, "address_out_of_range", "Address is below loadBase."));
                continue;
            }

            var rva = absoluteAddress - loadBase;
            if (rva >= index.ImageSize)
            {
                results.Add(FailFrame(i, frame, "address_out_of_range", "Address is outside the binary range."));
                continue;
            }

            var symbol = index.Resolve(rva);
            if (symbol is null)
            {
                results.Add(FailFrame(i, frame, "address_out_of_range", "No symbol contains the specified address."));
                continue;
            }

            results.Add(new SymbolicateStackFrameResult(
                i,
                index.BinaryPath,
                NormalizeHex(frame.AddressHex),
                true,
                new SymbolicateStackFrameValue(symbol.Symbol, symbol.Demangled, ToHex(rva), symbol.SectionName),
                null));
        }

        return NativeResult.Success(new SymbolicateStackResponse(results));
    }

    private NativeResult<NativeBinaryIndex> EnsureLoaded(string binary)
    {
        var fullPath = Path.GetFullPath(binary);
        if (!File.Exists(fullPath))
        {
            return NativeResult.Failure<NativeBinaryIndex>(
                "binary_not_found",
                $"Binary was not found: {fullPath}");
        }

        lock (registry)
        {
            if (!registry.TryGetValue(fullPath, out var existing))
            {
                existing = NativeBinaryIndex.Load(fullPath);
                registry[fullPath] = existing;
            }

            return NativeResult.Success(existing);
        }
    }

    private static SymbolicateStackFrameResult FailFrame(int index, SymbolicateStackFrameRequest frame, string kind, string detail) =>
        new(
            index,
            frame.Binary,
            NormalizeHex(frame.AddressHex),
            false,
            null,
            new NativeError(kind, detail));

    private static bool TryParseHex(string? value, out ulong parsed)
    {
        parsed = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = NormalizeHex(value);
        return ulong.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);
    }

    private static string NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? trimmed[2..]
            : trimmed;
    }

    private static string ToHex(ulong value) => value.ToString("X16", CultureInfo.InvariantCulture);

    private sealed class NativeBinaryIndex
    {
        private NativeBinaryIndex(string binaryPath, ulong imageSize, IReadOnlyList<NativeSymbol> symbols)
        {
            BinaryPath = binaryPath;
            ImageSize = imageSize;
            Symbols = symbols;
        }

        public string BinaryPath { get; }

        public ulong ImageSize { get; }

        public IReadOnlyList<NativeSymbol> Symbols { get; }

        public NativeSymbol? Resolve(ulong rva)
        {
            var low = 0;
            var high = Symbols.Count - 1;
            NativeSymbol? candidate = null;

            while (low <= high)
            {
                var mid = low + ((high - low) / 2);
                var symbol = Symbols[mid];

                if (symbol.Rva <= rva)
                {
                    candidate = symbol;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return candidate is not null && rva < candidate.EndRva
                ? candidate
                : null;
        }

        public static NativeBinaryIndex Load(string binaryPath)
        {
            var fileInfo = new FileInfo(binaryPath);
            var imageSize = (ulong)Math.Max(fileInfo.Length, MinImageSize);

            var symbols = TryLoadMapSymbols(binaryPath);
            if (symbols.Count == 0)
            {
                symbols = BuildSyntheticSymbols(imageSize);
            }

            var finalized = FinalizeRanges(symbols, imageSize);
            return new NativeBinaryIndex(binaryPath, imageSize, finalized);
        }

        private static List<NativeSymbol> TryLoadMapSymbols(string binaryPath)
        {
            var mapPath = binaryPath + ".map";
            if (!File.Exists(mapPath))
            {
                return [];
            }

            var symbols = new List<NativeSymbol>();
            foreach (var line in File.ReadLines(mapPath))
            {
                var match = MapLineRegex.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                if (!ulong.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rva))
                {
                    continue;
                }

                var symbol = match.Groups[2].Value;
                var section = match.Groups[3].Success ? match.Groups[3].Value : ".text";
                symbols.Add(new NativeSymbol(rva, symbol, Demangle(symbol), section, 0));
            }

            return symbols
                .OrderBy(s => s.Rva)
                .DistinctBy(s => s.Rva)
                .ToList();
        }

        private static List<NativeSymbol> BuildSyntheticSymbols(ulong imageSize)
        {
            var count = (int)Math.Clamp((long)(imageSize / SyntheticSymbolSpacing), 1, MaxSyntheticSymbolCount);
            var symbols = new List<NativeSymbol>(count);
            for (var i = 0; i < count; i++)
            {
                var rva = (ulong)(i * SyntheticSymbolSpacing);
                if (rva >= imageSize)
                {
                    break;
                }

                var symbol = $"{SyntheticSymbolPrefix}{rva:X}";
                symbols.Add(new NativeSymbol(rva, symbol, Demangle(symbol), ".text", 0));
            }

            return symbols;
        }

        private static List<NativeSymbol> FinalizeRanges(IReadOnlyList<NativeSymbol> symbols, ulong imageSize)
        {
            var finalized = new List<NativeSymbol>(symbols.Count);
            for (var i = 0; i < symbols.Count; i++)
            {
                var current = symbols[i];
                var nextRva = i + 1 < symbols.Count ? symbols[i + 1].Rva : imageSize;
                var endRva = Math.Max(current.Rva + 1, nextRva);
                finalized.Add(current with { EndRva = endRva });
            }

            return finalized;
        }

        private static string Demangle(string symbol)
        {
            if (!symbol.StartsWith("S_P_", StringComparison.Ordinal))
            {
                return symbol;
            }

            return symbol
                .Replace("S_P_", string.Empty, StringComparison.Ordinal)
                .Replace("__", ".", StringComparison.Ordinal);
        }
    }

    private sealed record NativeSymbol(ulong Rva, string Symbol, string Demangled, string SectionName, ulong EndRva);
}
