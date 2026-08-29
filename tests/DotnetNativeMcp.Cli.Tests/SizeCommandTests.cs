using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Cli.Tests;

public sealed class SizeCommandTests
{
    [Fact]
    public async Task SizeCommand_JsonOutput_ReturnsBreakdownData()
    {
        var fixturePath = RequireSampleAot();
        var mstatPath = RequireSampleAotMstat();

        var result = await CliTestHarness.InvokeAsync("size", fixturePath, "--group-by", "assembly", "--top-n", "5");

        result.ExitCode.Should().Be(0);

        using var document = JsonDocument.Parse(result.StandardOutput);
        document.RootElement.GetProperty("summary").GetString().Should().Contain("assembly size bucket");
        var data = document.RootElement.GetProperty("data");
        data.GetProperty("groupBy").GetString().Should().Be("assembly");
        data.GetProperty("mstatPath").GetString().Should().Be(Path.GetFullPath(mstatPath));
        data.GetProperty("rows").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SizeCommand_TableOutput_RendersCategoryTotalsAndBreakdown()
    {
        var fixturePath = RequireSampleAot();

        var result = await CliTestHarness.InvokeAsync("size", fixturePath, "--output", "table", "--group-by", "category");

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("category totals");
        result.StandardOutput.Should().Contain("self-size");
        result.StandardOutput.Should().Contain("category breakdown");
        result.StandardOutput.Should().Contain("blob");
    }

    [Fact]
    public async Task SizeCommand_MissingMstat_ReturnsMstatNotFound()
    {
        var fixturePath = RequireSampleAot();

        var result = await CliTestHarness.InvokeAsync("size", fixturePath, "--mstat-path", "/no/such/file.mstat");

        result.ExitCode.Should().Be(1);

        using var document = JsonDocument.Parse(result.StandardOutput);
        document.RootElement.GetProperty("error").GetProperty("kind").GetString().Should().Be("mstat_not_found");
    }

    private static string RequireSampleAot()
    {
        CliFixturePaths.SampleAot.Should().NotBeNull();
        return CliFixturePaths.SampleAot!;
    }

    private static string RequireSampleAotMstat()
    {
        CliFixturePaths.SampleAotMstat.Should().NotBeNull();
        return CliFixturePaths.SampleAotMstat!;
    }
}
