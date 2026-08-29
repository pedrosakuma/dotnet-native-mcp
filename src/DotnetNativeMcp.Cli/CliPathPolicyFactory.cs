using DotnetNativeMcp.Core.Security;
using Microsoft.Extensions.Configuration;

namespace DotnetNativeMcp.Cli;

public static class CliPathPolicyFactory
{
    public static PathAccessPolicy Build(IConfiguration configuration, IReadOnlyList<string> commandLineRoots)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(commandLineRoots);

        var roots = new List<string>(commandLineRoots.Count);

        foreach (var child in configuration.GetSection("NativeMcp:AllowedBinaryRoots").GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(child.Value))
            {
                roots.Add(child.Value);
            }
        }

        var envRoots = configuration["NATIVE_MCP_ALLOWED_ROOTS"];
        if (!string.IsNullOrWhiteSpace(envRoots))
        {
            roots.AddRange(envRoots.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        var binariesDir = configuration["BINARIES_DIR"];
        if (!string.IsNullOrWhiteSpace(binariesDir))
        {
            roots.Add(binariesDir);
        }

        roots.AddRange(commandLineRoots);
        return PathPolicyBuilder.Build(roots);
    }
}
