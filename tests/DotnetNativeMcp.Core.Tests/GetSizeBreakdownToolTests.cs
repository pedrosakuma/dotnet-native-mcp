using System.Linq;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using DotnetNativeMcp.Server.Tools;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class GetSizeBreakdownToolTests
{
    [Fact]
    public void GetSizeBreakdown_UnknownHandle_ReturnsBinaryNotFound()
    {
        var tool = new NativeTools(new NativeBinaryRegistry(), new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());

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

        var tool = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());
        var result = tool.GetSizeBreakdown(load.Data!.Handle.Value, mstatPath: "/no/such/file.mstat");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.MstatNotFound);
    }

    [Fact]
    public void GetSizeBreakdown_CorruptMstatPresent_ReturnsMstatInvalid()
    {
        var fixturePath = FixturePaths.SampleAot;
        if (fixturePath is null || !File.Exists(fixturePath))
            return;

        var registry = new NativeBinaryRegistry();
        var load = registry.Load(fixturePath);
        load.IsError.Should().BeFalse();

        var scratchDir = Path.Combine(
            Path.GetDirectoryName(typeof(GetSizeBreakdownToolTests).Assembly.Location)!,
            "scratch");
        Directory.CreateDirectory(scratchDir);
        var corruptPath = Path.Combine(scratchDir, $"{Guid.NewGuid():N}-corrupt.mstat");
        File.WriteAllBytes(corruptPath, [0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02, 0x03,
                                         0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B]);
        try
        {
            var tool = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());
            var result = tool.GetSizeBreakdown(load.Data!.Handle.Value, mstatPath: corruptPath);

            result.IsError.Should().BeTrue();
            result.Error!.Kind.Should().Be(ErrorKinds.MstatInvalid);
        }
        finally
        {
            File.Delete(corruptPath);
        }
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

        var tool = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());
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

    [Fact]
    public void GetSizeBreakdown_SampleAot_GroupByCategory_ReturnsCategorySummary()
    {
        var fixturePath = FixturePaths.SampleAot;
        var mstatPath = FixturePaths.SampleAotMstat;
        if (fixturePath is null || mstatPath is null || !File.Exists(fixturePath) || !File.Exists(mstatPath))
            return;

        var registry = new NativeBinaryRegistry();
        var load = registry.Load(fixturePath);
        load.IsError.Should().BeFalse();

        var tool = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());
        var result = tool.GetSizeBreakdown(load.Data!.Handle.Value, groupBy: "category", topN: 10);

        result.IsError.Should().BeFalse();
        result.Data!.GroupBy.Should().Be("category");
        result.Data.FormatVersion.Should().MatchRegex(@"^\d+\.\d+$");
        result.Data.CategoryTotals.Should().NotBeEmpty();
        result.Data.CategoryTotals.Sum(c => c.TotalSize).Should().Be(result.Data.TotalAttributedBytes);
        result.Data.Rows.Should().NotBeEmpty();
        result.Data.Rows.Select(row => row.TotalSize).Should().BeInDescendingOrder();
    }

    [Fact]
    public void GetSizeBreakdown_InvalidGroupBy_ReturnsInvalidArgument()
    {
        var fixturePath = FixturePaths.SampleAot;
        if (fixturePath is null || !File.Exists(fixturePath))
            return;

        var registry = new NativeBinaryRegistry();
        var load = registry.Load(fixturePath);
        load.IsError.Should().BeFalse();

        var tool = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());
        var result = tool.GetSizeBreakdown(load.Data!.Handle.Value, groupBy: "nonsense");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }
}
