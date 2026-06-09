using System.Globalization;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging; // for INativeBinaryRegistry
using DotnetNativeMcp.Core.Security; // for PathAccessPolicy
using DotnetNativeMcp.Core.Symbols; // for SourceResolver
using DotnetNativeMcp.Core.Xref;   // for NativeCallGraphCache
using ModelContextProtocol.Server;

namespace DotnetNativeMcp.Server.Tools;

/// <summary>
/// V0 MCP tools for navigating native .NET binaries (NativeAOT and ReadyToRun).
/// Accepts <c>NativeFrame</c> handoffs from <c>dotnet-diagnostics-mcp</c>.
/// </summary>
#pragma warning disable CS9113
[McpServerToolType]
public sealed partial class NativeTools(INativeBinaryRegistry registry, NativeCallGraphCache callGraphCache, SourceResolver sourceResolver, PathAccessPolicy? pathPolicy = null)
{
#pragma warning restore CS9113

    private readonly PathAccessPolicy _pathPolicy = pathPolicy ?? PathAccessPolicy.Permissive;

    private static string ToHex(ulong value) => value.ToString("x16", CultureInfo.InvariantCulture);

    private static string FormatByteDelta(long delta)
    {
        if (delta == 0)
            return "0 B";

        var sign = delta > 0 ? "+" : "-";
        return sign + FormatBytes(delta > 0 ? (ulong)delta : (ulong)(-delta));
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        var format = unitIndex == 0 ? "0" : "0.0";
        return value.ToString(format, CultureInfo.InvariantCulture) + " " + units[unitIndex];
    }

    private static ulong ParseHex(string hex) => ulong.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
