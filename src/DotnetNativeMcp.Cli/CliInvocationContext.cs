using DotnetNativeMcp.Core.Security;
using DotnetNativeMcp.Cli.Output;

namespace DotnetNativeMcp.Cli;

public sealed class CliInvocationContext(IOutputWriter outputWriter, PathAccessPolicy pathPolicy)
{
    public IOutputWriter OutputWriter { get; } = outputWriter;

    public PathAccessPolicy PathPolicy { get; } = pathPolicy;
}
