namespace DotnetNativeMcp.Core.R2R;

/// <summary>
/// Known ReadyToRun section types.
/// Mirrors <c>ReadyToRunSectionType</c> in the .NET runtime
/// (<c>src/coreclr/inc/readytorun.h</c>). Every R2R section type is in the
/// 100+ range — there are no low-numbered section types.
/// </summary>
public enum ReadyToRunSectionType : uint
{
    /// <summary>Null-terminated compiler identification string.</summary>
    CompilerIdentifier = 100,

    /// <summary>Import sections table.</summary>
    ImportSections = 101,

    /// <summary>Array of RUNTIME_FUNCTION rows for all methods in the image.</summary>
    RuntimeFunctions = 102,

    /// <summary>Entry point table indexed by MethodDef token.</summary>
    MethodDefEntryPoints = 103,

    /// <summary>Per-method exception handling info.</summary>
    ExceptionInfo = 104,

    /// <summary>Debug information for methods.</summary>
    DebugInfo = 105,

    /// <summary>Thunks for delay-loaded method calls.</summary>
    DelayLoadMethodCallThunks = 106,

    // 107 used by an older format of AvailableTypes.

    /// <summary>Available types exported by this module.</summary>
    AvailableTypes = 108,

    /// <summary>Entry points for instance methods (generic instantiations).</summary>
    InstanceMethodEntryPoints = 109,

    /// <summary>Inlining info (version 1). Added in V2.1, deprecated in 4.1.</summary>
    InliningInfo = 110,

    /// <summary>Profile data information. Added in V2.2.</summary>
    ProfileDataInfo = 111,

    /// <summary>Manifest metadata blob. Added in V2.3.</summary>
    ManifestMetadata = 112,

    /// <summary>Presence map for custom attributes. Added in V3.1.</summary>
    AttributePresence = 113,

    /// <summary>Inlining info (version 2). Added in V4.1.</summary>
    InliningInfo2 = 114,

    /// <summary>Component assemblies (composite images). Added in V4.1.</summary>
    ComponentAssemblies = 115,

    /// <summary>Owner composite executable path (component images). Added in V4.1.</summary>
    OwnerCompositeExecutable = 116,

    /// <summary>PGO instrumentation data. Added in V5.2.</summary>
    PgoInstrumentationData = 117,

    /// <summary>MVIDs of manifest assemblies. Added in V5.3.</summary>
    ManifestAssemblyMvids = 118,

    /// <summary>Cross-module inline info. Added in V6.2.</summary>
    CrossModuleInlineInfo = 119,

    /// <summary>Hot/cold method mapping. Added in V8.0.</summary>
    HotColdMap = 120,

    /// <summary>Bitmap indicating which methods are generic. Added in V9.0.</summary>
    MethodIsGenericMap = 121,

    /// <summary>Map from type to enclosing type. Added in V9.0.</summary>
    EnclosingTypeMap = 122,

    /// <summary>Generic info per type. Added in V9.0.</summary>
    TypeGenericInfoMap = 123,

    /// <summary>External type maps. Added in V18.3.</summary>
    ExternalTypeMaps = 124,

    /// <summary>Proxy type maps. Added in V18.3.</summary>
    ProxyTypeMaps = 125,

    /// <summary>Type map assembly targets. Added in V18.3.</summary>
    TypeMapAssemblyTargets = 126,
}
