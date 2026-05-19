using System.Text.Json;
using DotnetNativeMcp.Core;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public sealed class CompareNativeBinariesTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Compare_fixture_variants_reports_growth_hotspots()
    {
        var baseline = LoadFixture("sampleaot-default.json");
        var target = LoadFixture("sampleaot-trim-full.json");

        var baselineHandle = NativeImageLoader.RegisterLoadedImage("SampleAot-default", baseline.Symbols, baseline.Sections);
        var targetHandle = NativeImageLoader.RegisterLoadedImage("SampleAot-trim-full", target.Symbols, target.Sections);

        var diff = NativeImageLoader.CompareNativeBinaries(baselineHandle, targetHandle);

        diff.GrowthHotspots.Should().NotBeEmpty();
        diff.Verdict.Should().Be("mixed");
    }

    [Fact]
    public void Compare_same_handle_returns_empty_diff()
    {
        var fixture = LoadFixture("sampleaot-default.json");
        var handle = NativeImageLoader.RegisterLoadedImage("SampleAot-default-same", fixture.Symbols, fixture.Sections);

        var diff = NativeImageLoader.CompareNativeBinaries(handle, handle);

        diff.Verdict.Should().Be("no_change");
        diff.AddedSymbols.Should().BeEmpty();
        diff.RemovedSymbols.Should().BeEmpty();
        diff.GrowthHotspots.Should().BeEmpty();
        diff.ShrunkSymbols.Should().BeEmpty();
        diff.SectionSizeDelta.Should().BeEmpty();
    }

    [Fact]
    public void Compare_sets_expected_verdict_for_grew_and_shrank()
    {
        var baselineHandle = NativeImageLoader.RegisterLoadedImage(
            "baseline",
            [new NativeSymbol("A", 100), new NativeSymbol("B", 80)],
            new Dictionary<string, long>(StringComparer.Ordinal) { [".text"] = 200 });
        var grewHandle = NativeImageLoader.RegisterLoadedImage(
            "grew",
            [new NativeSymbol("A", 120), new NativeSymbol("B", 84)],
            new Dictionary<string, long>(StringComparer.Ordinal) { [".text"] = 240 });
        var shrankHandle = NativeImageLoader.RegisterLoadedImage(
            "shrank",
            [new NativeSymbol("A", 90), new NativeSymbol("B", 60)],
            new Dictionary<string, long>(StringComparer.Ordinal) { [".text"] = 180 });

        NativeImageLoader.CompareNativeBinaries(baselineHandle, grewHandle).Verdict.Should().Be("grew");
        NativeImageLoader.CompareNativeBinaries(baselineHandle, shrankHandle).Verdict.Should().Be("shrank");
    }

    private static FixtureModel LoadFixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "SampleAot", fileName);
        var json = File.ReadAllText(path);
        var fixture = JsonSerializer.Deserialize<FixtureModel>(json, JsonOptions);
        return fixture ?? throw new InvalidOperationException($"Unable to deserialize fixture '{path}'.");
    }

    private sealed record FixtureModel(
        IReadOnlyList<NativeSymbol> Symbols,
        IReadOnlyDictionary<string, long> Sections);
}
