namespace DotnetNativeMcp.Core.R2R;

/// <summary>
/// One row in the R2R <c>RuntimeFunctions</c> section (x64 layout).
/// </summary>
/// <param name="Index">Zero-based index in the table.</param>
/// <param name="BeginAddress">RVA of the first instruction of the function.</param>
/// <param name="EndAddress">RVA one byte past the last instruction.</param>
/// <param name="UnwindInfoAddress">RVA of the unwind info record.</param>
public sealed record RuntimeFunction(
    int Index,
    uint BeginAddress,
    uint EndAddress,
    uint UnwindInfoAddress);
