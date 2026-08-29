namespace DotnetNativeMcp.Cli;

public static class Program
{
    public static Task<int> Main(string[] args) => CliApplication.InvokeAsync(args);
}
