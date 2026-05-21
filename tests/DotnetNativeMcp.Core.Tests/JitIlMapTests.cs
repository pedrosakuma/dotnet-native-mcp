using DotnetNativeMcp.Core.Disassembly;
using DotnetNativeMcp.Core.Errors;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class JitIlMapTests
{
    [Fact]
    public void Parse_UnsortedEntries_SortsAndResolvesRanges()
    {
        var result = JitIlMap.Parse("5\t4\n0\tprolog\n2\t0\n", "test.ilmap");

        result.IsError.Should().BeFalse();
        result.Error.Should().BeNull();

        var map = result.Data!;
        map.Entries.Select(entry => entry.NativeOffset).Should().Equal(0UL, 2UL, 5UL);
        map.FindIlOffset(0).Should().Be("prolog");
        map.FindIlOffset(1).Should().Be("prolog");
        map.FindIlOffset(2).Should().Be("0");
        map.FindIlOffset(4).Should().Be("0");
        map.FindIlOffset(5).Should().Be("4");
        map.FindIlOffset(9).Should().Be("4");
    }

    [Fact]
    public void Parse_CommentsBlankLinesAndSentinels_AreAccepted()
    {
        var result = JitIlMap.Parse("# comment\n\n0\tprolog\n3\tepilog\n7\tnoinfo\na\t0010\n", "test.ilmap");

        result.IsError.Should().BeFalse();
        result.Error.Should().BeNull();

        var map = result.Data!;
        map.FindIlOffset(0).Should().Be("prolog");
        map.FindIlOffset(4).Should().Be("epilog");
        map.FindIlOffset(8).Should().Be("noinfo");
        map.FindIlOffset(10).Should().Be("10");
    }

    [Fact]
    public void Parse_MalformedLineWithoutSingleTab_ReturnsInvalidArgument()
    {
        var result = JitIlMap.Parse("0 0", "broken.ilmap");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
        result.Error.Message.Should().Contain("Malformed IL map line 1");
    }

    [Fact]
    public void Parse_MalformedIlOffset_ReturnsInvalidArgument()
    {
        var result = JitIlMap.Parse("0\tbogus", "broken.ilmap");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
        result.Error.Message.Should().Contain("Malformed IL offset");
    }
}
