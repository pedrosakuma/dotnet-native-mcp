using DotnetNativeMcp.Core.Imaging;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class PeNativeReaderTests
{
    [Fact]
    public void Read_NonPeBytes_ReturnsNull()
    {
        var elf = new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 0, 0, 0, 0 };
        PeNativeReader.Read(new ReadOnlyMemory<byte>(elf), "elf.so").Should().BeNull();
    }

    [Fact]
    public void Read_ManagedTestAssembly_ParsesButRejectedAsNotNativeAot()
    {
        // The test assembly itself is a managed PE — no R2R header, no AOT exports.
        var thisAssembly = typeof(PeNativeReaderTests).Assembly.Location;
        if (!File.Exists(thisAssembly)) return;

        var bytes = File.ReadAllBytes(thisAssembly);
        var image = PeNativeReader.Read(new ReadOnlyMemory<byte>(bytes), thisAssembly);

        // It should parse as PE (it is a valid PE).
        image.Should().NotBeNull();
        image!.Format.Should().Be(BinaryFormat.Pe);

        // But it should not be identified as a managed-native build.
        PeNativeReader.LooksLikeManagedNativeBuild(image, bytes).Should().BeFalse();
    }

    [Fact]
    public void LooksLikeManagedNativeBuild_WithNativeAotExport_ReturnsTrue()
    {
        // Synthesise an image with a known NativeAOT export.
        var sym = new NativeSymbol(0, "RhpNewFast", "RhpNewFast", 0x1000, 0, null, true);
        var handle = Identity.ImageHandle.From("aabb", "aot.dll");
        var image = new NativeImage(handle, "aot.dll", BinaryFormat.Pe,
            Architecture.X64, [], [sym], ReadOnlyMemory<byte>.Empty, 0);

        PeNativeReader.LooksLikeManagedNativeBuild(image, ReadOnlySpan<byte>.Empty).Should().BeTrue();
    }
}
