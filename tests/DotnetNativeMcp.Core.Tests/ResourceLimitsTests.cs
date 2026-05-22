using System.Text;
using DotnetNativeMcp.Core.Disassembly;
using DotnetNativeMcp.Core.Dgml;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Strings;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class ResourceLimitsTests
{
    [Fact]
    public void SafeReadAllBytes_FileExceedsCap_ReturnsFileTooLarge()
    {
        var path = WriteScratchFile("too-large.bin", new byte[] { 1, 2, 3, 4, 5 });

        try
        {
            var result = ResourceLimits.SafeReadAllBytes(path, maxBytes: 4);

            result.IsError.Should().BeTrue();
            result.Error!.Kind.Should().Be(ErrorKinds.FileTooLarge);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SafeReadAllBytes_FileUnderCap_ReturnsBytes()
    {
        var expected = new byte[] { 1, 2, 3, 4 };
        var path = WriteScratchFile("within-cap.bin", expected);

        try
        {
            var result = ResourceLimits.SafeReadAllBytes(path, maxBytes: 4);

            result.IsError.Should().BeFalse();
            result.Data.Should().Equal(expected);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void JitIlMap_Load_FileExceedsCap_ReturnsFileTooLarge()
    {
        var path = WriteScratchFile("large.ilmap", Encoding.UTF8.GetBytes("0\t0\n"));

        try
        {
            var result = JitIlMap.Load(path, maxBytes: 3, maxEntries: ResourceLimits.MaxIlMapEntries);

            result.IsError.Should().BeTrue();
            result.Error!.Kind.Should().Be(ErrorKinds.FileTooLarge);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DgmlReader_Read_FileExceedsCap_ReturnsFileTooLarge()
    {
        var path = WriteScratchFile(
            "large.dgml",
            Encoding.UTF8.GetBytes("<DirectedGraph xmlns=\"http://schemas.microsoft.com/vs/2009/dgml\"><Nodes /></DirectedGraph>"));

        try
        {
            var result = DgmlReader.Read(path, maxBytes: 16, maxNodes: ResourceLimits.MaxDgmlNodes, maxEdges: ResourceLimits.MaxDgmlEdges);

            result.IsError.Should().BeTrue();
            result.Error!.Kind.Should().Be(ErrorKinds.FileTooLarge);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StringExtractor_StopsAtConfiguredMatchCap()
    {
        var bytes = Encoding.ASCII.GetBytes("aa\0bb\0cc\0");

        var results = StringExtractor.Extract(bytes, 0x1000, ".rodata", 2, ascii: true, utf16: false, out var truncated, maxMatches: 2);

        results.Select(result => result.Value).Should().Equal("aa", "bb");
        truncated.Should().BeTrue();
    }

    [Fact]
    public void StringExtractor_TruncatesOversizedMatchValue()
    {
        var bytes = Encoding.ASCII.GetBytes(new string('a', ResourceLimits.MaxExtractedStringChars + 32));

        var results = StringExtractor.Extract(bytes, 0x1000, ".rodata", 4, ascii: true, utf16: false, out var truncated);

        results.Should().ContainSingle();
        results[0].Length.Should().Be(ResourceLimits.MaxExtractedStringChars + 32);
        results[0].Value.Length.Should().Be(ResourceLimits.MaxExtractedStringChars);
        results[0].Value.Should().EndWith("…");
        truncated.Should().BeTrue();
    }

    private static string WriteScratchFile(string fileName, byte[] content)
    {
        var directory = Path.Combine(Path.GetDirectoryName(typeof(ResourceLimitsTests).Assembly.Location)!, "scratch");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}-{fileName}");
        File.WriteAllBytes(path, content);
        return path;
    }
}
