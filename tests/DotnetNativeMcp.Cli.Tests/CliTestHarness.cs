using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;

namespace DotnetNativeMcp.Cli.Tests;

internal static class CliTestHarness
{
    public static async Task<CliProcessResult> InvokeAsync(params string[] arguments)
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
        await process.WaitForExitAsync().ConfigureAwait(false);

        return new CliProcessResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }

    public static string? SampleAot
    {
        get
        {
            var dir = Path.GetDirectoryName(typeof(CliTestHarness).Assembly.Location);
            for (var i = 0; i < 8 && dir is not null; i++, dir = Path.GetDirectoryName(dir))
            {
                // Read directly from the fixture output that
                // DotnetNativeMcp.Core.Tests's BuildNativeAotFixture target
                // publishes and copies alongside its own tests
                // (tests/DotnetNativeMcp.Core.Tests/bin/Release/net10.0/fixtures/SampleAot/),
                // where the native binary and its .mstat sidecar sit side by
                // side. Do not re-publish from this project: a second
                // concurrent `dotnet publish` of the same fixture project
                // races with Core.Tests's own publish and corrupts the
                // native link (observed as "file too short" from ld).
                var candidate = Path.Combine(dir, "tests", "DotnetNativeMcp.Core.Tests", "bin", "Release", "net10.0", "fixtures", "SampleAot", "SampleAot");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}

internal sealed record CliProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string CombinedOutput => $"{StandardOutput}\n{StandardError}";

    public JsonDocument ParseJson() => JsonDocument.Parse(StandardOutput);
}
