namespace DotnetNativeMcp.Core.Security;

/// <summary>
/// Resolves filesystem paths to their true on-disk target by flattening
/// <c>..</c> traversal and following every reparse point (POSIX symlinks, NTFS
/// symlinks / junctions) along the path. The result is the canonical path used
/// for allowlist policy checks, per the cross-MCP handoff contract's
/// "Path hints are untrusted" rule.
///
/// <para>
/// <see cref="Path.GetFullPath(string)"/> alone flattens <c>..</c> but does
/// <b>not</b> follow reparse points, so a symlink (or a directory junction in
/// the path) could otherwise escape an allowlisted root. This helper walks the
/// path component by component, resolving each link to its final target.
/// </para>
/// </summary>
public static class PathCanonicalizer
{
    /// <summary>Upper bound on link resolutions to break symlink cycles.</summary>
    private const int MaxLinkResolutions = 40;

    /// <summary>
    /// Returns the canonical, fully-resolved absolute path for <paramref name="path"/>.
    /// Non-existent leaf components are tolerated: the existing ancestor portion is
    /// resolved and the remaining (non-existent) tail is appended, so a path that does
    /// not yet exist still produces a stable canonical form for policy checks (the
    /// caller's later <c>File.Exists</c> reports the missing file).
    /// </summary>
    public static string ResolveRealPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
            return full;

        var rest = full[root.Length..];
        var segments = rest.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        var current = root;
        var resolutions = 0;

        foreach (var segment in segments)
        {
            var candidate = Path.Combine(current, segment);
            current = ResolveComponent(candidate, ref resolutions);
        }

        return Path.GetFullPath(current);
    }

    /// <summary>
    /// Boundary-aware containment check: returns true when <paramref name="canonical"/>
    /// equals one of <paramref name="roots"/> exactly, or sits beneath it with a directory
    /// separator at the boundary (so <c>/binaries-secret</c> is NOT under <c>/binaries</c>).
    /// Comparison is case-insensitive only on Windows (whose filesystems are case-insensitive);
    /// every other platform — including case-sensitive macOS volumes — compares case-sensitively
    /// so a case-mismatched path cannot escape an allowed root.
    /// </summary>
    public static bool IsUnderAllowedRoot(string canonical, IReadOnlyList<string> roots)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        ArgumentNullException.ThrowIfNull(roots);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root))
                continue;

            var trimmedRoot = TrimTrailingSeparator(root);

            if (canonical.Equals(trimmedRoot, comparison))
                return true;

            if (canonical.Length > trimmedRoot.Length &&
                canonical.StartsWith(trimmedRoot, comparison) &&
                IsSeparator(canonical[trimmedRoot.Length]))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveComponent(string candidate, ref int resolutions)
    {
        // Follow the final-target chain for this component if it is a reparse point.
        // ResolveLinkTarget returns null for a normal entry or one that does not exist.
        while (true)
        {
            string? target;
            try
            {
                FileSystemInfo info = Directory.Exists(candidate)
                    ? new DirectoryInfo(candidate)
                    : new FileInfo(candidate);
                target = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
            }
            catch (IOException)
            {
                // Too many levels of symbolic links, or a malformed link: stop resolving.
                return candidate;
            }
            catch (UnauthorizedAccessException)
            {
                return candidate;
            }

            if (target is null)
                return candidate;

            if (++resolutions > MaxLinkResolutions)
                return candidate;

            // Targets may be relative to the link's own directory.
            candidate = Path.IsPathRooted(target)
                ? Path.GetFullPath(target)
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(candidate) ?? string.Empty, target));
        }
    }

    private static string TrimTrailingSeparator(string root)
    {
        var trimmed = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // Preserve a bare root like "/" or "C:\".
        return trimmed.Length == 0 ? root : trimmed;
    }

    private static bool IsSeparator(char c) =>
        c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
}
