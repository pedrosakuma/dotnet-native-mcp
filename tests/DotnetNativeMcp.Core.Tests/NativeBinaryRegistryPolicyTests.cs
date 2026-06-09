using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Security;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public sealed class NativeBinaryRegistryPolicyTests
{
    [Fact]
    public void Load_EnforcingPolicy_OutsideRoot_ReturnsPathNotAllowed()
    {
        var root = Directory.CreateTempSubdirectory("native-mcp-reg-");
        try
        {
            var policy = new PathAccessPolicy([root.FullName], enforcing: true);
            var registry = new NativeBinaryRegistry(policy);

            var result = registry.Load("/etc/shadow");

            result.IsError.Should().BeTrue();
            result.Error!.Kind.Should().Be(ErrorKinds.PathNotAllowed);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void RegisterHint_EnforcingPolicy_OutsideRoot_ReturnsPathNotAllowed()
    {
        var root = Directory.CreateTempSubdirectory("native-mcp-reg-");
        try
        {
            var policy = new PathAccessPolicy([root.FullName], enforcing: true);
            var registry = new NativeBinaryRegistry(policy);

            var result = registry.RegisterHint("/opt/evil/payload.so", "deadbeef");

            result.IsError.Should().BeTrue();
            result.Error!.Kind.Should().Be(ErrorKinds.PathNotAllowed);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Load_EnforcingPolicy_InsideRoot_ButMissing_ReturnsBinaryNotFound()
    {
        var root = Directory.CreateTempSubdirectory("native-mcp-reg-");
        try
        {
            var policy = new PathAccessPolicy([root.FullName], enforcing: true);
            var registry = new NativeBinaryRegistry(policy);

            // Path is allowed (under root) but does not exist → not a policy rejection.
            var result = registry.Load(Path.Combine(root.FullName, "missing.so"));

            result.IsError.Should().BeTrue();
            result.Error!.Kind.Should().Be(ErrorKinds.BinaryNotFound);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void RegisterHint_PermissivePolicy_Succeeds()
    {
        var registry = new NativeBinaryRegistry();
        var result = registry.RegisterHint("/any/where/binary.so", "abc");

        result.IsError.Should().BeFalse();
        result.Data.Should().Be(Path.GetFullPath("/any/where/binary.so"));
    }
}
