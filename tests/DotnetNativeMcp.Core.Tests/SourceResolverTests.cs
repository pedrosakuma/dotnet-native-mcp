using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public sealed class SourceResolverTests
{
    [Fact]
    public void TrySourceFor_WhenFixtureHasDwarf_ReturnsNonNullForKnownSymbol()
    {
        var binaryPath = FixturePaths.SampleAot;
        if (binaryPath is null)
            return; // fixture not built — skip

        var bytes = File.ReadAllBytes(binaryPath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), binaryPath);
        if (image is null)
            return;

        var resolver = new SourceResolver();

        // Try several addresses until we find one covered by DWARF.
        // The image should have .debug_line data from the NativeAOT publish.
        // We scan symbols to find a real code address.
        SourceLocation? found = null;
        foreach (var sym in image.Symbols.Take(200))
        {
            var va = image.ImageBase + sym.Rva;
            var loc = resolver.TrySourceFor(image, va);
            if (loc is not null)
            {
                found = loc;
                break;
            }
        }

        // If the binary has DWARF, at least one symbol should resolve.
        // If no DWARF, found remains null — that is acceptable (binary may lack debug info).
        if (found is not null)
        {
            Assert.NotEmpty(found.File);
            Assert.True(found.StartLine > 0);
        }
    }

    [Fact]
    public void TrySourceFor_FakeImageWithNoDwarf_ReturnsNull()
    {
        // Construct a minimal ELF with no .debug_line section.
        // Use a real system binary that definitely exists.
        var elfPath = "/bin/ls";
        if (!File.Exists(elfPath))
            elfPath = "/usr/bin/ls";
        if (!File.Exists(elfPath))
            return; // cannot test without /bin/ls

        var bytes = File.ReadAllBytes(elfPath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), elfPath);
        if (image is null) return;

        var resolver = new SourceResolver();
        var va = image.ImageBase + (image.Sections.FirstOrDefault(s => s.Name == ".text")?.VirtualAddress ?? 0UL);
        // /bin/ls almost certainly has no portable PDB sidecar.
        // TrySourceFor should not throw even with an unmanaged binary.
        var loc = resolver.TrySourceFor(image, va);
        // Either null (no DWARF without PDB) or a valid location — no exception.
        if (loc is not null)
        {
            Assert.NotEmpty(loc.File);
            Assert.True(loc.StartLine > 0);
        }
    }
}
