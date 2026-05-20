namespace DotnetNativeMcp.Core.R2R;

/// <summary>
/// Parsed <c>READYTORUN_HEADER</c> from a managed PE binary.
/// </summary>
/// <param name="MajorVersion">R2R format major version.</param>
/// <param name="MinorVersion">R2R format minor version.</param>
/// <param name="Flags">Raw header flags (<c>READYTORUN_FLAG_*</c>).</param>
/// <param name="Sections">All section entries in declaration order.</param>
public sealed record ReadyToRunHeader(
    ushort MajorVersion,
    ushort MinorVersion,
    uint Flags,
    IReadOnlyList<ReadyToRunSection> Sections)
{
    /// <summary>Human-readable version string (e.g. <c>"16.0"</c>).</summary>
    public string Version => $"{MajorVersion}.{MinorVersion}";

    /// <summary>
    /// Returns the section with the given type, or <c>null</c> if not present.
    /// </summary>
    public ReadyToRunSection? FindSection(ReadyToRunSectionType type)
    {
        var typeId = (uint)type;
        foreach (var sec in Sections)
        {
            if (sec.Type == typeId)
                return sec;
        }
        return null;
    }
}
