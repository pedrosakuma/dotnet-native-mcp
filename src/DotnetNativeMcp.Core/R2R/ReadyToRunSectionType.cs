namespace DotnetNativeMcp.Core.R2R;

/// <summary>
/// Known ReadyToRun section types.
/// Mirrors <c>ReadyToRunSectionType</c> in the .NET runtime
/// (coreclr/inc/readytorun.h).
/// </summary>
public enum ReadyToRunSectionType : uint
{
    /// <summary>Compiler flags used to produce this image.</summary>
    CompilerFlags = 1,

    /// <summary>Available types exported by this module.</summary>
    AvailableTypes = 2,

    /// <summary>Entry points for instance methods (generic instantiations).</summary>
    InstanceMethodEntryPoints = 3,

    /// <summary>Array of RUNTIME_FUNCTION rows for all methods in the image.</summary>
    RuntimeFunctions = 5,

    /// <summary>Entry point table indexed by MethodDef token.</summary>
    MethodDefEntryPoints = 6,

    /// <summary>Per-method exception handling info.</summary>
    ExceptionInfo = 7,

    /// <summary>DWARF-like debug information for methods.</summary>
    DebugInfo = 9,

    /// <summary>Thunks for delay-loaded method calls.</summary>
    DelayLoadMethodCallThunks = 10,

    /// <summary>Hash index of available types (R2R 5.0+).</summary>
    AvailableTypesHash = 11,

    /// <summary>Hash index of instance method entry points (R2R 5.0+).</summary>
    InstanceMethodEntryPointsHash = 12,

    /// <summary>Inlining info (version 1).</summary>
    InliningInfo = 13,

    /// <summary>Profile data information.</summary>
    ProfileDataInfo = 14,

    /// <summary>Manifest metadata blob.</summary>
    ManifestMetadata = 15,

    /// <summary>Presence map for custom attributes.</summary>
    AttributePresence = 16,

    /// <summary>Inlining info (version 2).</summary>
    InliningInfo2 = 17,

    /// <summary>Component assemblies (composite images).</summary>
    ComponentAssemblies = 18,

    /// <summary>Owner composite executable path (component images).</summary>
    OwnerCompositeExecutable = 19,

    /// <summary>PGO instrumentation data.</summary>
    PgoInstrumentationData = 20,

    /// <summary>MVIDs of manifest assemblies.</summary>
    ManifestAssemblyMvids = 21,

    /// <summary>Cross-module inline info.</summary>
    CrossModuleInlineInfo = 22,

    /// <summary>Hot/cold method mapping.</summary>
    HotColdMap = 23,

    /// <summary>Bitmap indicating which methods are generic.</summary>
    MethodIsGenericMap = 24,

    /// <summary>Map from type to enclosing type.</summary>
    EnclosingTypeMap = 25,

    /// <summary>Generic info per type.</summary>
    TypeGenericInfoMap = 26,

    // ---- CoreCLR-specific (>= 100) ----

    /// <summary>Null-terminated compiler identification string.</summary>
    CompilerIdentifier = 100,

    /// <summary>Import sections table.</summary>
    ImportSections = 101,

    /// <summary>Delay-load import sections table.</summary>
    DelayLoadImportSections = 102,

    /// <summary>ReadyToRun options record.</summary>
    ReadyToRunOptions = 103,

    /// <summary>Module information record.</summary>
    ModuleInfo = 104,

    /// <summary>Per-method header and code info (replaces RuntimeFunctions in newer versions).</summary>
    MethodHeaderAndCodeInfo = 105,

    /// <summary>L2P (lookup-to-pointer) table.</summary>
    L2PTable = 106,

    /// <summary>Type layouts for method type parameters.</summary>
    MethodTypeLayouts = 107,

    /// <summary>Type dependency information.</summary>
    TypeDependencies = 108,

    /// <summary>Preserved IL bodies.</summary>
    ILBodyPreservation = 109,

    /// <summary>Instrumentation data.</summary>
    InstrumentationData = 110,

    /// <summary>Manifest hash.</summary>
    ManifestHash = 140,
}
