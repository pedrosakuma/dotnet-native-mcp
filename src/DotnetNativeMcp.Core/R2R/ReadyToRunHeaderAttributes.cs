using System.Globalization;

namespace DotnetNativeMcp.Core.R2R;

/// <summary>
/// ReadyToRun header flags (<c>READYTORUN_FLAG_*</c>).
/// Mirrors <c>ReadyToRunFlag</c> in the .NET runtime
/// (<c>src/coreclr/inc/readytorun.h</c>).
/// </summary>
[Flags]
public enum ReadyToRunHeaderAttributes : uint
{
    /// <summary>No flags set.</summary>
    None = 0,

    /// <summary>The original IL assembly was platform-neutral.</summary>
    PlatformNeutralSource = 0x00000001,

    /// <summary>Type validation was skipped (set of methods determined via profile data).</summary>
    SkipTypeValidation = 0x00000002,

    /// <summary>Partial image — not all methods have native code.</summary>
    Partial = 0x00000004,

    /// <summary>PInvoke stubs compiled into the image are non-shareable (no secret parameter).</summary>
    NonSharedPInvokeStubs = 0x00000008,

    /// <summary>MSIL is embedded in the composite R2R executable.</summary>
    EmbeddedMsil = 0x00000010,

    /// <summary>This header describes a component assembly of a composite R2R image.</summary>
    Component = 0x00000020,

    /// <summary>This R2R module has multiple modules within its version bubble.</summary>
    MultiModuleVersionBubble = 0x00000040,

    /// <summary>This R2R module contains code that would not naturally be encoded into it.</summary>
    UnrelatedR2RCode = 0x00000080,

    /// <summary>The owning composite executable is in the platform native format.</summary>
    PlatformNativeImage = 0x00000100,

    /// <summary>IL method bodies have been stripped from the image.</summary>
    StrippedIlBodies = 0x00000200,

    /// <summary>Inlining info has been stripped from the image.</summary>
    StrippedInliningInfo = 0x00000400,

    /// <summary>Debug info has been stripped from the image.</summary>
    StrippedDebugInfo = 0x00000800,
}

/// <summary>Decoding helpers for <see cref="ReadyToRunHeaderAttributes"/>.</summary>
public static class ReadyToRunHeaderAttributesExtensions
{
    private static readonly ReadyToRunHeaderAttributes[] KnownFlags =
    [
        ReadyToRunHeaderAttributes.PlatformNeutralSource,
        ReadyToRunHeaderAttributes.SkipTypeValidation,
        ReadyToRunHeaderAttributes.Partial,
        ReadyToRunHeaderAttributes.NonSharedPInvokeStubs,
        ReadyToRunHeaderAttributes.EmbeddedMsil,
        ReadyToRunHeaderAttributes.Component,
        ReadyToRunHeaderAttributes.MultiModuleVersionBubble,
        ReadyToRunHeaderAttributes.UnrelatedR2RCode,
        ReadyToRunHeaderAttributes.PlatformNativeImage,
        ReadyToRunHeaderAttributes.StrippedIlBodies,
        ReadyToRunHeaderAttributes.StrippedInliningInfo,
        ReadyToRunHeaderAttributes.StrippedDebugInfo,
    ];

    /// <summary>
    /// Decodes a raw R2R header flags value into the names of the individual set bits.
    /// Any bits not covered by a known flag are reported as a single
    /// <c>Unknown(0x...)</c> entry so callers never silently lose information.
    /// </summary>
    public static IReadOnlyList<string> DecodeNames(uint flags)
    {
        var names = new List<string>();
        uint remaining = flags;

        foreach (var known in KnownFlags)
        {
            if ((flags & (uint)known) != 0)
            {
                names.Add(known.ToString());
                remaining &= ~(uint)known;
            }
        }

        if (remaining != 0)
            names.Add($"Unknown(0x{remaining.ToString("X8", CultureInfo.InvariantCulture)})");

        return names;
    }
}
