using System.Diagnostics;
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

    private static async Task<CliProcessResult> InvokeCliAsync(params string[] arguments)
    {
        var assemblyPath = typeof(Program).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        startInfo.ArgumentList.Add(assemblyPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        process.Should().NotBeNull();

        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new CliProcessResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }

    private sealed record CliProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => $"{StandardOutput}\n{StandardError}";
    }
}
