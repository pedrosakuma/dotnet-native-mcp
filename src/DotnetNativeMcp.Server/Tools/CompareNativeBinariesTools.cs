using System.ComponentModel;
using DotnetNativeMcp.Core;
using ModelContextProtocol.Server;

namespace DotnetNativeMcp.Server.Tools;

[McpServerToolType]
public sealed class CompareNativeBinariesTools
{
    [McpServerTool(Name = "compare_native_binaries")]
    [Description("Diffs two loaded image handles and returns symbol/section growth and shrink details.")]
    public static BinaryDiff CompareNativeBinaries(
        [Description("Baseline image handle (older release).")] string baselineImageHandle,
        [Description("Target image handle (newer release).")] string targetImageHandle,
        [Description("Percent threshold used to suppress symbol size noise. Defaults to 5.")] double thresholdPercent = 5.0) =>
        NativeImageLoader.CompareNativeBinaries(baselineImageHandle, targetImageHandle, thresholdPercent);
}
