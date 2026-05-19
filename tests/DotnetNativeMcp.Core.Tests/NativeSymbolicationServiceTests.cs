using DotnetNativeMcp.Core;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public sealed class NativeSymbolicationServiceTests
{
    private static readonly string[] RoundTripMapLines =
    [
        "00000010 FirstSymbol .text",
        "00000020 SecondSymbol .text",
        "00000030 ThirdSymbol .text",
    ];

    private static readonly string[] PartialFailureMapLines =
    [
        "00000010 FirstSymbol .text",
        "00000020 SecondSymbol .text",
    ];

    [Fact]
    public void Symbolicate_stack_round_trips_addresses_from_list_native_symbols()
    {
        using var fixture = BinaryFixture.Create(RoundTripMapLines);

        var service = new NativeSymbolicationService();
        var listed = service.ListNativeSymbols(fixture.BinaryPath);

        listed.Ok.Should().BeTrue();
        listed.Value.Should().NotBeNull();

        var frames = listed.Value!.Symbols
            .Select(s => new SymbolicateStackFrameRequest(fixture.BinaryPath, s.AddressHex))
            .ToArray();

        var symbolicated = service.SymbolicateStack(frames);

        symbolicated.Ok.Should().BeTrue();
        symbolicated.Value.Should().NotBeNull();
        symbolicated.Value!.Frames.Should().HaveCount(frames.Length);
        symbolicated.Value.Frames.Should().OnlyContain(f => f.Ok);
        symbolicated.Value.Frames.Select(f => f.Value!.Symbol)
            .Should()
            .Equal(listed.Value.Symbols.Select(s => s.Symbol));
    }

    [Fact]
    public void Symbolicate_stack_returns_per_frame_errors_without_failing_batch()
    {
        using var fixture = BinaryFixture.Create(PartialFailureMapLines);

        var missingBinary = Path.Combine(fixture.DirectoryPath, "missing.bin");
        var service = new NativeSymbolicationService();
        var frames = new[]
        {
            new SymbolicateStackFrameRequest(fixture.BinaryPath, "00000010"),
            new SymbolicateStackFrameRequest(missingBinary, "00000010"),
            new SymbolicateStackFrameRequest(fixture.BinaryPath, "0000FFFF"),
        };

        var result = service.SymbolicateStack(frames);

        result.Ok.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Frames.Should().HaveCount(3);

        result.Value.Frames[0].Ok.Should().BeTrue();
        result.Value.Frames[0].Value!.Symbol.Should().Be("FirstSymbol");

        result.Value.Frames[1].Ok.Should().BeFalse();
        result.Value.Frames[1].Error!.Kind.Should().Be("binary_not_found");

        result.Value.Frames[2].Ok.Should().BeFalse();
        result.Value.Frames[2].Error!.Kind.Should().Be("address_out_of_range");
    }

    private sealed class BinaryFixture : IDisposable
    {
        private BinaryFixture(string directoryPath, string binaryPath)
        {
            DirectoryPath = directoryPath;
            BinaryPath = binaryPath;
        }

        public string DirectoryPath { get; }

        public string BinaryPath { get; }

        public static BinaryFixture Create(IEnumerable<string> mapLines)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"native-mcp-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            var binaryPath = Path.Combine(directory, "sample.bin");
            File.WriteAllBytes(binaryPath, Enumerable.Range(0, 512).Select(i => (byte)i).ToArray());
            File.WriteAllLines(binaryPath + ".map", mapLines);

            return new BinaryFixture(directory, binaryPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
