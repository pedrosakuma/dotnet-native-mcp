using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Cli.Tests;

public sealed class RetentionCommandTests
{
    [Fact]
    public async Task RetentionCommand_JsonOutput_ReturnsPath()
    {
        var sampleAotPath = CliTestHarness.SampleAot;
        if (sampleAotPath is null)
            return;

        var result = await CliTestHarness.InvokeAsync(
            "retention",
            sampleAotPath,
            "--target",
            "Program",
            "--max-depth",
            "12",
            "--output",
            "json");

        result.ExitCode.Should().Be(0);

        using var document = JsonDocument.Parse(result.StandardOutput);
        var path = document.RootElement.GetProperty("data").GetProperty("path");
        path.GetArrayLength().Should().BeGreaterThan(0);
        path[0].GetProperty("edgeLabelFromPrevious").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task RetentionCommand_TableOutput_RendersStepReasonAndSymbolColumns()
    {
        var sampleAotPath = CliTestHarness.SampleAot;
        if (sampleAotPath is null)
            return;

        var result = await CliTestHarness.InvokeAsync(
            "retention",
            sampleAotPath,
            "--target",
            "Program",
            "--max-depth",
            "12",
            "--output",
            "table");

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("Step");
        result.StandardOutput.Should().Contain("Reason");
        result.StandardOutput.Should().Contain("Symbol");
    }

    [Fact]
    public async Task RetentionCommand_MissingDgml_ReturnsDgmlNotFound()
    {
        var sampleAotPath = CliTestHarness.SampleAot;
        if (sampleAotPath is null)
            return;

        var missingDgmlPath = Path.Combine(Path.GetDirectoryName(sampleAotPath)!, "missing.dgml");
        var result = await CliTestHarness.InvokeAsync(
            "retention",
            sampleAotPath,
            "--target",
            "Program",
            "--dgml-path",
            missingDgmlPath,
            "--output",
            "json");

        result.ExitCode.Should().Be(0);

        using var document = JsonDocument.Parse(result.StandardOutput);
        document.RootElement.GetProperty("error").GetProperty("kind").GetString().Should().Be("dgml_not_found");
    }
}
