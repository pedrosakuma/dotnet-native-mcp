using DotnetNativeMcp.Core.Errors;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Regression tests for <see cref="SanitisedError"/>: ensures error details returned to
/// MCP clients never carry <c>Exception.ToString()</c> stack traces or absolute filesystem
/// paths.
/// </summary>
public sealed class SanitisedErrorTests
{
    [Fact]
    public void From_NoStackFrameMarkers_InDetail()
    {
        var ex = CaptureException(static () => throw new InvalidDataException("boom"));

        var detail = SanitisedError.From(ex);

        detail.Should().NotContain("   at ");
        detail.Should().NotContain(typeof(SanitisedErrorTests).FullName!);
        detail.Should().Contain(nameof(InvalidDataException));
    }

    [Fact]
    public void From_StripsAbsolutePath_FromMessage()
    {
        var path = Path.Combine(Path.GetTempPath(), "secret-dir", "file.bin");
        var ex = new InvalidDataException($"Failed parsing '{path}' at offset 42.");

        var detail = SanitisedError.From(ex, path);

        detail.Should().NotContain(path);
        detail.Should().Contain("file.bin");
        detail.Should().Contain("offset 42");
    }

    [Fact]
    public void From_NullSensitivePaths_StillSanitisesStackTrace()
    {
        var ex = CaptureException(static () => throw new InvalidOperationException("bad state"));

        var detail = SanitisedError.From(ex);

        detail.Should().NotContain("   at ");
        detail.Should().StartWith(nameof(InvalidOperationException));
    }

    [Fact]
    public void SanitiseMessage_NullInput_ReturnsNull()
    {
        SanitisedError.SanitiseMessage(null).Should().BeNull();
        SanitisedError.SanitiseMessage("   ").Should().BeNull();
    }

    [Fact]
    public void Sink_Invoked_WithFullExceptionDetails()
    {
        var captured = string.Empty;
        var previous = SanitisedError.Sink;
        SanitisedError.Sink = s => captured = s;
        try
        {
            var ex = CaptureException(static () => throw new InvalidDataException("sentinel-12345"));

            SanitisedError.From(ex);

            captured.Should().Contain("sentinel-12345");
            captured.Should().Contain("   at ");
        }
        finally
        {
            SanitisedError.Sink = previous;
        }
    }

    private static Exception CaptureException(Action throwing)
    {
        try
        {
            throwing();
            throw new InvalidOperationException("Expected the delegate to throw.");
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
