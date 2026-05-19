using System.ComponentModel;
using DotnetNativeMcp.Core;
using ModelContextProtocol.Server;

namespace DotnetNativeMcp.Server.Tools;

/// <summary>
/// Temporary scaffold notice tool kept while the rest of the planned surface lands.
/// </summary>
[McpServerToolType]
public sealed class ScaffoldTools
{
    [McpServerTool(Name = "scaffold_status")]
    [Description("Returns the scaffold notice. dotnet-native-mcp is not yet feature-complete; see the repository's V0 tracking issue.")]
    public static string ScaffoldStatus() => NativeImageLoader.ScaffoldNotice;
}
