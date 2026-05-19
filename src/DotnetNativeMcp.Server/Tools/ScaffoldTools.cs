using System.ComponentModel;
using DotnetNativeMcp.Core;
using ModelContextProtocol.Server;

namespace DotnetNativeMcp.Server.Tools;

/// <summary>
/// Single placeholder tool so a fresh MCP client can connect, list tools, and see
/// the scaffold notice instead of an empty response. Replaced by the real V0 set
/// (load_native_binary / list_native_symbols / resolve_symbol / disassemble).
/// </summary>
[McpServerToolType]
public sealed class ScaffoldTools
{
    [McpServerTool(Name = "scaffold_status")]
    [Description("Returns the scaffold notice. dotnet-native-mcp is not yet feature-complete; see the repository's V0 tracking issue.")]
    public static string ScaffoldStatus() => NativeImageLoader.ScaffoldNotice;

    [McpServerTool(Name = "load_native_binary")]
    [Description("Loads a native binary and returns an image handle. When a paired .mstat sidecar exists, includes a hint to call get_size_breakdown.")]
    public static NativeResult<LoadNativeBinaryValue> LoadNativeBinary(
        [Description("Absolute or relative path to the native binary.")] string binaryPath)
    {
        var loaded = NativeImageLoader.LoadNativeBinary(binaryPath);
        if (!loaded.Ok || loaded.Value is null)
        {
            return NativeResult.Fail<LoadNativeBinaryValue>(
                loaded.Error?.Kind ?? NativeErrorKind.InvalidArgument,
                loaded.Error?.Detail ?? "Failed to load native binary.");
        }

        var hint = loaded.Value.MstatPath is null
            ? null
            : "Detected .mstat sidecar. Call get_size_breakdown with this imageHandle.";

        return NativeResult.Success(
            new LoadNativeBinaryValue(loaded.Value.ImageHandle, loaded.Value.BinaryPath, loaded.Value.MstatPath, hint));
    }

    [McpServerTool(Name = "get_size_breakdown")]
    [Description("Reads a .mstat sidecar for a loaded image and returns top-N native size groups by assembly, namespace, type, or method.")]
    public static NativeResult<SizeBreakdownValue> GetSizeBreakdown(
        [Description("Image handle returned by load_native_binary.")] string imageHandle,
        [Description("Grouping key: assembly, namespace, type, or method.")] string? groupBy = null,
        [Description("Maximum number of rows to return. Default 25; allowed range 1-500.")] int topN = 25,
        [Description("Optional explicit path to a .mstat sidecar. If omitted, the server auto-locates one next to the binary.")] string? mstatPath = null)
    {
        if (!NativeImageLoader.TryGetLoadedImage(imageHandle, out var image))
        {
            return NativeResult.Fail<SizeBreakdownValue>(
                NativeErrorKind.UnknownImageHandle,
                $"Unknown image handle '{imageHandle}'.");
        }

        if (!TryParseGroupBy(groupBy, out var parsedGroupBy))
        {
            return NativeResult.Fail<SizeBreakdownValue>(
                NativeErrorKind.InvalidArgument,
                "groupBy must be one of: assembly, namespace, type, method.");
        }

        return MstatSizeAnalyzer.GetSizeBreakdown(image, parsedGroupBy, topN, mstatPath);
    }

    private static bool TryParseGroupBy(string? value, out SizeBreakdownGroupBy groupBy)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            groupBy = SizeBreakdownGroupBy.Assembly;
            return true;
        }

        return Enum.TryParse(value, true, out groupBy);
    }
}

public sealed record LoadNativeBinaryValue(
    string ImageHandle,
    string BinaryPath,
    string? MstatPath,
    string? Hint);
