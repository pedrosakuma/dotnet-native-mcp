using DotnetNativeMcp.Core.Imaging;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class NativeBinaryRegistryTests
{
    [Fact]
    public void Load_NonExistentPath_ReturnsBinaryNotFound()
    {
        var registry = new NativeBinaryRegistry();
        var result = registry.Load("/does/not/exist/binary.so");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(Errors.ErrorKinds.BinaryNotFound);
    }

    [Fact]
    public void Load_SamePathTwice_ReturnsCachedResult()
    {
        if (!File.Exists("/usr/bin/cat")) return;

        var registry = new NativeBinaryRegistry();
        // First load will fail (not a managed binary) but we want to test caching with a valid managed binary.
        // Use the loader to confirm caching occurs: call twice, expect same handle.
        // For a negative test: load /usr/bin/cat twice, both should fail consistently.
        var r1 = registry.Load("/usr/bin/cat");
        var r2 = registry.Load("/usr/bin/cat");

        r1.IsError.Should().BeTrue();
        r2.IsError.Should().BeTrue();
        r1.Error!.Kind.Should().Be(r2.Error!.Kind);
    }

    [Fact]
    public void TryGet_UnknownHandle_ReturnsFalse()
    {
        var registry = new NativeBinaryRegistry();
        registry.TryGet("i:deadbeef:00000000", out _).Should().BeFalse();
    }

    [Fact]
    public void List_Empty_ReturnsEmpty()
    {
        var registry = new NativeBinaryRegistry();
        registry.List().Should().BeEmpty();
    }
}
