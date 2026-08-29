namespace DotnetNativeMcp.Cli;

public sealed record OutputFormat(string Value)
{
    public static OutputFormat Json { get; } = new("json");

    public static OutputFormat Table { get; } = new("table");

    public static OutputFormat Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "json" => Json,
        "table" => Table,
        _ => throw new InvalidOperationException($"Unsupported output format '{value}'.")
    };
}
