using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// End-to-end validation of the <c>dotnet-diagnostics-mcp → dotnet-native-mcp</c> handoff
/// (docs/handoff-contract.md). Simulates a producer-emitted <c>NativeFrame</c> JSON payload
/// against the real NativeAOT <c>SampleAot</c> ELF and asserts that the documented resolution
/// flow recovers the symbol — including the ASLR rebasing path, which is the realistic case for
/// position-independent (PIE) NativeAOT binaries whose on-disk image base is 0.
/// Tests skip cleanly when the AOT fixture has not been built.
/// </summary>
public sealed class HandoffRoundTripTests
{
    /// <summary>
    /// Test-local mirror of the producer's <c>NativeFrame</c> wire shape (docs/handoff-contract.md).
    /// Deserializing a real-shaped payload here keeps the consumer honest about the field names and
    /// transport conventions it has committed to accept.
    /// </summary>
    private sealed record NativeFrameWire(
        [property: JsonPropertyName("binary")] string Binary,
        [property: JsonPropertyName("symbol")] string Symbol,
        [property: JsonPropertyName("address")] string? Address = null,
        [property: JsonPropertyName("loadBase")] string? LoadBase = null,
        [property: JsonPropertyName("buildId")] string? BuildId = null);

    private static NativeImage? LoadFixture()
    {
        var binaryPath = FixturePaths.SampleAot;
        if (binaryPath is null)
            return null;

        var bytes = File.ReadAllBytes(binaryPath);
        return ElfReader.Read(new ReadOnlyMemory<byte>(bytes), binaryPath);
    }

    private static NativeSymbol? FirstResolvableFunction(NativeImage image) =>
        image.Symbols
            .Where(s => s.IsFunction && s.Size > 0 && !string.IsNullOrEmpty(s.Name) && image.FindSection(s.Rva) is not null)
            .OrderBy(s => s.Rva)
            .FirstOrDefault();

    [Fact]
    public void SampleAot_IsPositionIndependent_WithZeroImageBase()
    {
        var image = LoadFixture();
        if (image is null) return; // AOT toolchain unavailable — skip

        // The fixture is a PIE executable, so the on-disk base is 0. This is precisely why a
        // producer-observed runtime VA cannot be resolved without the NativeFrame.loadBase.
        image.ImageBase.Should().Be(0);
    }

    [Fact]
    public void NativeFrame_AslrFrame_ResolvesOnlyWhenLoadBaseIsHonored()
    {
        var image = LoadFixture();
        if (image is null) return; // AOT toolchain unavailable — skip

        var target = FirstResolvableFunction(image);
        if (target is null) return; // no usable function symbol in fixture — skip

        // Simulate a producer that observed the binary loaded at an ASLR base. Per the contract,
        // address is the absolute runtime VA and is transported as lowercase hex without a 0x prefix.
        const ulong loadBase = 0x0000_7f12_3456_0000UL;
        var runtimeVa = loadBase + target.Rva;

        var payload = $$"""
            {
              "binary": "{{image.FilePath}}",
              "symbol": "{{target.Name}}",
              "address": "{{runtimeVa:x16}}",
              "loadBase": "{{loadBase:x16}}"
            }
            """;

        var frame = JsonSerializer.Deserialize<NativeFrameWire>(payload);
        frame.Should().NotBeNull();
        frame!.Address.Should().NotBeNull();
        frame.LoadBase.Should().NotBeNull();

        // Without loadBase the absolute VA is rebased against the on-disk base (0). It lands far
        // past every real symbol, so it never resolves correctly to the target — it either errors
        // (AddressOutOfRange) or falls through to a bogus nearest zero-size symbol with a huge
        // displacement. Either way it is NOT the correct target at offset 0. This is the broken
        // round-trip this feature fixes.
        var withoutBase = StackSymbolicator.ResolveAddresses(image, [frame.Address!]);
        withoutBase.IsError.Should().BeFalse();
        var oobRow = withoutBase.Data!.Should().ContainSingle().Subject;
        var correctlyResolvedWithoutBase =
            oobRow.Error is null && oobRow.MangledName == target.Name && oobRow.Displacement == 0;
        correctlyResolvedWithoutBase.Should().BeFalse(
            "an ASLR'd runtime VA must not resolve correctly without the producer's loadBase");

        // With the producer-supplied loadBase parsed off the wire, the frame resolves to the symbol.
        StackSymbolicator.TryParseHexAddress(frame.LoadBase, out var parsedLoadBase, out _).Should().BeTrue();
        var withBase = StackSymbolicator.ResolveAddresses(image, [frame.Address!], parsedLoadBase);

        withBase.IsError.Should().BeFalse();
        var row = withBase.Data!.Should().ContainSingle().Subject;
        row.Error.Should().BeNull();
        row.MangledName.Should().Be(target.Name);
        row.Displacement.Should().Be(0);
    }

    [Fact]
    public void NativeFrame_NonZeroDisplacementWithinFunction_IsReportedRelativeToSymbolStart()
    {
        var image = LoadFixture();
        if (image is null) return; // AOT toolchain unavailable — skip

        var target = image.Symbols
            .Where(s => s.IsFunction && s.Size > 4 && !string.IsNullOrEmpty(s.Name) && image.FindSection(s.Rva) is not null)
            .OrderBy(s => s.Rva)
            .FirstOrDefault();
        if (target is null) return; // skip

        const ulong loadBase = 0x0000_5555_5555_0000UL;
        const ulong offset = 4;
        var runtimeVa = loadBase + target.Rva + offset;

        var result = StackSymbolicator.ResolveAddresses(image, ["0x" + runtimeVa.ToString("x")], loadBase);

        result.IsError.Should().BeFalse();
        var row = result.Data!.Single();
        row.Error.Should().BeNull();
        row.MangledName.Should().Be(target.Name);
        row.Displacement.Should().Be(offset);
    }
}
