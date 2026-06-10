namespace DotnetNativeMcp.Core.Dgml;

/// <summary>
/// Coarse classification of an ILC retention reason (the DGML <c>Reason</c> attribute), used to tell
/// whether a retention path is reflection-driven (potentially trimmable by removing reflection roots /
/// <c>DynamicDependency</c> / trimmer descriptors) or structural (a direct code, vtable or generics
/// dependency that cannot be removed without changing the program).
/// </summary>
public enum RetentionReasonKind
{
    /// <summary>Reason carries no information (empty <c>Reason</c>) or is a framework-structural edge.</summary>
    Structural,

    /// <summary>Reflection / metadata reason — the trim-relevant bucket (e.g. "Reflectable type", "MetadataType for constructed type", "Dataflow for type definition").</summary>
    Reflection,

    /// <summary>Generics: dictionaries, templates, type-loader dependencies.</summary>
    Generics,

    /// <summary>Virtual dispatch: vtables, interfaces, virtual methods, delegate targets.</summary>
    VirtualDispatch,

    /// <summary>Direct code: IL opcodes, relocations, field / static access, static constructors.</summary>
    DirectCode,

    /// <summary>Recognised as a real reason but not matched by any known bucket.</summary>
    Unknown,
}

/// <summary>The classification verdict for a whole retention path.</summary>
public sealed record RetentionPathClassification(
    string Verdict,
    bool ReflectionDriven,
    IReadOnlyDictionary<RetentionReasonKind, int> EdgeKindCounts);

/// <summary>
/// Classifies ILC retention reasons. Matching is keyword-based and deterministic, applied in a fixed
/// precedence so a reason that mentions several concepts lands in its most specific bucket
/// (reflection first, then generics, virtual dispatch, direct code, structural).
/// </summary>
public static class RetentionReasonClassifier
{
    public const string ReflectionDrivenVerdict = "reflection-driven";
    public const string StructuralVerdict = "structural";

    private static readonly string[] DirectCodeOpcodes =
    [
        "call", "callvirt", "calli", "newobj", "newarr", "ldstr", "ldsfld", "ldsflda",
        "stsfld", "ldtoken", "ldftn", "ldvirtftn", "ldelem", "ldelema", "stelem",
        "box", "unbox", "rem",
    ];

    /// <summary>Classifies a single retention reason. Null/whitespace is treated as a structural edge.</summary>
    public static RetentionReasonKind Classify(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return RetentionReasonKind.Structural;

        var value = reason.Trim();
        var lower = value.ToLowerInvariant();

        if (ContainsAny(lower, "reflect", "metadata", "dataflow", "annotated"))
            return RetentionReasonKind.Reflection;

        if (ContainsAny(lower, "dictionary", "generic", "template", "type loader", "typesignature"))
            return RetentionReasonKind.Generics;

        if (ContainsAny(lower, "vtable", "virtual", "interface", "dispatch", "delegate"))
            return RetentionReasonKind.VirtualDispatch;

        if (IsDirectCode(lower))
            return RetentionReasonKind.DirectCode;

        if (ContainsAny(lower, "primary", "secondary", "layout", "global module type", "static bases", "module with a static constructor"))
            return RetentionReasonKind.Structural;

        return RetentionReasonKind.Unknown;
    }

    /// <summary>True when any of the supplied edge reasons classifies as <see cref="RetentionReasonKind.Reflection"/>.</summary>
    public static bool IsReflectionDriven(IEnumerable<string?> edgeReasons)
    {
        ArgumentNullException.ThrowIfNull(edgeReasons);
        foreach (var reason in edgeReasons)
        {
            if (Classify(reason) == RetentionReasonKind.Reflection)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Classifies a whole retention path by its edges (the root node has no incoming edge and is ignored).
    /// A path is reflection-driven when at least one edge along it is a reflection / metadata reason.
    /// </summary>
    public static RetentionPathClassification ClassifyPath(RetentionPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var counts = new Dictionary<RetentionReasonKind, int>();
        var reflectionDriven = false;

        foreach (var segment in path.Segments)
        {
            if (segment.IncomingEdgeLabel is null)
                continue;

            var kind = Classify(segment.IncomingEdgeLabel);
            counts[kind] = counts.TryGetValue(kind, out var count) ? count + 1 : 1;
            if (kind == RetentionReasonKind.Reflection)
                reflectionDriven = true;
        }

        return new RetentionPathClassification(
            reflectionDriven ? ReflectionDrivenVerdict : StructuralVerdict,
            reflectionDriven,
            counts);
    }

    private static bool IsDirectCode(string lower)
    {
        foreach (var opcode in DirectCodeOpcodes)
        {
            if (lower.Contains(opcode, StringComparison.Ordinal))
                return true;
        }

        return ContainsAny(lower, "reloc", "field", "static", "cctor", "constructor", "interesting", "cast", "isinst", "constructed type", "instance method");
    }

    private static bool ContainsAny(string lower, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (lower.Contains(needle, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
