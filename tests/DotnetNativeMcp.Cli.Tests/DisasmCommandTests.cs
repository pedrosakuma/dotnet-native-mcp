using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Cli.Tests;

public sealed class DisasmCommandTests : IDisposable
{
    private readonly List<string> _scratchFiles = [];

    [Fact]
    public async Task Disasm_PathMode_SampleAot_ReturnsInstructions()
    {
        var sampleAot = FindSampleAot();
        if (sampleAot is null)
        {
            return;
        }

        var result = await InvokeCliAsync(
            "disasm",
            sampleAot,
            "--address",
            "58c0",
            "--length",
            "64",
            "--allow",
            Path.GetDirectoryName(sampleAot)!);

        result.ExitCode.Should().Be(0, result.CombinedOutput);

        using var payload = JsonDocument.Parse(result.StandardOutput);
        payload.RootElement.GetProperty("summary").GetString().Should().Contain("Disassembled");
        payload.RootElement.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        payload.RootElement.GetProperty("data").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Disasm_RawBytes_X64_ReturnsDecodedInstructions()
    {
        var result = await InvokeCliAsync(
            "disasm",
            "--bytes",
            "90 c3",
            "--architecture",
            "x64");

        result.ExitCode.Should().Be(0, result.CombinedOutput);

        using var payload = JsonDocument.Parse(result.StandardOutput);
        var instructions = payload.RootElement.GetProperty("data");
        instructions[0].GetProperty("mnemonic").GetString().Should().Be("nop");
        instructions[1].GetProperty("mnemonic").GetString().Should().Be("ret");
        instructions[0].GetProperty("addressHex").GetString().Should().Be("0000000000000000");
        instructions[1].GetProperty("addressHex").GetString().Should().Be("0000000000000001");
    }

    [Fact]
    public async Task Disasm_RawBytes_FromStdin_ReturnsDecodedInstructions()
    {
        var result = await InvokeCliAsync(
            ["disasm", "--bytes", "-", "--architecture", "x64"],
            standardInput: "90c3");

        result.ExitCode.Should().Be(0, result.CombinedOutput);

        using var payload = JsonDocument.Parse(result.StandardOutput);
        payload.RootElement.GetProperty("data")[0].GetProperty("mnemonic").GetString().Should().Be("nop");
        payload.RootElement.GetProperty("data")[1].GetProperty("mnemonic").GetString().Should().Be("ret");
    }

    [Fact]
    public async Task Disasm_RawBytes_TableOutput_RendersInstructionRows()
    {
        var result = await InvokeCliAsync(
            "disasm",
            "--bytes",
            "1f2003d5c0035fd6",
            "--architecture",
            "arm64",
            "--output",
            "table");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.StandardOutput.Should().Contain("Address");
        result.StandardOutput.Should().Contain("Mnemonic");
        result.StandardOutput.Should().Contain("nop");
        result.StandardOutput.Should().Contain("ret");
    }

    [Fact]
    public async Task Disasm_BlobMode_Arm64_ReturnsDecodedInstructions()
    {
        var blobPath = WriteScratchBlob(
            "arm64-blob.bin",
            [0x01, 0x00, 0x00, 0x94, 0xC0, 0x03, 0x5F, 0xD6]);

        var result = await InvokeCliAsync(
            "disasm",
            "--blob",
            blobPath,
            "--architecture",
            "arm64",
            "--base-address",
            "400000",
            "--size",
            "8",
            "--allow",
            Path.GetDirectoryName(blobPath)!);

        result.ExitCode.Should().Be(0, result.CombinedOutput);

        using var payload = JsonDocument.Parse(result.StandardOutput);
        var instructions = payload.RootElement.GetProperty("data");
        instructions[0].GetProperty("mnemonic").GetString().Should().Be("bl");
        instructions[1].GetProperty("mnemonic").GetString().Should().Be("ret");
    }

    [Theory]
    [InlineData(new[] { "disasm", "--blob", "__BLOB__", "--base-address", "1000", "--size", "8" }, "raw_blob_missing_architecture")]
    [InlineData(new[] { "disasm", "--blob", "__BLOB__", "--architecture", "x64", "--size", "8" }, "raw_blob_missing_base_address")]
    [InlineData(new[] { "disasm", "--blob", "__BLOB__", "--architecture", "x64", "--base-address", "1000" }, "raw_blob_missing_size")]
    public async Task Disasm_BlobMode_MissingRequiredParameters_ReturnExpectedErrorKinds(string[] templateArguments, string expectedKind)
    {
        var blobPath = WriteScratchBlob("x64-blob.bin", [0x90, 0xC3]);
        var arguments = templateArguments
            .Select(argument => argument == "__BLOB__" ? blobPath : argument)
            .Concat(["--allow", Path.GetDirectoryName(blobPath)!])
            .ToArray();

        var result = await InvokeCliAsync(arguments);

        result.ExitCode.Should().Be(1, result.CombinedOutput);

        using var payload = JsonDocument.Parse(result.StandardOutput);
        payload.RootElement.GetProperty("error").GetProperty("kind").GetString().Should().Be(expectedKind);
    }

    public void Dispose()
    {
        foreach (var path in _scratchFiles)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }

        GC.SuppressFinalize(this);
    }

    private static async Task<CliProcessResult> InvokeCliAsync(params string[] arguments) =>
        await InvokeCliAsync(arguments, standardInput: null);

    private static async Task<CliProcessResult> InvokeCliAsync(string[] arguments, string? standardInput)
    {
        var assemblyPath = typeof(Program).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
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

        if (standardInput is not null)
        {
            await process!.StandardInput.WriteAsync(standardInput).ConfigureAwait(false);
        }

        process!.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);

        return new CliProcessResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }

    private string WriteScratchBlob(string fileName, byte[] bytes)
    {
        var scratchDir = Path.Combine(Path.GetDirectoryName(typeof(DisasmCommandTests).Assembly.Location)!, "scratch");
        Directory.CreateDirectory(scratchDir);

        var path = Path.Combine(scratchDir, $"{Guid.NewGuid():N}-{fileName}");
        File.WriteAllBytes(path, bytes);
        _scratchFiles.Add(path);
        return path;
    }

    private static string? FindSampleAot()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            return null;
        }

        var buildOutputCandidate = Path.Combine(
            repoRoot,
            "tests",
            "DotnetNativeMcp.Core.Tests",
            "bin",
            "Release",
            "net10.0",
            "fixtures",
            "SampleAot",
            "SampleAot");

        if (File.Exists(buildOutputCandidate))
        {
            return buildOutputCandidate;
        }

        var publishCandidate = Path.Combine(
            repoRoot,
            "tests",
            "fixtures",
            "SampleAot",
            "bin",
            "Release",
            "net10.0",
            "linux-x64",
            "publish",
            "SampleAot");

        return File.Exists(publishCandidate) ? publishCandidate : null;
    }

    private static string? FindRepoRoot()
    {
        var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory, "DotnetNativeMcp.slnx")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    private sealed record CliProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => $"{StandardOutput}\n{StandardError}";
    }
}
