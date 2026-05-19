namespace DotnetNativeMcp.Core;

public sealed record BinaryDiff(
    string Verdict,
    IReadOnlyList<NativeSymbolSize> AddedSymbols,
    IReadOnlyList<NativeSymbolSize> RemovedSymbols,
    IReadOnlyList<NativeSymbolDelta> GrowthHotspots,
    IReadOnlyList<NativeSymbolDelta> ShrunkSymbols,
    IReadOnlyDictionary<string, long> SectionSizeDelta);

public sealed record NativeSymbolSize(string Name, long Size);

public sealed record NativeSymbolDelta(
    string Name,
    long BaselineSize,
    long TargetSize,
    long DeltaSize,
    double DeltaPercent);
