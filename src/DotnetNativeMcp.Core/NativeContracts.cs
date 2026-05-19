namespace DotnetNativeMcp.Core;

public sealed record NativeError(string Kind, string Detail);

public sealed record NativeResult<T>(bool Ok, T? Value, NativeError? Error);

public static class NativeResult
{
    public static NativeResult<T> Success<T>(T value) => new(true, value, null);

    public static NativeResult<T> Failure<T>(string kind, string detail) =>
        new(false, default, new NativeError(kind, detail));
}

public sealed record NativeSymbolEntry(string Symbol, string Demangled, string AddressHex, string SectionName);

public sealed record ListNativeSymbolsResponse(string Binary, IReadOnlyList<NativeSymbolEntry> Symbols);

public sealed record SymbolicateStackFrameRequest(string Binary, string AddressHex, string? LoadBase = null);

public sealed record SymbolicateStackFrameValue(string Symbol, string Demangled, string Rva, string SectionName);

public sealed record SymbolicateStackFrameResult(
    int Index,
    string Binary,
    string AddressHex,
    bool Ok,
    SymbolicateStackFrameValue? Value,
    NativeError? Error);

public sealed record SymbolicateStackResponse(IReadOnlyList<SymbolicateStackFrameResult> Frames);
