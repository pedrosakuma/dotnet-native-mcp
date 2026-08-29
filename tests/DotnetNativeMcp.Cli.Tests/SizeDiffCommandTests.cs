using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Cli.Tests;

public sealed class SizeDiffCommandTests
{
    [Fact]
    public async Task SizeDiffCommand_JsonOutput_ReturnsZeroDeltaForSameBinary()
    {
        var fixturePath = RequireSampleAot();

        var result = await CliTestHarness.InvokeAsync("size-diff", fixturePath, fixturePath);

        result.ExitCode.Should().Be(0);

        using var document = JsonDocument.Parse(result.StandardOutput);
        var data = document.RootElement.GetProperty("data");
        data.GetProperty("groupBy").GetString().Should().Be("category");
        data.GetProperty("totalSizeDelta").GetInt64().Should().Be(0);
        data.GetProperty("topGrew").GetArrayLength().Should().Be(0);
        data.GetProperty("topShrank").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task SizeDiffCommand_TableOutput_RendersDiffHeadersWhenNoBucketsChanged()
    {
        var fixturePath = RequireSampleAot();

        var result = await CliTestHarness.InvokeAsync("size-diff", fixturePath, fixturePath, "--output", "table");

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("grew");
        result.StandardOutput.Should().Contain("category");
        result.StandardOutput.Should().Contain("baseline");
        result.StandardOutput.Should().Contain("candidate");
        result.StandardOutput.Should().Contain("(none)");
    }

    [Fact]
    public async Task SizeDiffCommand_NegativeFailOnIncreaseBytes_ReturnsInvalidArgument()
    {
        var fixturePath = RequireSampleAot();

        var result = await CliTestHarness.InvokeAsync(
            "size-diff",
            fixturePath,
            fixturePath,
            "--current-mstat-path",
            RequireSampleAotMstat(),
            "--fail-on-increase-bytes",
            "-1");

        result.ExitCode.Should().Be(1);

        using var document = JsonDocument.Parse(result.StandardOutput);
        document.RootElement.GetProperty("error").GetProperty("kind").GetString().Should().Be("invalid_argument");
    }

    [Fact]
    public async Task SizeDiffCommand_MissingCurrentMstat_ReturnsMstatNotFound()
    {
        var fixturePath = RequireSampleAot();

        var result = await CliTestHarness.InvokeAsync(
            "size-diff",
            fixturePath,
            fixturePath,
            "--current-mstat-path",
            "/no/such/file.mstat");

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
