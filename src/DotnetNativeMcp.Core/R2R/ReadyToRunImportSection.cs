using System.Globalization;

namespace DotnetNativeMcp.Core.R2R;

/// <summary>
/// ReadyToRun import-section types.
/// Mirrors <c>ReadyToRunImportSectionType</c> in the .NET runtime
/// (<c>src/coreclr/inc/readytorun.h</c>).
/// </summary>
public enum ReadyToRunImportSectionType : byte
{
    /// <summary>Generic / unspecified import section.</summary>
    Unknown = 0,

    /// <summary>Virtual stub dispatch cells.</summary>
    StubDispatch = 2,

    /// <summary>String literal handles.</summary>
    StringHandle = 3,

    /// <summary>IL body fixups.</summary>
    ILBodyFixups = 7,
}

/// <summary>
/// ReadyToRun import-section flags (<c>ReadyToRunImportSectionFlags</c>).
/// Mirrors the runtime enum in <c>src/coreclr/inc/readytorun.h</c>.
/// Named with the BCL flags-enum convention (cf. <c>TypeAttributes</c>).
/// </summary>
[Flags]
public enum ReadyToRunImportSectionAttributes : ushort
{
    /// <summary>No flags set.</summary>
    None = 0x0000,

    /// <summary>The section is fixed up eagerly at module load time.</summary>
    Eager = 0x0001,

    /// <summary>The section contains pointers to code.</summary>
    PCode = 0x0004,
}

/// <summary>Decoding helpers for ReadyToRun import-section metadata.</summary>
public static class ReadyToRunImportSectionDecoder
{
    private static readonly ReadyToRunImportSectionAttributes[] KnownFlags =
    [
        ReadyToRunImportSectionAttributes.Eager,
        ReadyToRunImportSectionAttributes.PCode,
    ];

    /// <summary>
    /// Returns the human-readable name of an import-section <paramref name="type"/>,
    /// or the raw numeric value when the type is unknown.
    /// </summary>
    public static string TypeName(byte type) =>
        Enum.IsDefined(typeof(ReadyToRunImportSectionType), type)
            ? ((ReadyToRunImportSectionType)type).ToString()
            : type.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Decodes a raw import-section flags value into the names of the individual set bits.
    /// Any bits not covered by a known flag are reported as a single <c>Unknown(0x...)</c>
    /// entry so callers never silently lose information.
    /// </summary>
    public static IReadOnlyList<string> DecodeFlagNames(ushort flags)
    {
        var names = new List<string>();
        uint remaining = flags;

        foreach (var known in KnownFlags)
        {
            if ((flags & (ushort)known) != 0)
            {
                names.Add(known.ToString());
                remaining &= ~(uint)known;
            }
        }

        if (remaining != 0)
            names.Add($"Unknown(0x{remaining.ToString("X4", CultureInfo.InvariantCulture)})");

        return names;
    }
}

/// <summary>
/// One entry in the R2R <c>ImportSections</c> section (type 101).
/// Describes an image range containing references (fixups) to code or
/// runtime data structures. Mirrors <c>READYTORUN_IMPORT_SECTION</c>.
/// </summary>
/// <param name="Index">Zero-based index in the import-sections table.</param>
/// <param name="SectionRva">RVA of the fixup region this entry describes.</param>
/// <param name="SectionSize">Byte size of the fixup region.</param>
/// <param name="Flags">Raw <c>ReadyToRunImportSectionFlags</c> value.</param>
/// <param name="Type">Raw <c>ReadyToRunImportSectionType</c> value.</param>
/// <param name="EntrySize">Size, in bytes, of one fixup cell within the region.</param>
/// <param name="SignaturesRva">RVA of the optional signature descriptors (0 when absent).</param>
/// <param name="AuxiliaryDataRva">RVA of the optional auxiliary data, typically GC info (0 when absent).</param>
public sealed record ReadyToRunImportSection(
    int Index,
    uint SectionRva,
    uint SectionSize,
    ushort Flags,
    byte Type,
    byte EntrySize,
    uint SignaturesRva,
    uint AuxiliaryDataRva);
