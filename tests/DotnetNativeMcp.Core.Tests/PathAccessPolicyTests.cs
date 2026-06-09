using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Security;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public sealed class PathAccessPolicyTests
{
    [Fact]
    public void Permissive_DoesNotEnforce_AndReturnsCanonicalPath()
    {
        var policy = PathAccessPolicy.Permissive;
        policy.Enforcing.Should().BeFalse();

        var result = policy.Validate("/some/where/../where/binary.so");
        result.IsError.Should().BeFalse();
        result.Data.Should().Be(Path.GetFullPath("/some/where/binary.so"));
    }

    [Fact]
    public void Validate_EmptyPath_ReturnsInvalidArgument()
    {
        var policy = PathAccessPolicy.Permissive;
        var result = policy.Validate("   ");
        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void Enforcing_PathInsideRoot_IsAllowed()
    {
        using var root = new TempDir();
        var binary = Path.Combine(root.Path, "app", "native.so");
        Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
        File.WriteAllText(binary, "x");

        var policy = new PathAccessPolicy([root.Path], enforcing: true);
        var result = policy.Validate(binary);

        result.IsError.Should().BeFalse();
        result.Data.Should().Be(Path.GetFullPath(binary));
    }

    [Fact]
    public void Enforcing_PathOutsideRoot_IsRejected()
    {
        using var root = new TempDir();
        var policy = new PathAccessPolicy([root.Path], enforcing: true);

        var result = policy.Validate("/etc/shadow");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.PathNotAllowed);
    }

    [Fact]
    public void Enforcing_TraversalEscape_IsRejected()
    {
        using var root = new TempDir();
        var policy = new PathAccessPolicy([root.Path], enforcing: true);

        // ../ climbs out of the allowed root after canonicalisation.
        var escaping = Path.Combine(root.Path, "..", "outside", "evil.so");
        var result = policy.Validate(escaping);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.PathNotAllowed);
    }

    [Fact]
    public void Enforcing_SiblingRootPrefix_IsNotConsideredInside()
    {
        using var parent = new TempDir();
        var allowed = Path.Combine(parent.Path, "binaries");
        var sibling = Path.Combine(parent.Path, "binaries-secret");
        Directory.CreateDirectory(allowed);
        Directory.CreateDirectory(sibling);
        var secret = Path.Combine(sibling, "x.so");
        File.WriteAllText(secret, "x");

        var policy = new PathAccessPolicy([allowed], enforcing: true);
        var result = policy.Validate(secret);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.PathNotAllowed);
    }

    [Fact]
    public void Enforcing_SymlinkEscape_IsRejected()
    {
        if (OperatingSystem.IsWindows())
            return; // symlink creation may require elevation on Windows.

        using var root = new TempDir();
        using var outside = new TempDir();

        var secret = Path.Combine(outside.Path, "secret.so");
        File.WriteAllText(secret, "x");

        var link = Path.Combine(root.Path, "link.so");
        File.CreateSymbolicLink(link, secret);

        var policy = new PathAccessPolicy([root.Path], enforcing: true);
        var result = policy.Validate(link);

        // The symlink lives inside the root but resolves outside it → rejected.
        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.PathNotAllowed);
    }

    [Fact]
    public void Enforcing_SymlinkWithinRoot_IsAllowed()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var root = new TempDir();
        var target = Path.Combine(root.Path, "real.so");
        File.WriteAllText(target, "x");
        var link = Path.Combine(root.Path, "link.so");
        File.CreateSymbolicLink(link, target);

        var policy = new PathAccessPolicy([root.Path], enforcing: true);
        var result = policy.Validate(link);

        result.IsError.Should().BeFalse();
        result.Data.Should().Be(Path.GetFullPath(target));
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Path = Directory.CreateTempSubdirectory("native-mcp-pathtest-").FullName;

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

public sealed class PathPolicyBuilderTests
{
    [Fact]
    public void Build_NoRoots_ReturnsPermissive()
    {
        var policy = PathPolicyBuilder.Build([]);
        policy.Enforcing.Should().BeFalse();
    }

    [Fact]
    public void Build_BlankRoots_ReturnsPermissive()
    {
        var policy = PathPolicyBuilder.Build(["", "   "]);
        policy.Enforcing.Should().BeFalse();
    }

    [Fact]
    public void Build_WithRoot_IsEnforcingAndIncludesWellKnownRoots()
    {
        using var root = new DisposableDir();
        var policy = PathPolicyBuilder.Build([root.Path]);

        policy.Enforcing.Should().BeTrue();
        // Temp directory is a well-known root and is always added when enforcing.
        var tempProbe = Path.Combine(Path.GetTempPath(), "probe.so");
        PathCanonicalizer.IsUnderAllowedRoot(Path.GetFullPath(tempProbe), policy.AllowedRoots)
            .Should().BeTrue();
    }

    private sealed class DisposableDir : IDisposable
    {
        public DisposableDir() => Path = Directory.CreateTempSubdirectory("native-mcp-builder-").FullName;

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
