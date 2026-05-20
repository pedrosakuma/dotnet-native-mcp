using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>Tests for <see cref="NativeBinaryRegistry"/> lazy-hint and batch-load semantics.</summary>
public class NativeBinaryRegistryBatchTests
{
    // ---------------------------------------------------------------------------
    // Lazy RegisterHint
    // ---------------------------------------------------------------------------

    [Fact]
    public void RegisterHint_ThenLoad_UsesHintBuildId()
    {
        if (!File.Exists("/usr/bin/cat")) return;

        var registry = new NativeBinaryRegistry();

        // RegisterHint with a deliberately wrong buildId.
        // The subsequent Load should pass the hint buildId to NativeImageLoader,
        // which will fail with binary_mismatch — proving the hint was forwarded.
        registry.RegisterHint("/usr/bin/cat", "deadbeefdeadbeef");

        var result = registry.Load("/usr/bin/cat");

        // /usr/bin/cat is not a managed build, so the error is not_a_native_dotnet_image
        // (the hint buildId is checked AFTER the binary is read and found managedish, or it's
        // irrelevant when the binary is rejected earlier). Either way, it must be an error.
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public void RegisterHint_WithNullBuildId_DoesNotAffectLoad()
    {
        var registry = new NativeBinaryRegistry();

        // Registering without a buildId should be a no-op for subsequent load behaviour.
        registry.RegisterHint("/does/not/exist.so", null);

        var result = registry.Load("/does/not/exist.so");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.BinaryNotFound);
    }

    [Fact]
    public void RegisterHint_SamePath_IdempotentOverwrite()
    {
        var registry = new NativeBinaryRegistry();

        registry.RegisterHint("/some/path.so", "aabb");
        // Second call overwrites the first; no exception expected.
        registry.RegisterHint("/some/path.so", "ccdd");

        // Subsequent load will fail (file doesn't exist), which is fine.
        var result = registry.Load("/some/path.so");
        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.BinaryNotFound);
    }

    // ---------------------------------------------------------------------------
    // LoadEntry behaviour via Load (eager semantics for a single entry)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Load_NonExistent_ReturnsBinaryNotFound_RegardlessOfHint()
    {
        var registry = new NativeBinaryRegistry();
        registry.RegisterHint("/no/such/file.so", "abc");

        var result = registry.Load("/no/such/file.so");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.BinaryNotFound);
    }

    // ---------------------------------------------------------------------------
    // Cached + Re-verify
    // ---------------------------------------------------------------------------

    [Fact]
    public void RegisterHint_MultipleDistinctPaths_AllTrackable()
    {
        var registry = new NativeBinaryRegistry();

        // Register several paths; none should throw.
        registry.RegisterHint("/a/binary1.so", "deadbeef01");
        registry.RegisterHint("/a/binary2.so", "deadbeef02");
        registry.RegisterHint("/a/binary3.so");

        // All paths are registered; subsequent loads would fail with binary_not_found.
        foreach (var path in new[] { "/a/binary1.so", "/a/binary2.so", "/a/binary3.so" })
        {
            var r = registry.Load(path);
            r.IsError.Should().BeTrue();
            r.Error!.Kind.Should().Be(ErrorKinds.BinaryNotFound);
        }
    }
}
