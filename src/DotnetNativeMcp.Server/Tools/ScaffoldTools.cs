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
}
