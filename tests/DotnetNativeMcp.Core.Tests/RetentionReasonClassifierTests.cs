using System.Collections.ObjectModel;
using DotnetNativeMcp.Core.Dgml;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class RetentionReasonClassifierTests
{
    [Theory]
    [InlineData("Reflectable type")]
    [InlineData("Reflectable method")]
    [InlineData("Reflection invoke")]
    [InlineData("NecessaryType for metadata type")]
    [InlineData("MetadataType for constructed type")]
    [InlineData("Method signature metadata")]
    [InlineData("Dataflow for type definition")]
    [InlineData("Method has annotated parameters")]
    [InlineData("Methods have same reflectability")]
    public void Classify_ReflectionAndMetadataReasons_ReturnReflection(string reason) =>
        RetentionReasonClassifier.Classify(reason).Should().Be(RetentionReasonKind.Reflection);

    [Theory]
    [InlineData("Generic dictionary dependency")]
    [InlineData("Dictionary dependency")]
    [InlineData("Dictionary contents")]
    [InlineData("Template type")]
    [InlineData("Type loader template")]
    public void Classify_GenericReasons_ReturnGenerics(string reason) =>
        RetentionReasonClassifier.Classify(reason).Should().Be(RetentionReasonKind.Generics);

    [Theory]
    [InlineData("Virtual method")]
    [InlineData("VTable")]
    [InlineData("Sealed Vtable")]
    [InlineData("Interface vtable slice")]
    [InlineData("Interface for a dispatch map")]
    [InlineData("Slot is a delegate target")]
    public void Classify_VirtualDispatchReasons_ReturnVirtualDispatch(string reason) =>
        RetentionReasonClassifier.Classify(reason).Should().Be(RetentionReasonKind.VirtualDispatch);

    [Theory]
    [InlineData("call")]
    [InlineData("callvirt")]
    [InlineData("newobj")]
    [InlineData("ldstr")]
    [InlineData("ldtoken")]
    [InlineData("reloc")]
    [InlineData("Field written outside initializer")]
    [InlineData("GC statics indirection")]
    [InlineData("Call to interesting method")]
    [InlineData("IsInst/CastClass")]
    [InlineData("Instance method on a constructed type")]
    public void Classify_DirectCodeReasons_ReturnDirectCode(string reason) =>
        RetentionReasonClassifier.Classify(reason).Should().Be(RetentionReasonKind.DirectCode);

    [Theory]
    [InlineData("Primary")]
    [InlineData("Secondary")]
    [InlineData("Layout")]
    [InlineData("Global module type")]
    public void Classify_StructuralReasons_ReturnStructural(string reason) =>
        RetentionReasonClassifier.Classify(reason).Should().Be(RetentionReasonKind.Structural);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_NullOrEmpty_ReturnStructural(string? reason) =>
        RetentionReasonClassifier.Classify(reason).Should().Be(RetentionReasonKind.Structural);

    [Fact]
    public void Classify_UnrecognisedReason_ReturnsUnknown() =>
        RetentionReasonClassifier.Classify("Synchronized method").Should().Be(RetentionReasonKind.Unknown);

    [Fact]
    public void Classify_ReflectionTakesPrecedenceOverDelegate() =>
        // "Delegate invoke method is always reflectable" mentions both — reflection wins.
        RetentionReasonClassifier.Classify("Delegate invoke method is always reflectable")
            .Should().Be(RetentionReasonKind.Reflection);

    [Fact]
    public void ClassifyPath_PathWithReflectionEdge_IsReflectionDriven()
    {
        var path = MakePath(
            ("root", null),
            ("mid", "call"),
            ("target", "Reflectable type"));

        var classification = RetentionReasonClassifier.ClassifyPath(path);

        classification.ReflectionDriven.Should().BeTrue();
        classification.Verdict.Should().Be(RetentionReasonClassifier.ReflectionDrivenVerdict);
        classification.EdgeKindCounts[RetentionReasonKind.Reflection].Should().Be(1);
        classification.EdgeKindCounts[RetentionReasonKind.DirectCode].Should().Be(1);
    }

    [Fact]
    public void ClassifyPath_AllCodeEdges_IsStructural()
    {
        var path = MakePath(
            ("root", null),
            ("mid", "call"),
            ("target", "Virtual method"));

        var classification = RetentionReasonClassifier.ClassifyPath(path);

        classification.ReflectionDriven.Should().BeFalse();
        classification.Verdict.Should().Be(RetentionReasonClassifier.StructuralVerdict);
        classification.EdgeKindCounts.Should().NotContainKey(RetentionReasonKind.Reflection);
    }

    [Fact]
    public void IsReflectionDriven_AnyReflectionReason_ReturnsTrue() =>
        RetentionReasonClassifier.IsReflectionDriven(["call", "reloc", "Reflectable type"]).Should().BeTrue();

    [Fact]
    public void IsReflectionDriven_NoReflectionReason_ReturnsFalse() =>
        RetentionReasonClassifier.IsReflectionDriven(["call", "Virtual method", null]).Should().BeFalse();

    private static RetentionPath MakePath(params (string Id, string? IncomingEdge)[] segments)
    {
        var list = segments
            .Select(s => new RetentionPathSegment(s.Id, s.Id, null, s.IncomingEdge))
            .ToList();
        return new RetentionPath(
            list[0].NodeId,
            list[0].Label,
            null,
            list.Count - 1,
            new ReadOnlyCollection<RetentionPathSegment>(list));
    }
}
