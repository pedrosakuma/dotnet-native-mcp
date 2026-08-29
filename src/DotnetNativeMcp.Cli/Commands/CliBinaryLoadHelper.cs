using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Security;

namespace DotnetNativeMcp.Cli;

internal static class CliBinaryLoadHelper
{
    internal static NativeResult<NativeImage> LoadValidatedImage(
        INativeBinaryRegistry registry,
        PathAccessPolicy pathPolicy,
        string path)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(pathPolicy);

        var validation = pathPolicy.Validate(path);
        if (validation.IsError)
        {
            return NativeResult.Fail<NativeImage>(
                validation.Error!.Kind,
                validation.Error.Message,
                validation.Error.Detail);
        }

        return registry.Load(validation.Data!);
    }
}
