namespace DotnetNativeMcp.Core.Errors;

/// <summary>Stable error kind strings emitted by all tools. Once published, never repurposed.</summary>
public static class ErrorKinds
{
    /// <summary>Path doesn't resolve on the consumer's host.</summary>
    public const string BinaryNotFound = "binary_not_found";

    /// <summary>buildId disagrees with on-disk binary.</summary>
    public const string BinaryMismatch = "binary_mismatch";

    /// <summary>The binary opened, but isn't a managed-flavored native build.</summary>
    public const string NotANativeDotnetImage = "not_a_native_dotnet_image";

    /// <summary>Symbol not in .map or .symtab.</summary>
    public const string SymbolNotFound = "symbol_not_found";

    /// <summary>Address is not inside any known section.</summary>
    public const string AddressOutOfRange = "address_out_of_range";

    /// <summary>No paired .mstat sidecar could be found.</summary>
    public const string MstatNotFound = "mstat_not_found";

    /// <summary>The .mstat sidecar exists and was opened, but its contents are not a parseable NativeAOT .mstat image (truncated, wrong format, unsupported version, bad table offsets, etc.).</summary>
    public const string MstatInvalid = "mstat_invalid";

    /// <summary>No paired .dgml sidecar could be found.</summary>
    public const string DgmlNotFound = "dgml_not_found";

    /// <summary>Architecture not supported in this version (e.g. ARM64 pre-V1).</summary>
    public const string DisassemblyUnsupported = "disassembly_unsupported";

    /// <summary>A supplied argument value was invalid.</summary>
    public const string InvalidArgument = "invalid_argument";

    /// <summary>An unexpected internal failure occurred.</summary>
    public const string InternalError = "internal_error";

    /// <summary>Build-id provided in an eager manifest import did not match the on-disk binary.</summary>
    public const string BuildIdMismatch = "build_id_mismatch";

    /// <summary>The Mach-O binary uses a feature not yet supported (e.g. 32-bit, chained fixups, bitcode).</summary>
    public const string MachoFeatureUnsupported = "macho_feature_unsupported";

    /// <summary>The binary does not contain a ReadyToRun header (not an R2R image, or a NativeAOT binary).</summary>
    public const string R2RNotPresent = "r2r_not_present";

    /// <summary>The R2R header version is not supported by this tool version.</summary>
    public const string R2RUnsupportedVersion = "r2r_unsupported_version";

    /// <summary>The target architecture is not supported for the requested R2R operation (e.g. ARM64 RuntimeFunctions in v1).</summary>
    public const string R2RArchUnsupported = "r2r_arch_unsupported";

    /// <summary>The requested R2R section type is not present in this image.</summary>
    public const string R2RSectionNotPresent = "r2r_section_not_present";

    /// <summary>rawBlob=true requires 'architecture' because there is no header to infer it from.</summary>
    public const string RawBlobMissingArchitecture = "raw_blob_missing_architecture";

    /// <summary>rawBlob=true requires 'baseAddress' so that call/jmp absolute targets render correctly.</summary>
    public const string RawBlobMissingBaseAddress = "raw_blob_missing_base_address";

    /// <summary>rawBlob=true requires 'size' because there is no section table to bound the code slice.</summary>
    public const string RawBlobMissingSize = "raw_blob_missing_size";
}
