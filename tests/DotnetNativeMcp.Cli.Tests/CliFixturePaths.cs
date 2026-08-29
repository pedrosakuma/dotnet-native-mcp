using System.Reflection;

namespace DotnetNativeMcp.Cli.Tests;

internal static class CliFixturePaths
{
    public static string? SampleAot
    {
        get
        {
            var repoRoot = FindRepoRoot();
            if (repoRoot is null)
            {
                return null;
            }

            // Read directly from the fixture output that
            // DotnetNativeMcp.Core.Tests's BuildNativeAotFixture target
            // publishes and copies alongside its own tests
            // (tests/DotnetNativeMcp.Core.Tests/bin/Release/net10.0/fixtures/SampleAot/),
            // where the native binary and its .mstat sidecar sit side by
            // side. Do NOT re-publish from this project: a second concurrent
            // `dotnet publish` of the same fixture project races with
            // Core.Tests's own publish and corrupts the native link
            // (observed as "file too short" from ld).
            var candidate = Path.Combine(repoRoot, "tests", "DotnetNativeMcp.Core.Tests", "bin", "Release", "net10.0", "fixtures", "SampleAot", "SampleAot");
            return File.Exists(candidate) ? candidate : null;
        }
    }

    public static string? SampleAotMstat
    {
        get
        {
            var binary = SampleAot;
            return binary is null ? null : Path.ChangeExtension(binary, ".mstat");
        }
    }

    public static string? FindRepoRoot()
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "DotnetNativeMcp.slnx")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
