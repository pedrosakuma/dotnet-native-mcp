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

    /// <summary>Architecture not supported in this version (e.g. ARM64 pre-V1).</summary>
    public const string DisassemblyUnsupported = "disassembly_unsupported";

    /// <summary>A supplied argument value was invalid.</summary>
    public const string InvalidArgument = "invalid_argument";

    /// <summary>An unexpected internal failure occurred.</summary>
    public const string InternalError = "internal_error";
}
