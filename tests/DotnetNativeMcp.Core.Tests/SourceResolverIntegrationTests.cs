using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// End-to-end integration tests for <see cref="SourceResolver"/> against the real
/// NativeAOT SampleAot fixture binary. Tests skip cleanly when the fixture is not
/// built (toolchain unavailable); on CI the fixture is always present.
/// </summary>
public sealed class SourceResolverIntegrationTests
{
    [Fact]
    public void TrySourceFor_WithRealFixture_ResolvesSymbolToNonNullLocation()
    {
        var binaryPath = FixturePaths.SampleAot;
        if (binaryPath is null)
            return; // AOT toolchain unavailable — skip

        var bytes = File.ReadAllBytes(binaryPath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), binaryPath);
        Assert.NotNull(image);
        Assert.True(image.Symbols.Count > 0, "fixture must expose symbols via .symtab");

        var resolver = new SourceResolver();

        SourceLocation? found = null;
        foreach (var sym in image.Symbols)
        {
            var va = image.ImageBase + sym.Rva;
            var loc = resolver.TrySourceFor(image, va);
            if (loc is not null)
            {
                found = loc;
                break;
            }
        }

        Assert.NotNull(found);
        Assert.NotEmpty(found.File);
        Assert.True(found.StartLine > 0, $"StartLine must be > 0, got {found.StartLine}");
    }

    [Fact]
    public void TrySourceFor_WithRealFixture_SourceLocationFileIsCsFile()
    {
        var binaryPath = FixturePaths.SampleAot;
        if (binaryPath is null)
            return;

        var bytes = File.ReadAllBytes(binaryPath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), binaryPath);
        if (image is null) return;

        var resolver = new SourceResolver();

        // Collect several resolved locations and verify at least one is a .cs file.
        var resolvedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sym in image.Symbols.Take(1000))
        {
            var loc = resolver.TrySourceFor(image, image.ImageBase + sym.Rva);
            if (loc?.File is not null)
                resolvedFiles.Add(loc.File);
        }

        Assert.NotEmpty(resolvedFiles);
        Assert.Contains(resolvedFiles, f =>
            f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TrySourceFor_WithRealFixtureAndPdb_ReturnsSourceLinkUrl()
    {
        var binaryPath = FixturePaths.SampleAot;
        var pdbPath = FixturePaths.SampleAotPdb;
        if (binaryPath is null || pdbPath is null)
            return; // fixture or PDB not built — skip

        var bytes = File.ReadAllBytes(binaryPath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), binaryPath);
        if (image is null) return;

        // SourceResolver loads the PDB automatically by looking for a .pdb sidecar
        // next to the binary. Create a temporary copy of both so the sidecar is found.
        var tempDir = Path.Combine(Path.GetDirectoryName(binaryPath)!, ".sourceresolver_test");
        Directory.CreateDirectory(tempDir);
        var tempBinary = Path.Combine(tempDir, Path.GetFileName(binaryPath));
        var tempPdb = Path.ChangeExtension(tempBinary, ".pdb");
        try
        {
            File.Copy(binaryPath, tempBinary, overwrite: true);
            File.Copy(pdbPath, tempPdb, overwrite: true);

            var tempBytes = File.ReadAllBytes(tempBinary);
            var tempImage = ElfReader.Read(new ReadOnlyMemory<byte>(tempBytes), tempBinary);
            if (tempImage is null) return;

            var resolver = new SourceResolver();

            // Find the first resolved location that has a SourceLink URL.
            string? foundUrl = null;
            foreach (var sym in tempImage.Symbols)
            {
                var loc = resolver.TrySourceFor(tempImage, tempImage.ImageBase + sym.Rva);
                if (loc?.SourceLinkUrl is not null)
                {
                    foundUrl = loc.SourceLinkUrl;
                    break;
                }
            }

            // The PDB embeds SourceLink pointing to raw.githubusercontent.com — we must
            // resolve at least one symbol to a URL or the integration we claim to cover did not run.
            Assert.NotNull(foundUrl);
            Assert.StartsWith("https://", foundUrl, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
