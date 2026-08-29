using DotnetNativeMcp.Cli.Output;

namespace DotnetNativeMcp.Cli;

public static class OutputWriterFactory
{
    public static IOutputWriter Create(OutputFormat format, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(writer);

        return format.Value switch
        {
            "json" => new JsonOutputWriter(writer),
            "table" => new TableOutputWriter(writer),
            _ => throw new InvalidOperationException($"Unsupported output format '{format.Value}'.")
        };
    }
}
