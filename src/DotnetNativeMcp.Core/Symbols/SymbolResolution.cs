using DotnetNativeMcp.Core.Imaging;

namespace DotnetNativeMcp.Core.Symbols;

/// <summary>
/// Address-based and name-based symbol resolution over a sorted symbol table.
/// </summary>
public static class SymbolResolution
{
    /// <summary>Looks up a symbol by exact name (case-sensitive).</summary>
    public static NativeSymbol? FindByName(IReadOnlyList<NativeSymbol> symbols, string name)
    {
        foreach (var sym in symbols)
        {
            if (sym.Name == name || sym.DemangledName == name)
                return sym;
        }
        return null;
    }

    /// <summary>
    /// Finds the symbol whose RVA range contains <paramref name="rva"/>.
    /// Falls back to the nearest symbol whose RVA is ≤ <paramref name="rva"/> when
    /// size information is unavailable (size == 0).
    /// Returns <c>null</c> when no symbol can be associated with the address.
    /// </summary>
    public static NativeSymbol? FindByRva(IReadOnlyList<NativeSymbol> symbols, ulong rva)
    {
        // Build or use a sorted view.
        var sorted = symbols.OrderBy(s => s.Rva).ToList();

        // Binary search for the last symbol with RVA ≤ rva.
        int lo = 0, hi = sorted.Count - 1, best = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (sorted[mid].Rva <= rva)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (best < 0) return null;
        var candidate = sorted[best];

        // If we have a size, check the range.
        if (candidate.Size > 0 && rva >= candidate.Rva + candidate.Size)
        {
            // Outside the known range; return the nearest but caller can verify.
            return null;
        }

        return candidate;
    }

    /// <summary>
    /// Translates an absolute virtual address to an RVA using <paramref name="imageBase"/>.
    /// </summary>
    public static ulong VaToRva(ulong virtualAddress, ulong imageBase) =>
        virtualAddress >= imageBase ? virtualAddress - imageBase : virtualAddress;
}
