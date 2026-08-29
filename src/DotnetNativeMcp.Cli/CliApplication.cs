using System.CommandLine;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace DotnetNativeMcp.Cli;

public static class CliApplication
{
    public static RootCommand CreateRootCommand()
    {
        var options = new CliOptions();
        var root = new RootCommand(CliMetadata.Description);

        root.Options.Add(options.Output);
        root.Options.Add(options.Allow);
        root.Subcommands.Add(DisasmCommandFactory.Create(options));
        root.Subcommands.Add(ResolveCommandFactory.Create(options));
        root.Subcommands.Add(CallersCommandFactory.Create(options));
        root.Subcommands.Add(SizeCommandFactory.Create(options));
        root.Subcommands.Add(SizeDiffCommandFactory.Create(options));
        root.Subcommands.Add(VersionCommandFactory.Create(options));
        root.Subcommands.Add(R2rCommandFactory.Create(options));
        root.Subcommands.Add(SymbolsCommandFactory.Create(options));
        root.Subcommands.Add(ImportsCommandFactory.Create(options));

        return root;
    }

    public static Task<int> InvokeAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var configuration = new InvocationConfiguration();
        return CreateRootCommand().Parse(args).InvokeAsync(configuration, cancellationToken);
    }

    internal static CliInvocationContext BuildInvocationContext(ParseResult parseResult, CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(options);

        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var allowRoots = parseResult.GetValue(options.Allow) ?? [];
        var outputFormat = OutputFormat.Parse(parseResult.GetValue(options.Output));
        var pathPolicy = CliPathPolicyFactory.Build(configuration, allowRoots);
        var outputWriter = OutputWriterFactory.Create(outputFormat, Console.Out);

        return new CliInvocationContext(outputWriter, pathPolicy);
    }

    internal static string GetInformationalVersion() =>
        typeof(CliApplication).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(CliApplication).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
