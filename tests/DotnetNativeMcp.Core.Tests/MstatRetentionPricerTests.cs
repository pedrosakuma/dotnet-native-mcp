using DotnetNativeMcp.Core.Mstat;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class MstatRetentionPricerTests
{
    private static MstatDocument Doc(params MstatAttribution[] attributions) =>
        new(
            "/tmp/x.mstat",
            attributions,
            MethodCount: 0,
            TypeCount: 0,
            TotalSize: 0,
            FormatVersion: "2.0",
            CategoryTotals: [],
            DeduplicatedMethodCount: 0);

    [Fact]
    public void TryPrice_MethodLabel_MatchesByMangledSuffix()
    {
        var pricer = MstatRetentionPricer.Build(Doc(
            new MstatAttribution("System.Private.CoreLib", "System", "System.AppContext", "OnFirstChanceException", 189, MstatCategory.Method)));

        pricer.TryPrice("S_P_CoreLib_System_AppContext__OnFirstChanceException", out var cost).Should().BeTrue();
        cost.SizeBytes.Should().Be(189);
        cost.MatchKind.Should().Be(MstatCategory.Method);
        cost.AttributionCount.Should().Be(1);
        cost.TypeName.Should().Be("System.AppContext");
        cost.MethodName.Should().Be("OnFirstChanceException");
    }

    [Fact]
    public void TryPrice_TypeLabel_MatchesByMangledSuffix()
    {
        var pricer = MstatRetentionPricer.Build(Doc(
            new MstatAttribution("App", "MyApp", "MyApp.Widget", null, 42, MstatCategory.Type)));

        pricer.TryPrice("App_MyApp_Widget", out var cost).Should().BeTrue();
        cost.SizeBytes.Should().Be(42);
        cost.MatchKind.Should().Be(MstatCategory.Type);
        cost.MethodName.Should().BeNull();
    }

    [Fact]
    public void TryPrice_MethodWinsOverType_WhenBothCouldMatch()
    {
        var pricer = MstatRetentionPricer.Build(Doc(
            new MstatAttribution("App", "MyApp", "MyApp.Foo", null, 10, MstatCategory.Type),
            new MstatAttribution("App", "MyApp", "MyApp.Foo", "Bar", 7, MstatCategory.Method)));

        pricer.TryPrice("App_MyApp_Foo__Bar", out var cost).Should().BeTrue();
        cost.MatchKind.Should().Be(MstatCategory.Method);
        cost.SizeBytes.Should().Be(7);
    }

    [Fact]
    public void TryPrice_OverloadsSharingAName_AggregateAndCount()
    {
        var pricer = MstatRetentionPricer.Build(Doc(
            new MstatAttribution("App", "MyApp", "MyApp.Cfg", "Get", 100, MstatCategory.Method),
            new MstatAttribution("App", "MyApp", "MyApp.Cfg", "Get", 50, MstatCategory.Method)));

        pricer.TryPrice("App_MyApp_Cfg__Get", out var cost).Should().BeTrue();
        cost.SizeBytes.Should().Be(150);
        cost.AttributionCount.Should().Be(2);
    }

    [Fact]
    public void TryPrice_LongestKeyWins()
    {
        var pricer = MstatRetentionPricer.Build(Doc(
            new MstatAttribution("App", "N", "N.A.B", null, 5, MstatCategory.Type),
            new MstatAttribution("App", "N", "N.B", null, 9, MstatCategory.Type)));

        // Label ends with both "N_B" and "N_A_B"; the longer key wins.
        pricer.TryPrice("App_N_A_B", out var cost).Should().BeTrue();
        cost.SizeBytes.Should().Be(5);
        cost.TypeName.Should().Be("N.A.B");
    }

    [Fact]
    public void TryPrice_RequiresUnderscoreBoundary_NoPartialTokenMatch()
    {
        var pricer = MstatRetentionPricer.Build(Doc(
            new MstatAttribution("App", "", "Context", null, 5, MstatCategory.Type)));

        // "MyContext" must not match the key "Context" (no '_' boundary before it).
        pricer.TryPrice("App_MyContext", out _).Should().BeFalse();
    }

    [Fact]
    public void TryPrice_UnmatchedLabel_ReturnsFalse()
    {
        var pricer = MstatRetentionPricer.Build(Doc(
            new MstatAttribution("App", "MyApp", "MyApp.Foo", "Bar", 7, MstatCategory.Method)));

        pricer.TryPrice("__FrozenSegmentStart", out _).Should().BeFalse();
        pricer.TryPrice(null, out _).Should().BeFalse();
        pricer.TryPrice("", out _).Should().BeFalse();
    }

    [Fact]
    public void TryPrice_AmbiguousEqualLengthKeys_AreNotGuessed()
    {
        // Two distinct types whose mangled keys are equal length and both suffix-match the label.
        var pricer = MstatRetentionPricer.Build(Doc(
            new MstatAttribution("App", "A", "A.X", null, 5, MstatCategory.Type),
            new MstatAttribution("App", "B", "B.X", null, 9, MstatCategory.Type)));

        // Label "A_X" only ends with "A_X" (length 3); "B_X" doesn't suffix it — so this is unique.
        pricer.TryPrice("A_X", out var unique).Should().BeTrue();
        unique.SizeBytes.Should().Be(5);
    }

    [Fact]
    public void Build_NestedTypeSeparator_IsFlattened()
    {
        var pricer = MstatRetentionPricer.Build(Doc(
            new MstatAttribution("App", "MyApp", "MyApp.Outer+Inner", "M", 11, MstatCategory.Method)));

        pricer.TryPrice("App_MyApp_Outer_Inner__M", out var cost).Should().BeTrue();
        cost.SizeBytes.Should().Be(11);
    }

    [Fact]
    public void TryPrice_DistinctIdentitiesManglingToSameKey_AreAmbiguousNotSummed()
    {
        // A nested type (Outer+Inner) and a dotted type (Outer.Inner) mangle to the same key but are
        // distinct managed identities — the size must not be summed and attributed to either node.
        var pricer = MstatRetentionPricer.Build(Doc(
            new MstatAttribution("App", "N", "N.Outer+Inner", "M", 10, MstatCategory.Method),
            new MstatAttribution("App", "N", "N.Outer.Inner", "M", 20, MstatCategory.Method)));

        pricer.TryPrice("App_N_Outer_Inner__M", out _).Should().BeFalse();
    }

    [Fact]
    public void TryPrice_SameTypeMethodInDifferentAssemblies_AreAmbiguous()
    {
        var pricer = MstatRetentionPricer.Build(Doc(
            new MstatAttribution("AsmA", "N", "N.Foo", "Bar", 10, MstatCategory.Method),
            new MstatAttribution("AsmB", "N", "N.Foo", "Bar", 20, MstatCategory.Method)));

        pricer.TryPrice("AsmA_N_Foo__Bar", out _).Should().BeFalse();
    }

    [Fact]
    public void TryPrice_AmbiguousMethodMatch_DoesNotFallBackToTypePricing()
    {
        // The method key "N_Foo__Bar" is ambiguous (two assemblies), and a type happens to mangle to the
        // same string. An ambiguous method match must suppress pricing entirely, never price as the type.
        var pricer = MstatRetentionPricer.Build(Doc(
            new MstatAttribution("AsmA", "N", "N.Foo", "Bar", 10, MstatCategory.Method),
            new MstatAttribution("AsmB", "N", "N.Foo", "Bar", 20, MstatCategory.Method),
            new MstatAttribution("AsmC", "N", "N.Foo__Bar", null, 99, MstatCategory.Type)));

        pricer.TryPrice("AsmA_N_Foo__Bar", out _).Should().BeFalse();
    }
}