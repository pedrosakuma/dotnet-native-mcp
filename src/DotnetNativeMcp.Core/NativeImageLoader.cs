namespace DotnetNativeMcp.Core;

/// <summary>
/// Scaffold placeholder. Real implementation lands in V0:
///   - Open PE/ELF via System.Reflection.PortableExecutable / a small ELF reader.
///   - Verify the binary is a managed-flavored native build (NativeAOT / R2R)
///     vs an arbitrary system .so/.dll; reject the latter explicitly.
///   - Return the ImageHandle the rest of the API hangs off (handles + addresses,
///     mirroring the ModuleVersionId + metadataToken model of dotnet-assembly-mcp).
/// </summary>
public static class NativeImageLoader
{
    public const string ScaffoldNotice =
        "dotnet-native-mcp is in scaffold phase. See docs/handoff-contract.md and the V0 issue.";
}
