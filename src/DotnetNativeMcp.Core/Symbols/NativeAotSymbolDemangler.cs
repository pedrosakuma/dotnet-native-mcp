using System.Globalization;
using System.Text;

namespace DotnetNativeMcp.Core.Symbols;

/// <summary>
/// Best-effort pretty-printer for NativeAOT-mangled ELF symbol names emitted by the
/// <c>ilc</c> compiler. When a NativeAOT app is published with
/// <c>&lt;StripSymbols&gt;false&lt;/StripSymbols&gt;</c> (the diagnostics-friendly opt-in),
/// <c>perf</c> reports stack frames using the mangled symbol name embedded in the ELF
/// <c>.symtab</c>. Those strings are accurate but long and hard to read.
/// This class turns them into something closer to a managed display name.
/// </summary>
/// <remarks>
/// <para>
/// The NativeAOT name mangling rules (per <c>NameMangler</c> in dotnet/runtime) double
/// every literal <c>_</c> in a managed identifier so the resulting string still uses single
/// underscores as the separator between mangled segments. We cannot perfectly invert that,
/// so the demangler is a best-effort cleanup, not a full inverse. The original mangled name
/// is always preserved next to the demangled form for debugging.
/// </para>
/// <para>Recognised shapes:</para>
/// <list type="bullet">
///   <item><description><c>S_P_CoreLib_System_Foo_Bar__Method</c> — System.Private.CoreLib types.</description></item>
///   <item><description><c>Microsoft_AspNetCore_Http_...__Method</c> — assembly + namespace + type + method.</description></item>
///   <item><description><c>&lt;Boxed&gt;X__&lt;unbox&gt;X__Method</c> — boxed unbox stubs.</description></item>
///   <item><description><c>unbox_X__Method</c> — explicit unbox shims.</description></item>
///   <item><description><c>X&lt;T1__T2&gt;__Method&lt;T3&gt;</c> — generics.</description></item>
/// </list>
/// </remarks>
public static class NativeAotSymbolDemangler
{
    /// <summary>
    /// Returns a human-readable display name for a NativeAOT mangled symbol.
    /// When the symbol does not match any known shape the original string is returned unchanged.
    /// Always idempotent.
    /// </summary>
    public static string Demangle(string? mangled)
    {
        if (string.IsNullOrEmpty(mangled)) return mangled ?? string.Empty;

        if (!LooksLikeNativeAotMangled(mangled)) return mangled;

        if (mangled.StartsWith("<Boxed>", StringComparison.Ordinal))
        {
            var unboxIdx = mangled.IndexOf("__<unbox>", StringComparison.Ordinal);
            if (unboxIdx > 0)
            {
                var boxedHalf = mangled["<Boxed>".Length..unboxIdx];
                var tail = mangled[(unboxIdx + "__<unbox>".Length)..];
                var tailFirstDouble = IndexOfDoubleUnderscore(tail, 0);
                var unboxTypeHalf = tailFirstDouble >= 0 ? tail[..tailFirstDouble] : tail;
                if (!string.Equals(boxedHalf, unboxTypeHalf, StringComparison.Ordinal))
                {
                    return Demangle(boxedHalf) + " → " + Demangle(tail) + " (boxed)";
                }
                return Demangle(tail) + " (boxed)";
            }
        }

        if (mangled.StartsWith("unbox_", StringComparison.Ordinal))
        {
            return Demangle(mangled["unbox_".Length..]) + " (unbox)";
        }

        if (mangled.StartsWith("S_P_CoreLib_", StringComparison.Ordinal))
        {
            return "System.Private.CoreLib." + DemangleCore(mangled["S_P_CoreLib_".Length..]);
        }

        return DemangleCore(mangled);
    }

    private static string DemangleCore(string mangled)
    {
        if (mangled.IndexOf('<') < 0)
        {
            return PrettifyDottedSegments(mangled);
        }

        var sb = new StringBuilder(mangled.Length);
        var i = 0;
        var lastPlainStart = 0;
        while (i < mangled.Length)
        {
            var c = mangled[i];
            if (c == '<')
            {
                sb.Append(PrettifyDottedSegments(mangled[lastPlainStart..i]));
                var depth = 1;
                var inner = new StringBuilder();
                i++;
                while (i < mangled.Length && depth > 0)
                {
                    var ch = mangled[i];
                    if (ch == '<') depth++;
                    else if (ch == '>') { depth--; if (depth == 0) break; }
                    inner.Append(ch);
                    i++;
                }
                sb.Append('<');
                sb.Append(SplitTypeArgs(inner.ToString()));
                sb.Append('>');
                if (i < mangled.Length) i++;
                lastPlainStart = i;
            }
            else
            {
                i++;
            }
        }
        sb.Append(PrettifyDottedSegments(mangled[lastPlainStart..]));
        return sb.ToString();
    }

