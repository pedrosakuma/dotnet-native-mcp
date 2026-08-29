using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Cli.Tests;

public sealed class SymbolsCommandTests
{
    [Fact]
    public async Task SymbolsCommand_ListsFixtureSymbolsAsJson()
    {
        if (CliTestHarness.SampleAot is not { } fixturePath)
            return;

        var result = await CliTestHarness.InvokeAsync("symbols", fixturePath, "--filter", "Main");

        result.ExitCode.Should().Be(0);
        using var json = result.ParseJson();
        json.RootElement.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        var data = json.RootElement.GetProperty("data");
        data.GetProperty("symbols").GetArrayLength().Should().BeGreaterThan(0);
        data.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(0);
        data.GetProperty("symbols").EnumerateArray().First().GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("symbols").EnumerateArray().Should().OnlyContain(symbol =>
            Contains(symbol, "name", "Main") || Contains(symbol, "demangledName", "Main"));
    }

    [Fact]
    public async Task SymbolsCommand_PaginatesWithCursor()
    {
        if (CliTestHarness.SampleAot is not { } fixturePath)
            return;

        var firstPageResult = await CliTestHarness.InvokeAsync("symbols", fixturePath, "--limit", "1");
        var secondPageResult = await CliTestHarness.InvokeAsync("symbols", fixturePath, "--limit", "1", "--cursor", "1");

        using var firstPage = firstPageResult.ParseJson();
        using var secondPage = secondPageResult.ParseJson();

        firstPageResult.ExitCode.Should().Be(0);
        secondPageResult.ExitCode.Should().Be(0);

        var firstSymbols = firstPage.RootElement.GetProperty("data").GetProperty("symbols");
        var secondSymbols = secondPage.RootElement.GetProperty("data").GetProperty("symbols");
        firstSymbols.GetArrayLength().Should().Be(1);
        secondSymbols.GetArrayLength().Should().Be(1);
        firstPage.RootElement.GetProperty("data").GetProperty("nextCursor").GetInt32().Should().Be(1);
        firstPage.RootElement.GetProperty("hints")[0].GetProperty("suggestedArguments").GetProperty("page-size").GetInt32().Should().Be(1);
        var firstSymbol = firstSymbols.EnumerateArray().First();
        var secondSymbol = secondSymbols.EnumerateArray().First();
        firstSymbol.GetProperty("name").GetString().Should().NotBe(secondSymbol.GetProperty("name").GetString());
    }

    [Fact]
    public async Task SymbolsCommand_TableOutput_RendersColumns()
    {
        if (CliTestHarness.SampleAot is not { } fixturePath)
            return;

        var result = await CliTestHarness.InvokeAsync("symbols", fixturePath, "--output", "table", "--limit", "1");

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("TotalCount");
        result.StandardOutput.Should().Contain("Symbols");
        result.StandardOutput.Should().Contain("Name");
        result.StandardOutput.Should().Contain("RvaHex");
    }

    [Fact]
    public async Task SymbolsCommand_PathOutsideAllowlist_ReturnsTypedError()
    {
        if (CliTestHarness.SampleAot is not { } fixturePath)
            return;

        var allowedRoot = Path.GetDirectoryName(fixturePath) ?? fixturePath;
        var deniedPath = Path.GetFullPath(Path.Combine(allowedRoot, "..", "..", "outside", "SampleAot"));

        var result = await CliTestHarness.InvokeAsync("symbols", deniedPath, "--allow", allowedRoot);

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
