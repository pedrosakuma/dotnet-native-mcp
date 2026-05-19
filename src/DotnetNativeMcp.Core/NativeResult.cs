using DotnetNativeMcp.Core.Errors;

namespace DotnetNativeMcp.Core;

/// <summary>Hint suggesting the next tool call after a successful result.</summary>
/// <param name="NextTool">Name of the recommended tool.</param>
/// <param name="Reason">Short explanation of why to call it next.</param>
/// <param name="SuggestedArguments">Optional pre-filled arguments for the next call.</param>
public sealed record NextActionHint(
    string NextTool,
    string Reason,
    IReadOnlyDictionary<string, object?>? SuggestedArguments = null);

/// <summary>
/// Uniform response envelope for all <c>dotnet-native-mcp</c> tools.
/// Mirrors the <c>AssemblyResult&lt;T&gt;</c> pattern from <c>dotnet-assembly-mcp</c>.
/// </summary>
/// <typeparam name="T">Payload type on success.</typeparam>
/// <param name="Summary">One-line human-readable description of the result.</param>
/// <param name="Data">Payload on success; <c>null</c> on error.</param>
/// <param name="Hints">Suggested follow-up actions for the LLM.</param>
/// <param name="Error">Populated on failure; <c>null</c> on success.</param>
public sealed record NativeResult<T>(
    string Summary,
    T? Data,
    IReadOnlyList<NextActionHint> Hints,
    NativeError? Error = null)
{
    /// <summary>True when the result represents an error.</summary>
    public bool IsError => Error is not null;
}

/// <summary>Factory helpers for <see cref="NativeResult{T}"/>.</summary>
public static class NativeResult
{
    /// <summary>Creates a successful result with optional hints.</summary>
    public static NativeResult<T> Ok<T>(string summary, T data, IReadOnlyList<NextActionHint>? hints = null) =>
        new(summary, data, hints ?? [], null);

    /// <summary>Creates a failure result.</summary>
    public static NativeResult<T> Fail<T>(string kind, string message, string? detail = null) =>
        new(
            $"Error ({kind}): {message}",
            default,
            [],
            new NativeError(kind, message, detail));
}
