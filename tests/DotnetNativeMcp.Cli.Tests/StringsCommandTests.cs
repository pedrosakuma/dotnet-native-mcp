using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Cli.Tests;

public sealed class StringsCommandTests
{
    [Fact]
    public async Task StringsCommand_JsonOutput_ReturnsUtf16Literal()
    {
        var sampleAotPath = CliTestHarness.SampleAot;
        if (sampleAotPath is null)
            return;

        var result = await CliTestHarness.InvokeAsync(
            "strings",
            sampleAotPath,
            "--min-length",
            "2",
            "--encodings",
            "utf16le",
            "--section",
            ".rodata",
            "--limit",
            "5000",
            "--output",
            "json");

        result.ExitCode.Should().Be(0);

        using var document = JsonDocument.Parse(result.StandardOutput);
        var strings = document.RootElement.GetProperty("data").GetProperty("strings");
        strings.EnumerateArray().Should().Contain(element =>
            element.GetProperty("encoding").GetString() == "utf16le"
            && element.GetProperty("value").GetString() == "hi");
    }

    [Fact]
    public async Task StringsCommand_TableOutput_RendersOffsetAndStringColumns()
    {
        var sampleAotPath = CliTestHarness.SampleAot;
        if (sampleAotPath is null)
            return;

        var result = await CliTestHarness.InvokeAsync(
            "strings",
            sampleAotPath,
            "--min-length",
            "2",
            "--encodings",
            "utf16le",
            "--section",
            ".rodata",
            "--limit",
            "5000",
            "--output",
            "table");

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("Offset");
        result.StandardOutput.Should().Contain("String");
        result.StandardOutput.Should().Contain("hi");
    }
}
