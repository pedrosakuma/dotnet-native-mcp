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
    public void TryLoad_WithFixturePdb_LoadsAndResolvesSourceLinkUrl()
    {
        var pdbPath = FixturePaths.SampleAotPdb;
        if (pdbPath is null)
            return; // fixture PDB not built — skip (AOT toolchain unavailable)

        // PDB must be a valid portable PDB (BSJB magic).
        var pdbBytes = File.ReadAllBytes(pdbPath);
        Assert.True(pdbBytes.Length >= 4);
        Assert.Equal(0x424A5342u, BitConverter.ToUInt32(pdbBytes, 0));

        // SourceLinkResolver must load without throwing.
        var resolver = SourceLinkResolver.TryLoad(pdbPath);

        // The fixture is built with Microsoft.SourceLink.GitHub, so SourceLink JSON is embedded.
        // The resolver must not be null.
        Assert.NotNull(resolver);

        // Resolving the fixture source file must yield a raw.githubusercontent.com URL.
        var assemblyDir = Path.GetDirectoryName(typeof(SourceLinkResolverTests).Assembly.Location)!;
        var fixtureSource = new[]
        {
            Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "..", "tests", "fixtures", "SampleAot", "Program.cs")),
            Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "..", "..", "tests", "fixtures", "SampleAot", "Program.cs")),
            Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "..", "..", "..", "tests", "fixtures", "SampleAot", "Program.cs")),
        }.FirstOrDefault(File.Exists);
        Assert.NotNull(fixtureSource);

        var url = resolver.ResolveUrl(fixtureSource);
        if (url is not null)
        {
            Assert.StartsWith("https://", url, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            // The path heuristic failed (e.g. CI uses deterministic /_/ path normalization
            // when ContinuousIntegrationBuild=true, or the build machine path differs).
            // Probe a few well-known SourceLink prefixes to confirm the resolver loaded
            // at least one usable mapping.
            string? repoUrl = null;
            foreach (var candidate in new[]
            {
                "/_/tests/fixtures/SampleAot/Program.cs",
                "/home/pedrotravi/dotnet-native-mcp/tests/fixtures/SampleAot/Program.cs",
            })
            {
                repoUrl = resolver.ResolveUrl(candidate);
                if (repoUrl is not null) break;
            }
            Assert.NotNull(repoUrl);
            Assert.StartsWith("https://raw.githubusercontent.com/", repoUrl, StringComparison.OrdinalIgnoreCase);
        }
    }
}
