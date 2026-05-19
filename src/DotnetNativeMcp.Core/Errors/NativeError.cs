namespace DotnetNativeMcp.Core.Errors;

/// <summary>Structured error payload returned inside <see cref="NativeResult{T}"/> when a tool call fails.</summary>
/// <param name="Kind">Stable kind string from <see cref="ErrorKinds"/>.</param>
/// <param name="Message">Human-readable message.</param>
/// <param name="Detail">Optional technical detail (stack trace, inner message).</param>
public sealed record NativeError(string Kind, string Message, string? Detail = null);
