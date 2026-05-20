using System.Reflection;

namespace DotnetNativeMcp.Bench;

/// <summary>
/// Resolves paths to fixture binaries at bench runtime.
/// SampleAot is built by the Core.Tests project's BuildNativeAotFixture target;
/// System.Private.CoreLib.dll comes from the published NativeAOT output alongside SampleAot.
/// Both skip cleanly when not present.
/// </summary>
internal static class BenchFixturePaths
{
    private static readonly string? _repoRoot = FindRepoRoot();

    /// <summary>
    /// Path to the NativeAOT SampleAot ELF binary, or <c>null</c> if not found.
    /// Looks in the Core.Tests output directory relative to the repo root.
    /// </summary>
    public static string? SampleAot
    {
        get
        {
            if (_repoRoot is null)
                return null;

            // Location built by Core.Tests BuildNativeAotFixture target.
            var candidate = Path.Combine(
                _repoRoot,
                "tests", "DotnetNativeMcp.Core.Tests",
                "bin", "Release", "net10.0",
                "fixtures", "SampleAot", "SampleAot");

            return File.Exists(candidate) ? candidate : null;
        }
    }

    /// <summary>
    /// Path to System.Private.CoreLib.dll published alongside SampleAot, or <c>null</c> if not found.
    /// This is a large R2R managed PE used to exercise the bench on a realistic binary.
    /// </summary>
    public static string? SystemPrivateCoreLib
    {
        get
        {
            if (_repoRoot is null)
                return null;

            var candidate = Path.Combine(
                _repoRoot,
                "tests", "fixtures", "SampleAot",
                "bin", "Release", "net10.0", "linux-x64",
                "System.Private.CoreLib.dll");

            return File.Exists(candidate) ? candidate : null;
        }
    }

    private static string? FindRepoRoot()
    {
        // Walk up from the assembly's location until we find the solution file.
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "DotnetNativeMcp.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
