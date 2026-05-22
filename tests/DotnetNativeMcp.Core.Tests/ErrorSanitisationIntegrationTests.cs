using DotnetNativeMcp.Core.Dgml;
using DotnetNativeMcp.Core.Mstat;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// End-to-end assertions that <see cref="DgmlReader"/> and <see cref="MstatReader"/>
/// surface no stack-frame markers and no absolute filesystem paths on parse failures.
/// </summary>
public sealed class ErrorSanitisationIntegrationTests : IDisposable
{
    private readonly string _root;

    public ErrorSanitisationIntegrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dnm-error-sanit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void DgmlReader_MalformedXml_DoesNotLeakPathOrStack()
    {
        var path = Path.Combine(_root, "broken.dgml");
        File.WriteAllText(path, "<DirectedGraph><Nodes><Node Id='a'/></Nodes><Edges><Edge Source='a' Target='b'");

        var result = DgmlReader.Read(path);

        result.IsError.Should().BeTrue();
        var detail = result.Error!.Detail ?? string.Empty;
        var message = result.Error.Message;

        detail.Should().NotContain("   at ");
        detail.Should().NotContain(path);
        message.Should().NotContain(path);
        message.Should().Contain("broken.dgml");
    }

    [Fact]
    public void MstatReader_InvalidImage_DoesNotLeakPathOrStack()
    {
        var path = Path.Combine(_root, "broken.mstat");
        File.WriteAllBytes(path, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 });

        var result = MstatReader.Read(path);

        result.IsError.Should().BeTrue();
        var detail = result.Error!.Detail ?? string.Empty;
        var message = result.Error.Message;

        detail.Should().NotContain("   at ");
        detail.Should().NotContain(path);
        message.Should().NotContain(path);
        message.Should().Contain("broken.mstat");
    }

    [Fact]
    public void DgmlReader_MissingFile_DoesNotLeakAbsolutePath()
    {
        var path = Path.Combine(_root, "missing.dgml");

        var result = DgmlReader.Read(path);

        result.IsError.Should().BeTrue();
        result.Error!.Message.Should().NotContain(path);
        result.Error.Message.Should().Contain("missing.dgml");
    }

    [Fact]
    public void MstatReader_MissingFile_DoesNotLeakAbsolutePath()
    {
        var path = Path.Combine(_root, "missing.mstat");

        var result = MstatReader.Read(path);

        result.IsError.Should().BeTrue();
        result.Error!.Message.Should().NotContain(path);
        result.Error.Message.Should().Contain("missing.mstat");
    }
}
