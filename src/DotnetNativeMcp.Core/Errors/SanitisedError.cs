using System.Diagnostics;

namespace DotnetNativeMcp.Core.Errors;

/// <summary>
/// Produces sanitised <see cref="NativeError.Detail"/> strings safe to ship to MCP clients:
/// strips absolute filesystem paths and never includes <c>Exception.ToString()</c> (which would
/// leak stack traces and internal call sites).
///
/// <para>
/// Full exception details remain available server-side via <see cref="Sink"/>: callers
/// (typically the MCP server's startup) can wire it to <c>ILogger</c>. When no sink is wired,
/// full details are written to <see cref="Trace"/>.
/// </para>
/// </summary>
public static class SanitisedError
{
    /// <summary>
    /// Optional diagnostics sink. The MCP server can wire this to an <c>ILogger</c> so that
    /// the full exception (including paths and stack trace) is recorded in server logs.
    /// When <see langword="null"/>, full details fall back to <see cref="Trace.WriteLine(string)"/>.
    /// </summary>
    public static Action<string>? Sink { get; set; }

    /// <summary>
    /// Returns a short, safe detail string for an exception: just the type name and a
    /// sanitised message. Logs the full exception (with paths and stack trace) to the
    /// configured diagnostics sink before returning.
    /// </summary>
    /// <param name="ex">The caught exception.</param>
    /// <param name="sensitivePaths">Absolute filesystem paths that, if present in
    /// <paramref name="ex"/>.Message, must be redacted to the file name only.</param>
    public static string From(Exception ex, params string?[] sensitivePaths)
    {
        ArgumentNullException.ThrowIfNull(ex);

        LogToSink(ex);

        var safeMessage = SanitiseMessage(ex.Message, sensitivePaths);
        return string.IsNullOrEmpty(safeMessage)
            ? ex.GetType().Name
            : $"{ex.GetType().Name}: {safeMessage}";
    }

    /// <summary>
    /// Sanitises a free-form message by replacing each absolute path in
    /// <paramref name="sensitivePaths"/> with its file name only. Returns <see langword="null"/>
    /// when the input is null or whitespace.
    /// </summary>
    public static string? SanitiseMessage(string? message, params string?[] sensitivePaths)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        if (sensitivePaths is null || sensitivePaths.Length == 0)
            return message;

        var result = message;
        foreach (var path in sensitivePaths)
        {
            if (string.IsNullOrEmpty(path))
                continue;

            var fileName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(fileName))
                continue;

            result = result.Replace(path, fileName, StringComparison.Ordinal);
        }

        return result;
    }

    private static void LogToSink(Exception ex)
    {
        try
        {
            var sink = Sink;
            if (sink is not null)
                sink(ex.ToString());
            else
                Trace.WriteLine(ex.ToString());
        }
        catch
        {
        }
    }
}
