using System.Text.Json;
using DotnetNativeMcp.Core;

namespace DotnetNativeMcp.Cli.Output;

public sealed class JsonOutputWriter(TextWriter writer) : IOutputWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public async ValueTask WriteAsync<T>(NativeResult<T> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        var payload = new
        {
            result.Summary,
            result.Data,
            result.Hints,
            result.Error,
        };

        await writer.WriteLineAsync(JsonSerializer.Serialize(payload, SerializerOptions).AsMemory(), cancellationToken)
            .ConfigureAwait(false);
    }
}
