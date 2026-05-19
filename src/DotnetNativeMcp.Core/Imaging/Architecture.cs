namespace DotnetNativeMcp.Core.Imaging;

/// <summary>CPU architecture of the native binary.</summary>
public enum Architecture
{
    /// <summary>Architecture could not be determined.</summary>
    Unknown = 0,

    /// <summary>x86 (32-bit).</summary>
    X86 = 1,

    /// <summary>x86-64 (64-bit).</summary>
    X64 = 2,

    /// <summary>ARM64 / AArch64.</summary>
    Arm64 = 3,
}
