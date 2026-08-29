using DotnetNativeMcp.Core;

namespace DotnetNativeMcp.Cli.Output;

public interface IOutputWriter
{
    ValueTask WriteAsync<T>(NativeResult<T> result, CancellationToken cancellationToken = default);
}
