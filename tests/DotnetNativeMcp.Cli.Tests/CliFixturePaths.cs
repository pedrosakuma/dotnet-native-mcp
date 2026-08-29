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

            // Read directly from the raw SampleAot publish output that
            // DotnetNativeMcp.Core.Tests's BuildNativeAotFixture target produces
            // (tests/fixtures/SampleAot/...). Do NOT re-publish from this project:
            // running a second concurrent `dotnet publish` of the same fixture
            // project races with Core.Tests's own publish and corrupts the native
            // link (observed as "file too short" from ld).
            var candidate = Path.Combine(repoRoot, "tests", "fixtures", "SampleAot", "bin", "Release", "net10.0", "linux-x64", "SampleAot");
            return File.Exists(candidate) ? candidate : null;
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
