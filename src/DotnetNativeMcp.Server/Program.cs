using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Xref;
using DotnetNativeMcp.Core.Symbols;
using DotnetNativeMcp.Server.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// V0 entry point. Mirrors the dual-transport shape of dotnet-assembly-mcp and
// dotnet-diagnostics-mcp (stdio for local tool installs, HTTP streamable for sidecar
// deployments).
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
builder.Configuration.AddEnvironmentVariables();
ConfigureMcpServer(builder.Services).WithHttpTransport();

var bearerToken = ResolveBearerToken(builder.Configuration);
var app = builder.Build();

var informationalVersion = typeof(Program).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? typeof(Program).Assembly.GetName().Version?.ToString()
    ?? "unknown";

app.MapGet("/health", () => Results.Ok(new { status = "ok", version = informationalVersion }));

if (!string.IsNullOrEmpty(bearerToken))
{
    var expectedTokenBytes = Encoding.UTF8.GetBytes(bearerToken);

    app.Use(async (context, next) =>
    {
        if (!context.Request.Path.StartsWithSegments("/mcp"))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (!HasValidBearerToken(context.Request.Headers.Authorization, expectedTokenBytes))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("unauthorized").ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    });
}

app.MapMcp("/mcp");

await app.RunAsync().ConfigureAwait(false);
return 0;

static IMcpServerBuilder ConfigureMcpServer(IServiceCollection services)
{
    services.AddSingleton<INativeBinaryRegistry, NativeBinaryRegistry>();
    services.AddSingleton<NativeCallGraphCache>();
    services.AddSingleton<SourceResolver>();

    return services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "dotnet-native-mcp",
                Version = typeof(NativeTools).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            };
        })
        .WithTools<NativeTools>();
}

static string? ResolveBearerToken(IConfiguration configuration)
{
    var configuredToken = configuration["NativeMcp:BearerToken"];
    if (!string.IsNullOrWhiteSpace(configuredToken))
    {
        return configuredToken;
    }

    var nativeToken = configuration["NATIVE_MCP_BEARER_TOKEN"];
    if (!string.IsNullOrWhiteSpace(nativeToken))
    {
        return nativeToken;
    }

    var sharedToken = configuration["MCP_BEARER_TOKEN"];
    return string.IsNullOrWhiteSpace(sharedToken) ? null : sharedToken;
}

static bool HasValidBearerToken(string? authorizationHeader, byte[] expectedTokenBytes)
{
    const string BearerPrefix = "Bearer ";

    if (string.IsNullOrEmpty(authorizationHeader)
        || !authorizationHeader.StartsWith(BearerPrefix, StringComparison.Ordinal))
    {
        return false;
    }

    var presentedTokenBytes = Encoding.UTF8.GetBytes(authorizationHeader[BearerPrefix.Length..]);
    return presentedTokenBytes.Length == expectedTokenBytes.Length
        && CryptographicOperations.FixedTimeEquals(presentedTokenBytes, expectedTokenBytes);
}

public partial class Program;
