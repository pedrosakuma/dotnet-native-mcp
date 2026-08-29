using System.CommandLine;

namespace DotnetNativeMcp.Cli;

public sealed class CliOptions
{
    public Option<string> Output { get; } = CreateOutputOption();

    public Option<string[]> Allow { get; } = CreateAllowOption();

    private static Option<string> CreateOutputOption()
    {
        var option = new Option<string>("--output")
        {
            Description = "Render command results as 'json' (default) or 'table'.",
            DefaultValueFactory = _ => OutputFormat.Json.Value,
            Recursive = true,
        };

        option.AcceptOnlyFromAmong(OutputFormat.Json.Value, OutputFormat.Table.Value);
        return option;
    }

    private static Option<string[]> CreateAllowOption()
    {
        return new Option<string[]>("--allow")
        {
            Description = "Allow access to binaries under this root. Repeat the option to add multiple trusted roots.",
            AllowMultipleArgumentsPerToken = true,
            Recursive = true,
        };
    }
}
