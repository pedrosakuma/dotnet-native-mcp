using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using DotnetNativeMcp.Server.Tools;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class CompareNativeBinariesMstatDiffTests
{
    private static NativeTools NewTools(NativeBinaryRegistry registry) =>
        new(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());

    [Fact]
    public void Compare_SameImageWithMstat_ReportsZeroSizeDiff()
    {
        var fixturePath = FixturePaths.SampleAot;
        var mstatPath = FixturePaths.SampleAotMstat;
        if (fixturePath is null || mstatPath is null || !File.Exists(fixturePath) || !File.Exists(mstatPath))
            return;

        var registry = new NativeBinaryRegistry();
        var baseline = registry.Load(fixturePath);
        var current = registry.Load(fixturePath);
        baseline.IsError.Should().BeFalse();
        current.IsError.Should().BeFalse();

        var tool = NewTools(registry);
        var result = tool.CompareNativeBinaries(baseline.Data!.Handle.Value, current.Data!.Handle.Value);

        result.IsError.Should().BeFalse();
        result.Data!.MstatSizeDiff.Should().NotBeNull();
        result.Data.MstatSizeDiffNote.Should().BeNull();

        var diff = result.Data.MstatSizeDiff!;
        diff.GroupBy.Should().Be("category");
        diff.BaselineTotalSize.Should().BeGreaterThan(0);
        diff.BaselineTotalSize.Should().Be(diff.CurrentTotalSize);
        diff.TotalSizeDelta.Should().Be(0);
        diff.AddedBucketCount.Should().Be(0);
        diff.RemovedBucketCount.Should().Be(0);
        diff.ChangedBucketCount.Should().Be(0);
        diff.TopGrew.Should().BeEmpty();
        diff.TopShrank.Should().BeEmpty();
    }

    [Fact]
    public void Compare_ExplicitGroupBy_PropagatesToDiff()
    {
        var fixturePath = FixturePaths.SampleAot;
        var mstatPath = FixturePaths.SampleAotMstat;
        if (fixturePath is null || mstatPath is null || !File.Exists(fixturePath) || !File.Exists(mstatPath))
            return;

        var registry = new NativeBinaryRegistry();
        var baseline = registry.Load(fixturePath);
        var current = registry.Load(fixturePath);

        var tool = NewTools(registry);
        var result = tool.CompareNativeBinaries(
            baseline.Data!.Handle.Value, current.Data!.Handle.Value, mstatGroupBy: "assembly");

        result.IsError.Should().BeFalse();
        result.Data!.MstatSizeDiff.Should().NotBeNull();
        result.Data.MstatSizeDiff!.GroupBy.Should().Be("assembly");
    }

    [Fact]
    public void Compare_CurrentMstatMissing_ReturnsNullDiffWithNote()
    {
        var fixturePath = FixturePaths.SampleAot;
        var mstatPath = FixturePaths.SampleAotMstat;
        if (fixturePath is null || mstatPath is null || !File.Exists(fixturePath) || !File.Exists(mstatPath))
            return;

        var registry = new NativeBinaryRegistry();
        var baseline = registry.Load(fixturePath);
        var current = registry.Load(fixturePath);

        var tool = NewTools(registry);
        var result = tool.CompareNativeBinaries(
            baseline.Data!.Handle.Value,
            current.Data!.Handle.Value,
            currentMstatPath: "/no/such/missing.mstat");

        result.IsError.Should().BeFalse();
        result.Data!.MstatSizeDiff.Should().BeNull();
        result.Data.MstatSizeDiffNote.Should().NotBeNullOrEmpty();
        result.Data.MstatSizeDiffNote.Should().Contain("current");
    }

    [Fact]
    public void Compare_InvalidMstatGroupBy_ReturnsInvalidArgument()
    {
        var fixturePath = FixturePaths.SampleAot;
        if (fixturePath is null || !File.Exists(fixturePath))
            return;

        var registry = new NativeBinaryRegistry();
        var baseline = registry.Load(fixturePath);
        var current = registry.Load(fixturePath);

        var tool = NewTools(registry);
        var result = tool.CompareNativeBinaries(
            baseline.Data!.Handle.Value, current.Data!.Handle.Value, mstatGroupBy: "bogus");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Compare_NullOrEmptyMstatGroupBy_ReturnsInvalidArgument_DoesNotThrow(string? groupBy)
    {
        var fixturePath = FixturePaths.SampleAot;
        if (fixturePath is null || !File.Exists(fixturePath))
            return;

        var registry = new NativeBinaryRegistry();
        var baseline = registry.Load(fixturePath);
        var current = registry.Load(fixturePath);

        var tool = NewTools(registry);
        var result = tool.CompareNativeBinaries(
            baseline.Data!.Handle.Value, current.Data!.Handle.Value, mstatGroupBy: groupBy!);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }
}
