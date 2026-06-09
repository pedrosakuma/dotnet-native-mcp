namespace DotnetNativeMcp.Core.Security;

/// <summary>
/// Builds a <see cref="PathAccessPolicy"/> from operator-configured trusted roots,
/// augmented with always-available well-known roots that handed-off binaries
/// legitimately live under (the NuGet global packages cache, the .NET shared
/// framework, and the system temp directory used for sidecar artifacts).
///
/// <para>
/// Enforcement is keyed off whether the <b>operator</b> configured any root: the
/// well-known defaults are added to the allowlist so they pass when enforcing, but
/// their presence alone never flips the policy into enforcing mode. This keeps the
/// permissive-by-default posture while making "set one root and everything still
/// resolves" ergonomic.
/// </para>
/// </summary>
public static class PathPolicyBuilder
{
    /// <summary>
    /// Constructs a policy. When <paramref name="operatorRoots"/> contains at least one
    /// usable entry the policy is enforcing; otherwise it is permissive.
    /// </summary>
    public static PathAccessPolicy Build(IReadOnlyList<string> operatorRoots)
    {
        ArgumentNullException.ThrowIfNull(operatorRoots);

        var usableOperatorRoots = operatorRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .ToList();

        var enforcing = usableOperatorRoots.Count > 0;

        if (!enforcing)
            return PathAccessPolicy.Permissive;

        var allRoots = new List<string>(usableOperatorRoots);
        allRoots.AddRange(WellKnownRoots());

        return new PathAccessPolicy(allRoots, enforcing: true);
    }

    /// <summary>
    /// Well-known roots that handed-off native binaries and their sidecars commonly
    /// resolve under, regardless of operator configuration.
    /// </summary>
    public static IEnumerable<string> WellKnownRoots()
    {
        // NuGet global packages cache (native runtime packages land here).
        var nugetPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(nugetPackages))
            yield return nugetPackages;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
            yield return Path.Combine(userProfile, ".nuget", "packages");

        // .NET shared framework / runtime install root.
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
            yield return dotnetRoot;

        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
                yield return Path.Combine(programFiles, "dotnet");
        }
        else
        {
            yield return "/usr/share/dotnet";
            yield return "/usr/lib/dotnet";
        }

        // Temp directory: where sidecar producers (capture_method_bytes, ilmap) stage artifacts.
        var temp = Path.GetTempPath();
        if (!string.IsNullOrWhiteSpace(temp))
            yield return temp;
    }
}
