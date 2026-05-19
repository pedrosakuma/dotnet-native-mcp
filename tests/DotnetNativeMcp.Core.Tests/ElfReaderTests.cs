using DotnetNativeMcp.Core.Imaging;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class ElfReaderTests
{
    private static readonly string CatPath = "/usr/bin/cat";

    [Fact]
    public void Read_NullOrShortBytes_ReturnsNull()
    {
        ElfReader.Read(ReadOnlyMemory<byte>.Empty, "empty").Should().BeNull();
        ElfReader.Read(new ReadOnlyMemory<byte>([1, 2, 3]), "short").Should().BeNull();
    }

    [Fact]
    public void Read_NonElf_ReturnsNull()
    {
        // A PE magic (MZ)
        var mz = new byte[] { 0x4D, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        ElfReader.Read(new ReadOnlyMemory<byte>(mz), "mz.dll").Should().BeNull();
    }

    [Fact]
    public void Read_SystemCat_ParsesSuccessfully()
    {
        if (!File.Exists(CatPath)) return;
        var bytes = File.ReadAllBytes(CatPath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), CatPath);

        image.Should().NotBeNull();
        image!.Format.Should().Be(BinaryFormat.Elf);
        image.Architecture.Should().Be(Architecture.X64);
        image.Sections.Should().NotBeEmpty();
        image.Handle.Value.Should().StartWith("i:");
    }

    [Fact]
    public void Read_SystemCat_IsNotManagedNativeBuild()
    {
        if (!File.Exists(CatPath)) return;
        var bytes = File.ReadAllBytes(CatPath);
        var image = ElfReader.Read(new ReadOnlyMemory<byte>(bytes), CatPath);
        image.Should().NotBeNull();

        // /usr/bin/cat is a plain C binary — it has no NativeAOT markers.
        ElfReader.LooksLikeManagedNativeBuild(image!).Should().BeFalse();
    }

    [Fact]
    public void Read_SyntheticMinimalElf_WithNativeAotMarkerInName_IsAccepted()
    {
        // Build a synthetic ELF that passes the symbol-name heuristic.
        // We do this by checking that LooksLikeManagedNativeBuild returns true
        // when the symbol list contains an S_P_ name.
        var sym = new NativeSymbol(0, "S_P_CoreLib_System_String__Equals",
            "System.Private.CoreLib.System.String.Equals", 0x1000, 16, ".text", true);
        var handle = Identity.ImageHandle.From("aabb", "fake.so");
        var image = new NativeImage(handle, "fake.so", BinaryFormat.Elf,
            Architecture.X64, [], [sym], ReadOnlyMemory<byte>.Empty, 0);

        ElfReader.LooksLikeManagedNativeBuild(image).Should().BeTrue();
    }

    [Fact]
    public void Read_SyntheticElf_WithRhMarker_IsAccepted()
    {
        var sym = new NativeSymbol(0, "RhpNewFast", "RhpNewFast", 0x2000, 8, ".text", true);
        var handle = Identity.ImageHandle.From("ccdd", "aot.so");
        var image = new NativeImage(handle, "aot.so", BinaryFormat.Elf,
            Architecture.X64, [], [sym], ReadOnlyMemory<byte>.Empty, 0);

        ElfReader.LooksLikeManagedNativeBuild(image).Should().BeTrue();
    }
}
