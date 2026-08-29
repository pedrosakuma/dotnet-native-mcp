namespace DotnetNativeMcp.Cli.Output;

public interface ITableRenderable
{
    ValueTask WriteTableAsync(TextWriter writer, CancellationToken cancellationToken = default);
}
