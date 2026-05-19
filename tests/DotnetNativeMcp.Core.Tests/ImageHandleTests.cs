using DotnetNativeMcp.Core.Identity;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class ImageHandleTests
{
    [Fact]
    public void From_ProducesCorrectFormat()
    {
        var handle = ImageHandle.From("deadbeef", "MyApp");
        handle.Value.Should().StartWith("i:");
        handle.Value.Should().Contain(":");
        handle.BuildIdHex.Should().Be("deadbeef");
        handle.NameHash.Should().HaveLength(8); // CRC32 = 4 bytes = 8 hex chars
    }

    [Fact]
    public void TryParse_ValidHandle_Succeeds()
    {
        var handle = ImageHandle.From("cafebabe", "foo.so");
        var parsed = ImageHandle.TryParse(handle.Value);
        parsed.Should().NotBeNull();
        parsed!.BuildIdHex.Should().Be("cafebabe");
        parsed.NameHash.Should().Be(handle.NameHash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("notahandle")]
    [InlineData("i:")]
    [InlineData("i:nocolon")]
    public void TryParse_Invalid_ReturnsNull(string? value)
    {
        ImageHandle.TryParse(value).Should().BeNull();
    }

    [Fact]
    public void Parse_Invalid_Throws()
    {
        var act = () => ImageHandle.Parse("bad");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var handle = ImageHandle.From("aabb", "test");
        handle.ToString().Should().Be(handle.Value);
    }

    [Fact]
    public void From_SameInput_SameNameHash()
    {
        var h1 = ImageHandle.From("aabb", "MyBinary.so");
        var h2 = ImageHandle.From("aabb", "MyBinary.so");
        h1.NameHash.Should().Be(h2.NameHash);
    }
}