    private static string SplitTypeArgs(string inner)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (depth == 0 && i + 1 < inner.Length && c == '_' && inner[i + 1] == '_')
            {
                parts.Add(inner[start..i]);
                i++;
                start = i + 1;
            }
        }
        parts.Add(inner[start..]);
        return string.Join(", ", parts.Select(Demangle));
    }

    private static string PrettifyDottedSegments(string segment)
    {
        if (segment.Length == 0) return segment;

        var firstDouble = IndexOfDoubleUnderscore(segment, 0);
        if (firstDouble < 0)
        {
            return ConvertSegmentUnderscoresToDots(segment);
        }

        var typePart = segment[..firstDouble];
        var afterFirst = segment[(firstDouble + 2)..];

        var secondDouble = IndexOfDoubleUnderscore(afterFirst, 0);
        string methodPart;
        string? trailing = null;
        if (secondDouble >= 0)
        {
            methodPart = afterFirst[..secondDouble];
            trailing = afterFirst[(secondDouble + 2)..];
        }
        else
        {
            methodPart = afterFirst;
        }

        var fqType = ConvertSegmentUnderscoresToDots(typePart);
        return trailing is null
            ? fqType + "." + methodPart
            : fqType + "." + methodPart + " [" + trailing + "]";
    }

    private static int IndexOfDoubleUnderscore(string s, int start)
    {
        for (var i = start; i + 1 < s.Length; i++)
        {
            if (s[i] == '_' && s[i + 1] == '_') return i;
        }
        return -1;
    }

    private static string ConvertSegmentUnderscoresToDots(string segment)
    {
        var parts = segment.Split('_');
        var sb = new StringBuilder(segment.Length);
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            if (sb.Length > 0)
            {
                if (IsAllDigits(parts[i]))
                {
                    sb.Append('`').Append(parts[i]);
                    continue;
                }
                sb.Append('.');
            }
            sb.Append(parts[i]);
        }
        return sb.ToString();
    }

    private static bool IsAllDigits(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (!char.IsDigit(s[i])) return false;
        }
        return s.Length > 0;
    }

    private static bool IsKnownNativeSymbol(string s)
    {
        if (s.StartsWith("CryptoNative_", StringComparison.Ordinal) ||
            s.StartsWith("SystemNative_", StringComparison.Ordinal) ||
            s.StartsWith("GlobalizationNative_", StringComparison.Ordinal) ||
            s.StartsWith("CompressionNative_", StringComparison.Ordinal) ||
            s.StartsWith("HttpNative_", StringComparison.Ordinal) ||
            s.StartsWith("NetSecurityNative_", StringComparison.Ordinal))
        {
            return true;
        }

        if (s.StartsWith("__libc_", StringComparison.Ordinal) ||
            s.StartsWith("__GI_", StringComparison.Ordinal) ||
            s.StartsWith("[k", StringComparison.Ordinal) ||
            s == "[unknown]")
        {
            return true;
        }

        if (s.StartsWith("_Z", StringComparison.Ordinal)) return true;

        return false;
    }

    /// <summary>True when the symbol carries positive evidence of NativeAOT mangling.</summary>
    public static bool LooksLikeNativeAotMangled(string s)
    {
        if (IsKnownNativeSymbol(s)) return false;
        return s.StartsWith("S_P_", StringComparison.Ordinal)
            || s.StartsWith("<Boxed>", StringComparison.Ordinal)
            || s.StartsWith("unbox_", StringComparison.Ordinal)
            || s.Contains("__", StringComparison.Ordinal);
    }

    /// <summary>Provenance of a frame's display name.</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<SymbolSource>))]
    public enum SymbolSource
    {
        /// <summary>Unknown provenance.</summary>
        Unknown = 0,
        /// <summary>Came from ELF symbol resolution and looked managed-mangled.</summary>
        ElfMangled,
        /// <summary>Same as ElfMangled but ran through <see cref="Demangle"/>.</summary>
        ElfDemangled,
        /// <summary>Came from a non-managed (libc / P/Invoke / kernel) frame.</summary>
        Native,
        /// <summary>Synthetic / stripped — returned <c>[unknown]</c> or an address.</summary>
        Stripped,
        /// <summary>Resolved via Windows PDB / DIA.</summary>
        PdbResolved,
        /// <summary>Trace mixed multiple distinct sources.</summary>
        Mixed,
    }

    /// <summary>Classifies a symbol so the artifact can carry a coarse source label.</summary>
    public static SymbolSource Classify(string? symbol)
    {
        if (string.IsNullOrEmpty(symbol)) return SymbolSource.Stripped;
        if (symbol == "[unknown]" || symbol.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return SymbolSource.Stripped;
        if (IsKnownNativeSymbol(symbol)) return SymbolSource.Native;
        if (LooksLikeNativeAotMangled(symbol)) return SymbolSource.ElfMangled;
        if (symbol.Contains('.', StringComparison.Ordinal)) return SymbolSource.ElfDemangled;
        return SymbolSource.Native;
    }

    /// <summary>Combines two source labels for trace-level rollup.</summary>
    public static SymbolSource Combine(SymbolSource a, SymbolSource b)
    {
        if (a == SymbolSource.Unknown) return b;
        if (b == SymbolSource.Unknown) return a;
        if (a == b) return a;
        if ((a == SymbolSource.ElfMangled && b == SymbolSource.ElfDemangled) ||
            (a == SymbolSource.ElfDemangled && b == SymbolSource.ElfMangled))
        {
            return SymbolSource.ElfDemangled;
        }
        return SymbolSource.Mixed;
    }

    internal static string FormatHex(long value) => value.ToString("x", CultureInfo.InvariantCulture);
}
