using System.CommandLine;
using DotnetNativeMcp.Core;

namespace DotnetNativeMcp.Cli;

public static class VersionCommandFactory
{
    public static Command Create(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var command = new Command("version", "Show scaffold metadata and prove output dispatch end-to-end.");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var invocation = CliApplication.BuildInvocationContext(parseResult, options);
            var result = NativeResult.Ok(
                summary: "CLI scaffold is available.",
                data: new VersionCommandData(
                    ToolCommandName: CliMetadata.ToolCommandName,
                    Version: CliApplication.GetInformationalVersion(),
                    PathPolicyEnforcing: invocation.PathPolicy.Enforcing,
                    AllowedRoots: invocation.PathPolicy.AllowedRoots));

            await invocation.OutputWriter.WriteAsync(result, cancellationToken).ConfigureAwait(false);
            return 0;
        });

        return command;
    }
}

public sealed record VersionCommandData(
    string ToolCommandName,
    string Version,
    bool PathPolicyEnforcing,
    IReadOnlyList<string> AllowedRoots);
