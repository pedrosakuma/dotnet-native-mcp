using DotnetNativeMcp.Core.R2R;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class ReadyToRunHeaderAttributesTests
{
    [Fact]
    public void DecodeNames_Zero_ReturnsEmpty()
    {
        ReadyToRunHeaderAttributesExtensions.DecodeNames(0).Should().BeEmpty();
    }

    [Fact]
    public void DecodeNames_SingleKnownFlag_ReturnsName()
    {
        ReadyToRunHeaderAttributesExtensions
            .DecodeNames((uint)ReadyToRunHeaderAttributes.Component)
            .Should().ContainSingle().Which.Should().Be("Component");
    }

    [Fact]
    public void DecodeNames_MultipleKnownFlags_ReturnsAllNamesInBitOrder()
    {
        // 0x3 == PlatformNeutralSource | SkipTypeValidation (the synthetic fixture value).
        var names = ReadyToRunHeaderAttributesExtensions.DecodeNames(0x00000003u);

        names.Should().Equal("PlatformNeutralSource", "SkipTypeValidation");
    }

    [Fact]
    public void DecodeNames_AllKnownFlags_DecodeWithoutUnknownResidue()
    {
        const uint allKnown =
            (uint)ReadyToRunHeaderAttributes.PlatformNeutralSource |
            (uint)ReadyToRunHeaderAttributes.SkipTypeValidation |
            (uint)ReadyToRunHeaderAttributes.Partial |
            (uint)ReadyToRunHeaderAttributes.NonSharedPInvokeStubs |
            (uint)ReadyToRunHeaderAttributes.EmbeddedMsil |
            (uint)ReadyToRunHeaderAttributes.Component |
            (uint)ReadyToRunHeaderAttributes.MultiModuleVersionBubble |
            (uint)ReadyToRunHeaderAttributes.UnrelatedR2RCode |
            (uint)ReadyToRunHeaderAttributes.PlatformNativeImage |
            (uint)ReadyToRunHeaderAttributes.StrippedIlBodies |
            (uint)ReadyToRunHeaderAttributes.StrippedInliningInfo |
            (uint)ReadyToRunHeaderAttributes.StrippedDebugInfo;

        var names = ReadyToRunHeaderAttributesExtensions.DecodeNames(allKnown);

        names.Should().HaveCount(12);
        names.Should().NotContain(n => n.StartsWith("Unknown", StringComparison.Ordinal));
    }

    [Fact]
    public void DecodeNames_UnknownBits_ReportedAsSingleUnknownEntry()
    {
        // 0x80000000 is not a defined flag; 0x20 is Component.
        var names = ReadyToRunHeaderAttributesExtensions.DecodeNames(0x80000020u);

        names.Should().Equal("Component", "Unknown(0x80000000)");
    }

    [Fact]
    public void DecodeNames_OnlyUnknownBits_ReportedAsUnknownOnly()
    {
        ReadyToRunHeaderAttributesExtensions
            .DecodeNames(0xF0000000u)
            .Should().ContainSingle().Which.Should().Be("Unknown(0xF0000000)");
    }
}
