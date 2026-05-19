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

    [McpServerTool(Name = "extract_strings")]
    [Description("Extracts printable ASCII and UTF-16LE strings from selected binary sections.")]
    public static ExtractStringsResult ExtractStrings(
        [Description("Absolute path to the native binary to inspect.")] string binaryPath,
        [Description("Optional section filter. Defaults to .rodata and .rdata.")] string[]? sections = null,
        [Description("Minimum length for extracted strings.")] int minLength = 6,
        [Description("Maximum results per page (capped at 2000).")] int maxResults = 200,
        [Description("Result offset for pagination.")] int pageOffset = 0) =>
        NativeStringExtractor.Extract(binaryPath, sections, minLength, maxResults, pageOffset);
}
