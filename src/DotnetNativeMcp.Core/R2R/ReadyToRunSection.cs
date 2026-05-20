namespace DotnetNativeMcp.Core.R2R;

/// <summary>One entry in the ReadyToRun section table.</summary>
/// <param name="Type">Numeric section type (see <see cref="ReadyToRunSectionType"/>).</param>
/// <param name="TypeName">Human-readable name, or the raw numeric value when unknown.</param>
/// <param name="VirtualAddress">RVA of the section data within the image.</param>
/// <param name="Size">Byte size of the section data.</param>
public sealed record ReadyToRunSection(
    uint Type,
    string TypeName,
    uint VirtualAddress,
    uint Size);
