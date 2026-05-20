using System.Linq;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Server.Tools;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class GetSizeBreakdownToolTests
{
    [Fact]
    public void GetSizeBreakdown_UnknownHandle_ReturnsBinaryNotFound()
    {
        var tool = new NativeTools(new NativeBinaryRegistry());

        var result = tool.GetSizeBreakdown("i:deadbeef:00000000");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.BinaryNotFound);
    }

    [Fact]
    public void GetSizeBreakdown_MissingMstat_ReturnsMstatNotFound()
    {
        var fixturePath = FixturePaths.SampleAot;
        if (fixturePath is null || !File.Exists(fixturePath))
            return;

        var registry = new NativeBinaryRegistry();
        var load = registry.Load(fixturePath);
        load.IsError.Should().BeFalse();

        var tool = new NativeTools(registry);
        var result = tool.GetSizeBreakdown(load.Data!.Handle.Value, mstatPath: "/no/such/file.mstat");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.MstatNotFound);
    }

    [Fact]
    public void GetSizeBreakdown_SampleAot_ReturnsDeterministicAssemblyBreakdown()
    {
        var fixturePath = FixturePaths.SampleAot;
        var mstatPath = FixturePaths.SampleAotMstat;
        if (fixturePath is null || mstatPath is null || !File.Exists(fixturePath) || !File.Exists(mstatPath))
            return;

        var registry = new NativeBinaryRegistry();
        var load = registry.Load(fixturePath);
        load.IsError.Should().BeFalse();

        var tool = new NativeTools(registry);
        var result = tool.GetSizeBreakdown(load.Data!.Handle.Value, groupBy: "assembly", topN: 10);

        result.IsError.Should().BeFalse();
        result.Data.Should().NotBeNull();
        result.Data!.GroupBy.Should().Be("assembly");
        result.Data.Rows.Should().NotBeEmpty();
        result.Data.Rows.Select(row => row.TotalSize).Should().BeInDescendingOrder();
        result.Data.Rows.Should().Contain(row => row.AssemblyName == "System.Private.CoreLib");
        load.Hints.Should().ContainSingle(hint => hint.NextTool == "get_size_breakdown");
        result.Hints.Should().ContainSingle(hint => hint.NextTool == "get_size_breakdown");
    }
}
