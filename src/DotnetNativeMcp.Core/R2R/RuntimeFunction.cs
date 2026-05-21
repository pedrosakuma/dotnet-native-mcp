namespace DotnetNativeMcp.Core.R2R;

/// <summary>
/// One row in the R2R <c>RuntimeFunctions</c> section.
/// </summary>
/// <param name="Index">Zero-based index in the table.</param>
/// <param name="BeginAddress">RVA of the first instruction of the function.</param>
/// <param name="EndAddress">RVA one byte past the last instruction.</param>
/// <param name="UnwindInfoAddress">x64 unwind-info RVA, or the raw ARM64 unwind word / xdata RVA.</param>
public sealed record RuntimeFunction(
    int Index,
    uint BeginAddress,
    uint EndAddress,
    uint UnwindInfoAddress);
