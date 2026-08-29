using System.Linq;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Cli.Tests;

public sealed class CliSmokeTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("--version")]
    public async Task RootCommands_ReturnSuccessAndOutput(string argument)
    {
        var result = await InvokeCliAsync(argument);

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("json", "\"summary\"")]
    [InlineData("table", "ToolCommandName")]
    public async Task VersionCommand_UsesConfiguredOutputWriter(string output, string expectedText)
    {
        var result = await InvokeCliAsync("version", "--output", output);

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain(expectedText);
    }

    [Fact]
    public async Task UnknownOutputValue_ReturnsClearError()
    {
        var result = await InvokeCliAsync("version", "--output", "yaml");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("--output");
        result.CombinedOutput.Should().Contain("json");
        result.CombinedOutput.Should().Contain("table");
    }

    [Fact]
    public void RootCommand_RegistersResolveAndCallers()
    {
        var names = CliApplication.CreateRootCommand().Subcommands.Select(command => command.Name);

        names.Should().Contain(["resolve", "callers", "version", "symbols", "imports", "size", "size-diff", "strings", "retention"]);
    }

    private static Task<CliProcessResult> InvokeCliAsync(params string[] arguments) =>
        CliTestHarness.InvokeAsync(arguments);
}
