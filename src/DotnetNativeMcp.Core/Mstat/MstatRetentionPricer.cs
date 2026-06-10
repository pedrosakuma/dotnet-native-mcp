namespace DotnetNativeMcp.Core.Mstat;

/// <summary>
/// The native byte cost mstat attributes to a single DGML retention node, recovered by matching the
/// node's mangled label against the mstat method/type tables. <see cref="AttributionCount"/> is greater
/// than one when several mstat attributions (e.g. overloads the linker mangles distinctly but mstat
/// records under one method name) collapse onto the same matched key — the size is then their sum and
/// should be read as an upper bound for the node.
/// </summary>
public sealed record MstatNodeCost(
    long SizeBytes,
    int AttributionCount,
    string MatchKind,
    string AssemblyName,
    string TypeName,
    string? MethodName);

/// <summary>
/// Prices DGML retention nodes against an <see cref="MstatDocument"/>. NativeAOT mangles a method node's
/// DGML label as <c>&lt;assembly&gt;_&lt;Type&gt;__&lt;Method&gt;</c> and a type node's label as
/// <c>&lt;assembly&gt;_&lt;Type&gt;</c> (namespace/nesting separators flattened to <c>_</c>). The mstat
/// tables key the same entities by their managed <c>Type</c> / <c>Method</c> names, so we match a label
/// to the longest mstat key it ends with on a <c>_</c> boundary, preferring the more specific method keys.
/// Matching is best-effort: nodes with a mangled signature suffix, generic instantiation, or that are
/// blobs / regions / EETypes the mstat tables do not price simply stay unpriced.
/// </summary>
public sealed class MstatRetentionPricer
{
    private readonly IReadOnlyDictionary<string, KeyBucket> _methodKeys;
    private readonly IReadOnlyDictionary<string, KeyBucket> _typeKeys;

    private MstatRetentionPricer(
        IReadOnlyDictionary<string, KeyBucket> methodKeys,
        IReadOnlyDictionary<string, KeyBucket> typeKeys)
    {
        _methodKeys = methodKeys;
        _typeKeys = typeKeys;
    }

    /// <summary>Builds a pricer from an mstat document. Aggregates attributions that share a mangled key.</summary>
    public static MstatRetentionPricer Build(MstatDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var methods = new Dictionary<string, KeyBucket>(StringComparer.Ordinal);
        var types = new Dictionary<string, KeyBucket>(StringComparer.Ordinal);

        foreach (var attribution in document.Attributions)
        {
            if (attribution.Source == MstatCategory.Method && attribution.MethodName is { Length: > 0 })
            {
                var key = Mangle(attribution.TypeName) + "__" + attribution.MethodName;
                Accumulate(methods, key, attribution, MstatCategory.Method);
            }
            else if (attribution.Source == MstatCategory.Type)
            {
                var key = Mangle(attribution.TypeName);
                if (key.Length > 0)
                    Accumulate(types, key, attribution, MstatCategory.Type);
            }
        }

        return new MstatRetentionPricer(methods, types);
    }

    /// <summary>
    /// Attempts to price a DGML node by its label. Method matches win over type matches; within a table the
    /// longest key the label ends with on a <c>_</c> boundary wins, and a tie at that length — or a key that
    /// several distinct managed identities mangle onto — is treated as unmatched (ambiguous) rather than guessed.
    /// </summary>
    public bool TryPrice(string? label, out MstatNodeCost cost)
    {
        cost = null!;
        if (string.IsNullOrEmpty(label))
            return false;

        // The label is a method node iff the method table matches it at all. An ambiguous method match
        // therefore means "this method node cannot be safely priced" — never fall back to a type key.
        switch (Match(_methodKeys, label, out cost))
        {
            case MatchOutcome.Matched:
                return true;
            case MatchOutcome.Ambiguous:
                cost = null!;
                return false;
        }

        return Match(_typeKeys, label, out cost) == MatchOutcome.Matched;
    }

    private static MatchOutcome Match(IReadOnlyDictionary<string, KeyBucket> keys, string label, out MstatNodeCost cost)
    {
        cost = null!;
        var bestLength = -1;
        var tie = false;
        KeyBucket? best = null;

        foreach (var (key, value) in keys)
        {
            if (!EndsOnBoundary(label, key))
                continue;

            if (key.Length > bestLength)
            {
                bestLength = key.Length;
                best = value;
                tie = false;
            }
            else if (key.Length == bestLength)
            {
                tie = true;
            }
        }

        if (best is null)
            return MatchOutcome.None;

        if (tie || best.Ambiguous)
            return MatchOutcome.Ambiguous;

        cost = best.ToCost();
        return MatchOutcome.Matched;
    }

    private static bool EndsOnBoundary(string label, string key) =>
        label.Length >= key.Length
        && label.EndsWith(key, StringComparison.Ordinal)
        && (label.Length == key.Length || label[label.Length - key.Length - 1] == '_');

    private static void Accumulate(Dictionary<string, KeyBucket> map, string key, MstatAttribution attribution, string matchKind)
    {
        if (map.TryGetValue(key, out var existing))
        {
            existing.Add(attribution);
        }
        else
        {
            map[key] = new KeyBucket(attribution, matchKind);
        }
    }

    private static string Mangle(string typeName) =>
        typeName.Replace('.', '_').Replace('+', '_');

    private enum MatchOutcome
    {
        None,
        Matched,
        Ambiguous,
    }

    private sealed class KeyBucket
    {
        private readonly string _assemblyName;
        private readonly string _typeName;
        private readonly string? _methodName;
        private readonly string _matchKind;

        public KeyBucket(MstatAttribution attribution, string matchKind)
        {
            _assemblyName = attribution.AssemblyName;
            _typeName = attribution.TypeName;
            _methodName = matchKind == MstatCategory.Method ? attribution.MethodName : null;
            _matchKind = matchKind;
            SizeBytes = attribution.TotalSize;
            AttributionCount = 1;
        }

        public long SizeBytes { get; private set; }

        public int AttributionCount { get; private set; }

        /// <summary>True when distinct managed identities mangle onto this key (e.g. a nested type and a
        /// dotted type, or the same type/method in two assemblies) — the size would mix unrelated entities,
        /// so the key is not safe to attribute to any single node.</summary>
        public bool Ambiguous { get; private set; }

        public void Add(MstatAttribution attribution)
        {
            // Overloads the linker mangles distinctly but mstat records under one method name share the same
            // (assembly, type, method) identity and legitimately aggregate; anything else is a mangle collision.
            if (!string.Equals(_assemblyName, attribution.AssemblyName, StringComparison.Ordinal)
                || !string.Equals(_typeName, attribution.TypeName, StringComparison.Ordinal)
                || !string.Equals(_methodName, _matchKind == MstatCategory.Method ? attribution.MethodName : null, StringComparison.Ordinal))
            {
                Ambiguous = true;
            }

            SizeBytes += attribution.TotalSize;
            AttributionCount++;
        }

        public MstatNodeCost ToCost() =>
            new(SizeBytes, AttributionCount, _matchKind, _assemblyName, _typeName, _methodName);
    }
}
