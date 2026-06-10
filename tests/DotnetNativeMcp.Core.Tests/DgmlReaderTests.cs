using DotnetNativeMcp.Core.Dgml;
using DotnetNativeMcp.Core.Errors;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class DgmlReaderTests
{
    [Fact]
    public void Read_ValidDgml_ReturnsNodesAndEdges()
    {
        var path = WriteScratchFile("valid.dgml", """
            <DirectedGraph xmlns="http://schemas.microsoft.com/vs/2009/dgml">
              <Nodes>
                <Node Id="root" Label="App root" Category="Root" />
                <Node Id="json" Label="Newtonsoft.Json" />
                <Node Id="db" Label="MyDbContext" />
              </Nodes>
              <Links>
                <Link Source="root" Target="json" Label="Uses" />
                <Link Source="json" Target="db" Label="Reflects" />
              </Links>
            </DirectedGraph>
            """);

        try
        {
            var result = DgmlReader.Read(path);

            result.IsError.Should().BeFalse();
            result.Data.Should().NotBeNull();
            result.Data!.Nodes.Should().HaveCount(3);
            result.Data.Edges.Should().HaveCount(2);
            result.Data.Nodes.Should().ContainSingle(node => node.Id == "root" && node.Category == "Root");
            result.Data.Edges.Should().ContainSingle(edge => edge.Source == "json" && edge.Target == "db" && edge.Label == "Reflects");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_LinkWithReason_CapturesReasonAsEdgeLabel()
    {
        // The ILC emits the retention reason on the DGML 'Reason' attribute, not 'Label'.
        var path = WriteScratchFile("reason.dgml", """
            <DirectedGraph xmlns="http://schemas.microsoft.com/vs/2009/dgml">
              <Nodes>
                <Node Id="0" Label="App" />
                <Node Id="1" Label="MyType" />
              </Nodes>
              <Links>
                <Link Source="0" Target="1" Reason="Reflectable type" />
              </Links>
            </DirectedGraph>
            """);

        try
        {
            var result = DgmlReader.Read(path);

            result.IsError.Should().BeFalse();
            result.Data!.Edges.Should().ContainSingle(edge =>
                edge.Source == "0" && edge.Target == "1" && edge.Label == "Reflectable type");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_LinkWithReasonAndLabel_PrefersReason()
    {
        var path = WriteScratchFile("reason-and-label.dgml", """
            <DirectedGraph xmlns="http://schemas.microsoft.com/vs/2009/dgml">
              <Nodes>
                <Node Id="0" Label="App" />
                <Node Id="1" Label="MyType" />
              </Nodes>
              <Links>
                <Link Source="0" Target="1" Reason="call" Label="ignored" />
              </Links>
            </DirectedGraph>
            """);

        try
        {
            var result = DgmlReader.Read(path);

            result.IsError.Should().BeFalse();
            result.Data!.Edges.Should().ContainSingle(edge => edge.Label == "call");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_MalformedXml_ReturnsInternalError()
    {
        var path = WriteScratchFile("malformed.dgml", "<DirectedGraph xmlns=\"http://schemas.microsoft.com/vs/2009/dgml\"><Nodes>");

        try
        {
            var result = DgmlReader.Read(path);

            result.IsError.Should().BeTrue();
            result.Error!.Kind.Should().Be(ErrorKinds.InternalError);
            result.Error.Message.Should().Contain("Malformed DGML");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_NonDgmlXml_ReturnsInternalError()
    {
        var path = WriteScratchFile("not-dgml.xml", "<root><node /></root>");

        try
        {
            var result = DgmlReader.Read(path);

            result.IsError.Should().BeTrue();
            result.Error!.Kind.Should().Be(ErrorKinds.InternalError);
            result.Error.Message.Should().Contain("DirectedGraph");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteScratchFile(string fileName, string content)
    {
        var directory = Path.Combine(Path.GetDirectoryName(typeof(DgmlReaderTests).Assembly.Location)!, "scratch");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}-{fileName}");
        File.WriteAllText(path, content);
        return path;
    }
}
