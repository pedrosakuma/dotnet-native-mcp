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
