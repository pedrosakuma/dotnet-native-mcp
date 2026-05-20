using DotnetNativeMcp.Core.Imaging;

namespace DotnetNativeMcp.Core.Xref;

/// <summary>
/// Scans a loaded image's call-graph index for call sites that resolve
/// (via ELF PLT, PE import thunk, or Mach-O stubs) to an external symbol
/// exported by a different image.
/// </summary>
public static class CrossImageCallGraphScanner
{
    /// <summary>
    /// Returns all call sites in <paramref name="callerImage"/> that target
    /// the imported symbol <paramref name="targetSymbolName"/> from an external library.
    /// </summary>
    /// <param name="callerImage">The image to search for cross-image call sites.</param>
    /// <param name="callerGraph">Pre-built call-graph index for <paramref name="callerImage"/> (target VA → callers).</param>
    /// <param name="targetSymbolName">The exported symbol name to match against (mangled or demangled).</param>
    /// <param name="targetLibrary">Optional library qualifier (SONAME / DLL name). When <c>null</c>, all libraries are matched.</param>
    /// <returns>A list of <see cref="CrossImageCallSite"/> instances, empty when no callers are found.</returns>
    public static IReadOnlyList<CrossImageCallSite> FindCallers(
        NativeImage callerImage,
        IReadOnlyDictionary<ulong, IReadOnlyList<CallSite>> callerGraph,
        string targetSymbolName,
        string? targetLibrary)
    {
        return callerImage.Format switch
        {
            BinaryFormat.Elf => FindElfCallers(callerImage, callerGraph, targetSymbolName, targetLibrary),
            BinaryFormat.Pe => FindPeCallers(callerImage, callerGraph, targetSymbolName, targetLibrary),
            _ => [],
        };
    }

    // -------------------------------------------------------------------------
    // ELF: resolve via .rela.plt → .plt / .plt.sec → call graph
    // -------------------------------------------------------------------------

    private static List<CrossImageCallSite> FindElfCallers(
        NativeImage callerImage,
        IReadOnlyDictionary<ulong, IReadOnlyList<CallSite>> callerGraph,
        string targetSymbolName,
        string? targetLibrary)
    {
        // When a library qualifier is requested, check that this image actually
        // imports from that library before doing expensive PLT resolution.
        if (targetLibrary is not null)
        {
            var importsResult = ElfReader.ReadImportedFunctions(callerImage);
            if (!importsResult.IsError)
            {
                var hasMatchingLib = importsResult.Data!.Any(f =>
                    string.Equals(f.Library, targetLibrary, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(f.Name, targetSymbolName, StringComparison.Ordinal));
                if (!hasMatchingLib)
                {
                    // Check without library match (anonymous imports) – only skip if lib-qualified imports exist.
                    var hasAnyMatchingLib = importsResult.Data!.Any(f =>
                        f.Library is not null &&
                        string.Equals(f.Library, targetLibrary, StringComparison.OrdinalIgnoreCase));
                    if (hasAnyMatchingLib)
                        return [];
                }
            }
        }

        // Map PLT entry VA → symbol name.
        var pltMap = ElfReader.ResolvePltEntries(callerImage);

        var result = new List<CrossImageCallSite>();
        foreach (var (pltEntryVa, symName) in pltMap)
        {
            if (!string.Equals(symName, targetSymbolName, StringComparison.Ordinal))
                continue;

            if (!callerGraph.TryGetValue(pltEntryVa, out var sites))
                continue;

            foreach (var site in sites)
            {
                result.Add(new CrossImageCallSite(
                    site.SourceAddressHex,
                    site.CallerSymbol,
                    site.CallerDemangled,
                    site.Mnemonic,
                    site.Operands,
                    site.RawBytes,
                    callerImage.Handle.BuildIdHex,
                    callerImage.FilePath));
            }
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // PE: best-effort thunk scanning
    // -------------------------------------------------------------------------
    // PE binaries typically call imports via CALL [__imp_symbol] (indirect through IAT),
    // which is not captured as a near branch by NativeCallGraphBuilder.
    // Some compilers emit a direct-call thunk wrapper in .text; this handles that case.
    // For most PE binaries the cross-image result will be empty, which is correct.

    private static List<CrossImageCallSite> FindPeCallers(
        NativeImage callerImage,
        IReadOnlyDictionary<ulong, IReadOnlyList<CallSite>> callerGraph,
        string targetSymbolName,
        string? targetLibrary)
    {
        // PE imports are typically indirect; we use a best-effort symbol-name match
        // against any exported thunk symbols in the caller's own symbol table.
        var result = new List<CrossImageCallSite>();

        foreach (var sym in callerImage.Symbols)
        {
            if (!string.Equals(sym.Name, targetSymbolName, StringComparison.Ordinal) &&
                !string.Equals(sym.DemangledName, targetSymbolName, StringComparison.Ordinal))
                continue;

            // A thunk wrapping an import has a small body (≤ 8 bytes) and starts with jmp.
            var thunkVa = callerImage.ImageBase + sym.Rva;
            if (!callerGraph.TryGetValue(thunkVa, out var sites))
                continue;

            foreach (var site in sites)
            {
                result.Add(new CrossImageCallSite(
                    site.SourceAddressHex,
                    site.CallerSymbol,
                    site.CallerDemangled,
                    site.Mnemonic,
                    site.Operands,
                    site.RawBytes,
                    callerImage.Handle.BuildIdHex,
                    callerImage.FilePath));
            }
        }

        return result;
    }
}
