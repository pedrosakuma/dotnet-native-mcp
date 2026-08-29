using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Cli.Tests;

public sealed class ImportsCommandTests
{
    [Fact]
    public async Task ImportsCommand_ListsFixtureLibrariesAsJson()
    {
        if (CliTestHarness.SampleAot is not { } fixturePath)
            return;

        var result = await CliTestHarness.InvokeAsync("imports", fixturePath, "--kind", "libraries", "--filter", "libc");

        result.ExitCode.Should().Be(0);
        using var json = result.ParseJson();
        json.RootElement.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        var data = json.RootElement.GetProperty("data");
        data.GetProperty("kind").GetString().Should().Be("libraries");
        data.GetProperty("libraries").GetArrayLength().Should().BeGreaterThan(0);
        data.GetProperty("libraries").EnumerateArray().Should().OnlyContain(library =>
            Contains(library, "name", "libc"));
    }

    [Fact]
    public async Task ImportsCommand_Functions_PaginatesWithCursor()
    {
        if (CliTestHarness.SampleAot is not { } fixturePath)
            return;

        var firstPageResult = await CliTestHarness.InvokeAsync("imports", fixturePath, "--limit", "1");
        var secondPageResult = await CliTestHarness.InvokeAsync("imports", fixturePath, "--limit", "1", "--cursor", "1");

        using var firstPage = firstPageResult.ParseJson();
        using var secondPage = secondPageResult.ParseJson();

        firstPageResult.ExitCode.Should().Be(0);
        secondPageResult.ExitCode.Should().Be(0);

        var firstFunctions = firstPage.RootElement.GetProperty("data").GetProperty("functions");
        var secondFunctions = secondPage.RootElement.GetProperty("data").GetProperty("functions");
        firstFunctions.GetArrayLength().Should().Be(1);
        secondFunctions.GetArrayLength().Should().Be(1);
        firstPage.RootElement.GetProperty("data").GetProperty("nextCursor").GetInt32().Should().Be(1);
        firstPage.RootElement.GetProperty("hints")[0].GetProperty("suggestedArguments").GetProperty("page-size").GetInt32().Should().Be(1);
        var firstFunction = firstFunctions.EnumerateArray().First();
        var secondFunction = secondFunctions.EnumerateArray().First();
        firstFunction.GetProperty("name").GetString().Should().NotBe(secondFunction.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ImportsCommand_TableOutput_RendersColumns()
    {
        if (CliTestHarness.SampleAot is not { } fixturePath)
            return;

        var result = await CliTestHarness.InvokeAsync("imports", fixturePath, "--output", "table", "--kind", "libraries", "--limit", "1");

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("Kind");
        result.StandardOutput.Should().Contain("Libraries");
        result.StandardOutput.Should().Contain("Name");
    }


    [Fact]
    public async Task ImportsCommand_InvalidKind_ReturnsTypedError()
    {
        if (CliTestHarness.SampleAot is not { } fixturePath)
            return;

        var result = await CliTestHarness.InvokeAsync("imports", fixturePath, "--kind", "bogus");

        result.ExitCode.Should().NotBe(0);
        using var json = result.ParseJson();
        json.RootElement.GetProperty("error").GetProperty("kind").GetString().Should().Be("invalid_argument");
    }

    [Fact]
    public async Task ImportsCommand_PathOutsideAllowlist_ReturnsTypedError()
    {
        if (CliTestHarness.SampleAot is not { } fixturePath)
            return;

        var allowedRoot = Path.GetDirectoryName(fixturePath) ?? fixturePath;
        var deniedPath = Path.GetFullPath(Path.Combine(allowedRoot, "..", "..", "outside", "SampleAot"));

        var result = await CliTestHarness.InvokeAsync("imports", deniedPath, "--allow", allowedRoot);

        result.ExitCode.Should().NotBe(0);
        using var json = result.ParseJson();
        json.RootElement.GetProperty("error").GetProperty("kind").GetString().Should().Be("path_not_allowed");
    }

    private static bool Contains(JsonElement element, string propertyName, string value)
    {
        var text = element.GetProperty(propertyName).GetString();
        return text is not null && text.Contains(value, StringComparison.OrdinalIgnoreCase);
    }
}
