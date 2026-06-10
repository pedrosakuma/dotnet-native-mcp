namespace DotnetNativeMcp.Core.R2R;

/// <summary>
/// One entry in the R2R <c>ComponentAssemblies</c> section (type 115), present in composite
/// ReadyToRun images. Mirrors <c>READYTORUN_COMPONENT_ASSEMBLIES_ENTRY</c> in the .NET runtime
/// (<c>src/coreclr/inc/readytorun.h</c>): two <c>IMAGE_DATA_DIRECTORY</c> values describing the
/// component assembly's CLR COR header and its per-assembly ReadyToRun core header.
/// </summary>
/// <param name="Index">Zero-based index in the component-assemblies table.</param>
/// <param name="CorHeaderRva">RVA of the component assembly's COR (CLI) header.</param>
/// <param name="CorHeaderSize">Byte size of the COR header.</param>
/// <param name="AssemblyHeaderRva">RVA of the component assembly's ReadyToRun core header.</param>
/// <param name="AssemblyHeaderSize">Byte size of the ReadyToRun core header.</param>
public sealed record ReadyToRunComponentAssembly(
    int Index,
    uint CorHeaderRva,
    uint CorHeaderSize,
    uint AssemblyHeaderRva,
    uint AssemblyHeaderSize);
