using DotnetNativeMcp.Core;
using DotnetNativeMcp.Server.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// Scaffold-phase entry point. Mirrors the dual-transport shape of
// dotnet-assembly-mcp and dotnet-diagnostics-mcp (stdio for local tool installs,
// HTTP streamable for sidecar deployments). Tool implementations land in V0 —
// see docs/handoff-contract.md and the V0 tracking issue.
//
//   * --stdio (or NATIVE_MCP_TRANSPORT=stdio): JSON-RPC over STDIN/STDOUT.
//   * default: HTTP /mcp on port 8789 (the convention slot after 8787/8788).

var useStdio = args.Contains("--stdio")
    || string.Equals(
        Environment.GetEnvironmentVariable("NATIVE_MCP_TRANSPORT"),
        "stdio",
        StringComparison.OrdinalIgnoreCase);

if (useStdio)
{
    var stdioBuilder = Host.CreateApplicationBuilder(args);

    // stdio: route every log to STDERR so STDOUT stays a clean JSON-RPC channel.
    stdioBuilder.Logging.ClearProviders();
    stdioBuilder.Logging.AddConsole(o =>
    {
        o.LogToStandardErrorThreshold = LogLevel.Trace;
    });
    stdioBuilder.Logging.AddSimpleConsole(o =>
    {
        o.IncludeScopes = true;
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss.fff ";
    });

    ConfigureMcpServer(stdioBuilder.Services).WithStdioServerTransport();

    await stdioBuilder.Build().RunAsync().ConfigureAwait(false);
    return 0;
}

var builder = WebApplication.CreateBuilder(args);
ConfigureMcpServer(builder.Services).WithHttpTransport();

var app = builder.Build();
var bearerToken = ResolveBearerToken(builder.Configuration);

if (!string.IsNullOrWhiteSpace(bearerToken))
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase))
        {
            await next().ConfigureAwait(false);
            return;
        }

        var expectedAuthorization = $"Bearer {bearerToken}";
        var incomingAuthorization = context.Request.Headers.Authorization.ToString();

        if (!string.Equals(incomingAuthorization, expectedAuthorization, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next().ConfigureAwait(false);
    });
}

app.MapGet("/health", () => Results.Ok(new { status = "scaffold", notice = NativeImageLoader.ScaffoldNotice }));
app.MapMcp("/mcp");

await app.RunAsync().ConfigureAwait(false);
return 0;

static IMcpServerBuilder ConfigureMcpServer(IServiceCollection services) =>
    services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "dotnet-native-mcp",
                Version = typeof(ScaffoldTools).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            };
        })
        .WithTools<ScaffoldTools>();

static string? ResolveBearerToken(IConfiguration configuration)
{
    var nativeToken = configuration["NATIVE_MCP_BEARER_TOKEN"];
    if (!string.IsNullOrWhiteSpace(nativeToken))
    {
        return nativeToken;
    }

    var sharedToken = configuration["MCP_BEARER_TOKEN"];
    return string.IsNullOrWhiteSpace(sharedToken) ? null : sharedToken;
}

public partial class Program;
