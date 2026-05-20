using DotnetNativeMcp.Core.Symbols;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public sealed class SourceLinkResolverTests
{
    [Fact]
    public void TryLoad_WhenFileDoesNotExist_ReturnsNull()
    {
        Assert.Null(SourceLinkResolver.TryLoad("/nonexistent/path/foo.pdb"));
    }

    [Fact]
    public void TryLoad_WhenPathIsNull_ReturnsNull()
    {
        Assert.Null(SourceLinkResolver.TryLoad(null));
    }

    [Fact]
    public void TryLoadFromBytes_WithNonPdbBytes_ReturnsNull()
    {
        var notPdb = new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01 }; // ELF magic
        Assert.Null(SourceLinkResolver.TryLoadFromBytes(notPdb));
    }

    [Fact]
    public void TryLoad_WithFixturePdb_SucceedsOrReturnsNullGracefully()
    {
        var fixtureDir = Path.GetDirectoryName(typeof(SourceLinkResolverTests).Assembly.Location)!;
        var pdbPath = Path.Combine(fixtureDir, "fixtures", "SampleAot", "SampleAot.pdb");
        if (!File.Exists(pdbPath))
            return; // fixture PDB not copied — skip

        // Should not throw; may return null if the PDB has no SourceLink data.
        var resolver = SourceLinkResolver.TryLoad(pdbPath);
        if (resolver is not null)
        {
            // If SourceLink data was found, resolving a known path should not throw.
            var url = resolver.ResolveUrl("/home/pedrotravi/dotnet-native-mcp/src/Program.cs");
            // url may be null or a valid string — no exception expected.
        }
    }
}
