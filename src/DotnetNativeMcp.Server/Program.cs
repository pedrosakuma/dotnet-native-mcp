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
EnsureSafeHttpBinding(builder.Configuration, bearerToken);
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

static void EnsureSafeHttpBinding(IConfiguration configuration, string? bearerToken)
{
    if (!string.IsNullOrEmpty(bearerToken))
    {
        return;
    }

    var allowUnauth = configuration["NativeMcp:AllowUnauthenticatedNonLoopback"];
    if (string.Equals(allowUnauth, "true", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    foreach (var url in CollectConfiguredUrls(configuration))
    {
        if (!IsLoopbackUrl(url))
        {
            throw new InvalidOperationException(
                $"HTTP transport is configured to bind to '{url}' without a bearer token. "
                + "Refusing to start: an unauthenticated MCP endpoint on a non-loopback address "
                + "exposes filesystem-reading tools to any client that can reach the host. "
                + "Set NATIVE_MCP_BEARER_TOKEN (or NativeMcp:BearerToken / MCP_BEARER_TOKEN), "
                + "bind to 127.0.0.1 / ::1 / localhost, or — if a trusted reverse proxy in front "
                + "of the server provides authentication — opt in explicitly with "
                + "NativeMcp:AllowUnauthenticatedNonLoopback=true.");
        }
    }
}

static IEnumerable<string> CollectConfiguredUrls(IConfiguration configuration)
{
    var anyUrlsConfigured = false;

    foreach (var key in new[] { "Urls", "URLS", "ASPNETCORE_URLS" })
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            continue;
        }

        anyUrlsConfigured = true;

        foreach (var entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return entry;
        }
    }

    // ASP.NET Core precedence: HTTP_PORTS / HTTPS_PORTS (and ASPNETCORE_*) only
    // take effect when no Urls/ASPNETCORE_URLS is configured — when both are set,
    // Kestrel logs "Overriding HTTP_PORTS ... Binding to values defined by URLS".
    // Mirror that here so a benign HTTP_PORTS env var doesn't trip the gate when
    // Urls already pins the bind to loopback.
    if (!anyUrlsConfigured)
    {
        foreach (var (key, scheme) in new[]
                 {
                     ("HTTP_PORTS", "http"),
                     ("HTTPS_PORTS", "https"),
                     ("ASPNETCORE_HTTP_PORTS", "http"),
                     ("ASPNETCORE_HTTPS_PORTS", "https"),
                 })
        {
            var value = configuration[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (var port in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return $"{scheme}://*:{port}";
            }
        }
    }

    var kestrelEndpoints = configuration.GetSection("Kestrel:Endpoints");
    foreach (var endpoint in kestrelEndpoints.GetChildren())
    {
        var url = endpoint["Url"];
        if (!string.IsNullOrWhiteSpace(url))
        {
            yield return url;
        }
    }
}

static bool IsLoopbackUrl(string url)
{
    // ASP.NET Core wildcard forms (http://*:8789, http://+:8789) bind to every
    // interface — treat as non-loopback. Tolerate scheme-less inputs by retrying
    // with a synthetic scheme so `*:8789` and `0.0.0.0:8789` are also caught.
    if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
    {
        if (!Uri.TryCreate("http://" + url, UriKind.Absolute, out parsed))
        {
            // Unparsable: be conservative and treat as non-loopback so the gate trips.
            return false;
        }
    }

    var host = parsed.Host;
    if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (!System.Net.IPAddress.TryParse(host, out var ip))
    {
        return false;
    }

    // IsLoopback already covers 127.0.0.0/8 and ::1; also collapse IPv4-mapped
    // IPv6 addresses (e.g. ::ffff:127.0.0.1) before checking.
    if (ip.IsIPv4MappedToIPv6)
    {
        ip = ip.MapToIPv4();
    }

    return System.Net.IPAddress.IsLoopback(ip);
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
