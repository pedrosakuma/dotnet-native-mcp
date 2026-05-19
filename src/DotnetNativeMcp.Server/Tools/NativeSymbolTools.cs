using System.ComponentModel;
using DotnetNativeMcp.Core;
using ModelContextProtocol.Server;

namespace DotnetNativeMcp.Server.Tools;

[McpServerToolType]
public sealed class NativeSymbolTools
{
    private static readonly NativeSymbolicationService Symbolication = NativeSymbolicationService.Shared;

    [McpServerTool(Name = "list_native_symbols")]
    [Description("Lists native symbols for a binary. Auto-loads the binary if it has not been loaded yet.")]
    public static NativeResult<ListNativeSymbolsResponse> ListNativeSymbols(
        [Description("Absolute or relative path to the native binary.")] string binary) =>
        Symbolication.ListNativeSymbols(binary);

    [McpServerTool(Name = "symbolicate_stack")]
    [Description("Bulk symbolication entry point for raw crash-log addresses without a producer. Accepts up to 200 frames.")]
    public static NativeResult<SymbolicateStackResponse> SymbolicateStack(
        [Description("Frames to symbolicate. Each frame contains binary, addressHex, and optional loadBase.")]
        IReadOnlyList<SymbolicateStackFrameRequest> frames) =>
        Symbolication.SymbolicateStack(frames);
}
