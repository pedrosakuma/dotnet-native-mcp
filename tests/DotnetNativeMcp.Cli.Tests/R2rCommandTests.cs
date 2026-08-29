using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Cli.Tests;

public sealed class R2rCommandTests
{
    [Fact]
    public async Task HeaderCommand_JsonOutput_DecodesReadyToRunHeader()
    {
        var fixturePath = FindSystemPrivateCoreLibFixture();
        if (fixturePath is null)
            return;

        var result = await InvokeCliAsync(
            "r2r",
            "header",
            fixturePath,
            "--allow",
            Path.GetDirectoryName(fixturePath)!,
            "--output",
            "json");

        result.ExitCode.Should().Be(0, result.CombinedOutput);

        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        root.GetProperty("summary").GetString().Should().StartWith("R2R header v");

        var data = root.GetProperty("data");
        data.GetProperty("path").GetString().Should().Be(fixturePath);
        data.GetProperty("version").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("architecture").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("sectionCount").GetInt32().Should().BeGreaterThan(0);
        data.GetProperty("hasRuntimeFunctions").GetBoolean().Should().BeTrue();
        data.GetProperty("sections").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task HeaderCommand_TableOutput_RendersSectionTable()
    {
        var fixturePath = FindSystemPrivateCoreLibFixture();
        if (fixturePath is null)
            return;

        var result = await InvokeCliAsync(
            "r2r",
            "header",
            fixturePath,
            "--allow",
            Path.GetDirectoryName(fixturePath)!,
            "--output",
            "table");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.StandardOutput.Should().Contain("Sections");
        result.StandardOutput.Should().Contain("TypeName");
        result.StandardOutput.Should().Contain("RuntimeFunctions");
    }

    [Fact]
    public async Task RuntimeFunctionsCommand_JsonOutput_Paginates()
    {
        var fixturePath = FindSystemPrivateCoreLibFixture();
        if (fixturePath is null)
            return;

        var result = await InvokeCliAsync(
            "r2r",
            "runtime-functions",
            fixturePath,
            "--allow",
            Path.GetDirectoryName(fixturePath)!,
            "--limit",
            "2",
            "--output",
            "json");

        result.ExitCode.Should().Be(0, result.CombinedOutput);

        using var document = JsonDocument.Parse(result.StandardOutput);
        var data = document.RootElement.GetProperty("data");
        data.GetProperty("cursor").GetInt32().Should().Be(0);
        data.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(2);
        data.GetProperty("nextCursor").GetInt32().Should().Be(2);
        var functions = data.GetProperty("functions");
        functions.GetArrayLength().Should().Be(2);
        functions[0].GetProperty("index").GetInt32().Should().Be(0);
        functions[0].GetProperty("beginAddress").GetString().Should().StartWith("0x");
        functions[0].GetProperty("endAddress").GetString().Should().StartWith("0x");
        functions[0].GetProperty("unwindInfoAddress").GetString().Should().StartWith("0x");
    }

    [Fact]
    public async Task RuntimeFunctionsCommand_TableOutput_RendersFunctionColumns()
    {
        var fixturePath = FindSystemPrivateCoreLibFixture();
        if (fixturePath is null)
            return;

        var result = await InvokeCliAsync(
            "r2r",
            "runtime-functions",
            fixturePath,
            "--allow",
            Path.GetDirectoryName(fixturePath)!,
            "--cursor",
            "2",
            "--limit",
            "2",
            "--output",
            "table");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.StandardOutput.Should().Contain("Functions");
        result.StandardOutput.Should().Contain("BeginAddress");
        result.StandardOutput.Should().Contain("UnwindInfoAddress");
    }

    private static string? FindSystemPrivateCoreLibFixture()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
            return null;

        var candidate = Path.Combine(
            repoRoot,
            "tests",
            "fixtures",
            "SampleAot",
            "bin",
            "Release",
            "net10.0",
            "linux-x64",
            "System.Private.CoreLib.dll");

        return File.Exists(candidate) ? candidate : null;
    }

    private static string? FindRepoRoot()
    {
        var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory, "DotnetNativeMcp.slnx")))
                return directory;

            directory = Path.GetDirectoryName(directory);
        }

        return null;
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
