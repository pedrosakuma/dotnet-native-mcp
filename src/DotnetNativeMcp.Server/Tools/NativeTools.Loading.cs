using System.ComponentModel;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using ModelContextProtocol.Server;

namespace DotnetNativeMcp.Server.Tools;

public sealed partial class NativeTools
{
    [McpServerTool(Name = "load_native_binary")]
    [Description(
        "Opens a PE or ELF native binary, verifies it is a managed-flavored native build " +
        "(NativeAOT or ReadyToRun), and returns an ImageHandle used by all other tools. " +
        "Rejects arbitrary system .so/.dll files with 'not_a_native_dotnet_image'. " +
        "Optionally validates the build-id against a value from dotnet-diagnostics-mcp " +
        "to prevent stale-binary mistakes.")]
    public NativeResult<LoadNativeBinaryResult> LoadNativeBinary(
        [Description("Absolute path to the native binary on disk.")] string path,
        [Description("Optional build-id (hex) from dotnet-diagnostics-mcp NativeFrame.buildId. When supplied, the loaded binary's build-id must match or binary_mismatch is returned.")] string? buildId = null)
    {
        var result = registry.Load(path, buildId);
        if (result.IsError)
            return NativeResult.Fail<LoadNativeBinaryResult>(result.Error!.Kind, result.Error.Message, result.Error.Detail);

        var image = result.Data!;
        var data = new LoadNativeBinaryResult(
            image.Handle.Value,
            image.Format.ToString(),
            image.Architecture.ToString(),
            image.Handle.BuildIdHex,
            image.Symbols.Count,
            image.Sections.Count);

        return NativeResult.Ok(result.Summary, data, result.Hints);
    }

    [McpServerTool(Name = "import_native_manifest")]
    [Description(
        "Bulk handshake from a producer (typically dotnet-diagnostics-mcp): registers a list of " +
        "native binaries in one call. " +
        "mode='lazy' (default) records path hints without opening each file; " +
        "mode='eager' opens every entry immediately and verifies build-ids. " +
        "Per-entry failures are reported inline — one bad entry does not fail the whole batch.")]
    public NativeResult<ImportManifestData> ImportNativeManifest(
        [Description("Manifest entries. Each entry has a 'path' and optional 'name' and 'buildId'.")] IReadOnlyList<BatchManifestEntry> entries,
        [Description("'lazy' (default) records path hints without opening binaries; 'eager' opens and verifies each entry immediately.")] string mode = "lazy")
    {
        var normalizedMode = mode.Trim().ToLowerInvariant();
        if (normalizedMode is not ("lazy" or "eager"))
            return NativeResult.Fail<ImportManifestData>(ErrorKinds.InvalidArgument,
                $"mode must be 'lazy' or 'eager'. Actual: '{mode}'.");

        var isEager = normalizedMode == "eager";
        var results = new List<BatchLoadEntry>(entries.Count);
        var loadedCount = 0;

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Path))
            {
                results.Add(new BatchLoadEntry(
                    entry.Path ?? string.Empty,
                    entry.Name,
                    null,
                    "failed",
                    new NativeError(ErrorKinds.InvalidArgument, "entry path must not be empty.", null)));
                continue;
            }

            if (isEager)
            {
                var loadResult = registry.Load(entry.Path, entry.BuildId);
                if (loadResult.IsError)
                {
                    // Remap binary_mismatch to build_id_mismatch for per-entry clarity
                    var errKind = loadResult.Error!.Kind == ErrorKinds.BinaryMismatch
                        ? ErrorKinds.BuildIdMismatch
                        : loadResult.Error.Kind;
                    results.Add(new BatchLoadEntry(entry.Path, entry.Name, null, "failed",
                        new NativeError(errKind, loadResult.Error.Message, loadResult.Error.Detail)));
                }
                else
                {
                    loadedCount++;
                    results.Add(new BatchLoadEntry(
                        entry.Path, entry.Name, loadResult.Data!.Handle.Value, "loaded", null));
                }
            }
            else
            {
                registry.RegisterHint(entry.Path, entry.BuildId);
                loadedCount++;
                results.Add(new BatchLoadEntry(entry.Path, entry.Name, null, "registered", null));
            }
        }

        var total = entries.Count;
        var verb = isEager ? "Loaded" : "Registered";
        var summary = $"{verb} {loadedCount} of {total} entries.";
        return NativeResult.Ok(summary, new ImportManifestData(results, loadedCount, total));
    }
}

/// <summary>Result payload for <c>load_native_binary</c> (single-path mode).</summary>
/// <param name="ImageHandle">Opaque handle for subsequent tool calls.</param>
/// <param name="Format">Binary format: <c>Elf</c>, <c>Pe</c>, or <c>MachO</c>.</param>
/// <param name="Architecture">CPU architecture: <c>X64</c>, <c>X86</c>, <c>Arm64</c>, or <c>Unknown</c>.</param>
/// <param name="BuildIdHex">Build-id as lowercase hex.</param>
/// <param name="SymbolCount">Total symbol count after loading.</param>
/// <param name="SectionCount">Total section count.</param>
public sealed record LoadNativeBinaryResult(
    string ImageHandle,
    string Format,
    string Architecture,
    string BuildIdHex,
    int SymbolCount,
    int SectionCount);

/// <summary>One entry in a manifest supplied to <c>import_native_manifest</c>.</summary>
/// <param name="Path">Absolute path to the native binary on disk.</param>
/// <param name="Name">Optional display name for the binary (defaults to the file name).</param>
/// <param name="BuildId">Optional expected build-id hex. When supplied and mode is 'eager', the loaded binary's build-id must match.</param>
public sealed record BatchManifestEntry(
    string Path,
    string? Name = null,
    string? BuildId = null);

/// <summary>Per-entry outcome in a batch manifest import.</summary>
/// <param name="Path">Absolute path supplied in the manifest entry.</param>
/// <param name="Name">Optional display name from the manifest entry.</param>
/// <param name="BinaryHandle">ImageHandle when the binary was successfully loaded (eager mode); <c>null</c> in lazy mode or on failure.</param>
/// <param name="Status"><c>loaded</c> (eager success), <c>registered</c> (lazy success), or <c>failed</c>.</param>
/// <param name="Error">Populated on failure; <c>null</c> on success.</param>
public sealed record BatchLoadEntry(
    string Path,
    string? Name,
    string? BinaryHandle,
    string Status,
    NativeError? Error);

/// <summary>Result payload for <c>import_native_manifest</c>.</summary>
/// <param name="Entries">Per-entry outcomes in the same order as the input manifest.</param>
/// <param name="LoadedCount">Number of entries that succeeded (loaded or registered).</param>
/// <param name="TotalCount">Total number of entries submitted.</param>
public sealed record ImportManifestData(
    IReadOnlyList<BatchLoadEntry> Entries,
    int LoadedCount,
    int TotalCount);
