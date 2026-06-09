using DotnetNativeMcp.Core.Errors;

namespace DotnetNativeMcp.Core.Security;

/// <summary>
/// Enforces the consumer-side "path hints are untrusted" rule of the cross-MCP
/// handoff contract. Every filesystem path that arrives off the wire — the binary
/// path on <c>load_native_binary</c> / <c>import_native_manifest</c>, the
/// <c>imagePath</c> / <c>ilMapPath</c> on <c>disassemble</c>, and the sidecar
/// overrides on <c>explain_retention</c> / <c>get_size_breakdown</c> — is
/// canonicalised (symlinks/junctions resolved, <c>..</c> flattened) and then, when
/// the policy is <see cref="Enforcing"/>, checked against a fixed allowlist of
/// trusted roots before the file is opened.
///
/// <para>
/// Enforcement is opt-in: the policy only rejects paths once an operator configures
/// at least one trusted root. When no roots are configured the policy is permissive
/// (it still canonicalises, so downstream consumers always see the resolved path) and
/// the server emits a one-time startup warning recommending that roots be set.
/// </para>
/// </summary>
public sealed class PathAccessPolicy
{
    /// <summary>A permissive policy that canonicalises but never rejects. Default for tests and unconfigured hosts.</summary>
    public static PathAccessPolicy Permissive { get; } = new([], enforcing: false);

    private readonly IReadOnlyList<string> _allowedRoots;

    /// <summary>
    /// Creates a policy over the supplied <paramref name="allowedRoots"/>. Each root is
    /// canonicalised once here so the per-request hot path only canonicalises the candidate.
    /// </summary>
    /// <param name="allowedRoots">Trusted root directories. Non-existent or unresolvable roots are dropped.</param>
    /// <param name="enforcing">When true, paths outside <paramref name="allowedRoots"/> are rejected.</param>
    public PathAccessPolicy(IReadOnlyList<string> allowedRoots, bool enforcing)
    {
        ArgumentNullException.ThrowIfNull(allowedRoots);

        var canonical = new List<string>(allowedRoots.Count);
        foreach (var root in allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            try
            {
                canonical.Add(PathCanonicalizer.ResolveRealPath(root));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                // Skip a root we cannot canonicalise rather than failing startup.
            }
        }

        _allowedRoots = canonical;
        Enforcing = enforcing;
    }

    /// <summary>True when the policy rejects paths outside <see cref="AllowedRoots"/>.</summary>
    public bool Enforcing { get; }

    /// <summary>The canonicalised trusted roots this policy allows.</summary>
    public IReadOnlyList<string> AllowedRoots => _allowedRoots;

    /// <summary>
    /// Canonicalises <paramref name="path"/> and, when <see cref="Enforcing"/>, verifies the
    /// result is under an allowlisted root. On success returns the canonical path that the
    /// caller should open (symlinks already resolved). On failure returns
    /// <see cref="ErrorKinds.PathNotAllowed"/> with the rejected canonical path surfaced.
    /// </summary>
    public NativeResult<string> Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return NativeResult.Fail<string>(ErrorKinds.InvalidArgument, "path must not be empty.");

        string canonical;
        try
        {
            canonical = PathCanonicalizer.ResolveRealPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return NativeResult.Fail<string>(
                ErrorKinds.InvalidArgument,
                $"Could not canonicalise path '{Path.GetFileName(path)}'.",
                SanitisedError.From(ex, path));
        }

        if (!Enforcing)
            return NativeResult.Ok($"Path '{Path.GetFileName(canonical)}' canonicalised.", canonical);

        if (PathCanonicalizer.IsUnderAllowedRoot(canonical, _allowedRoots))
            return NativeResult.Ok($"Path '{Path.GetFileName(canonical)}' is within an allowed root.", canonical);

        return NativeResult.Fail<string>(
            ErrorKinds.PathNotAllowed,
            $"Path '{canonical}' is outside the configured allowlist of trusted binary roots. " +
            "Configure NativeMcp:AllowedBinaryRoots (or NATIVE_MCP_ALLOWED_ROOTS) to include its directory.");
    }
}
