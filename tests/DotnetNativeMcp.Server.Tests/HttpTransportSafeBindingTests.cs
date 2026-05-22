using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DotnetNativeMcp.Server.Tests;

public class HttpTransportSafeBindingTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8789")]
    [InlineData("http://localhost:8789")]
    [InlineData("http://[::1]:8789")]
    [InlineData("http://127.0.0.1:8789;https://localhost:8443")]
    public void LoopbackUrls_WithoutToken_AreAllowed(string urls)
    {
        var configuration = BuildConfiguration(urls: urls);

        InvokeGate(configuration, bearerToken: null);
    }

    [Theory]
    [InlineData("http://0.0.0.0:8789")]
    [InlineData("http://*:8789")]
    [InlineData("http://+:8789")]
    [InlineData("http://192.168.1.10:8789")]
    [InlineData("http://example.com:8789")]
    [InlineData("http://127.0.0.1:8789;http://0.0.0.0:9000")]
    public void NonLoopbackUrls_WithoutToken_Throw(string urls)
    {
        var configuration = BuildConfiguration(urls: urls);

        var act = () => InvokeGate(configuration, bearerToken: null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*non-loopback*");
    }

    [Theory]
    [InlineData("http://0.0.0.0:8789")]
    [InlineData("http://*:8789")]
    public void NonLoopbackUrls_WithToken_AreAllowed(string urls)
    {
        var configuration = BuildConfiguration(urls: urls);

        InvokeGate(configuration, bearerToken: "secret-token");
    }

    [Fact]
    public void NonLoopbackUrls_WithExplicitAllowOptIn_AreAllowed()
    {
        var configuration = BuildConfiguration(
            urls: "http://0.0.0.0:8789",
            extras: new() { ["NativeMcp:AllowUnauthenticatedNonLoopback"] = "true" });

        InvokeGate(configuration, bearerToken: null);
    }

    [Fact]
    public void KestrelEndpoints_NonLoopback_Throw()
    {
        var configuration = BuildConfiguration(
            urls: null,
            extras: new()
            {
                ["Kestrel:Endpoints:Public:Url"] = "http://0.0.0.0:8789",
            });

        var act = () => InvokeGate(configuration, bearerToken: null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*non-loopback*");
    }

    [Fact]
    public void AspNetCoreUrlsEnvironmentVariable_NonLoopback_Throws()
    {
        var configuration = BuildConfiguration(
            urls: null,
            extras: new() { ["ASPNETCORE_URLS"] = "http://0.0.0.0:8789" });

        var act = () => InvokeGate(configuration, bearerToken: null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*non-loopback*");
    }

    [Theory]
    [InlineData("HTTP_PORTS")]
    [InlineData("HTTPS_PORTS")]
    [InlineData("ASPNETCORE_HTTP_PORTS")]
    [InlineData("ASPNETCORE_HTTPS_PORTS")]
    public void HttpPortsShortForm_WithoutToken_Throws(string portsKey)
    {
        var configuration = BuildConfiguration(
            urls: null,
            extras: new() { [portsKey] = "8124" });

        var act = () => InvokeGate(configuration, bearerToken: null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*non-loopback*");
    }

    [Fact]
    public void HttpPortsShortForm_OverriddenByLoopbackUrls_DoesNotThrow()
    {
        // Mirrors ASP.NET Core precedence: when Urls is set, HTTP_PORTS is ignored
        // (Kestrel logs "Overriding HTTP_PORTS ... Binding to values defined by URLS").
        var configuration = BuildConfiguration(
            urls: "http://127.0.0.1:8789",
            extras: new() { ["HTTP_PORTS"] = "8124" });

        InvokeGate(configuration, bearerToken: null);
    }

    [Fact]
    public void Ipv4MappedIpv6Loopback_IsTreatedAsLoopback()
    {
        var configuration = BuildConfiguration(urls: "http://[::ffff:127.0.0.1]:8789");

        InvokeGate(configuration, bearerToken: null);
    }

    [Fact]
    public void NoUrlsConfigured_WithoutToken_DoesNotThrow()
    {
        var configuration = BuildConfiguration(urls: null);

        InvokeGate(configuration, bearerToken: null);
    }

    private static IConfiguration BuildConfiguration(string? urls, Dictionary<string, string?>? extras = null)
    {
        var data = new Dictionary<string, string?>();
        if (urls is not null)
        {
            data["Urls"] = urls;
        }

        if (extras is not null)
        {
            foreach (var (k, v) in extras)
            {
                data[k] = v;
            }
        }

        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    private static void InvokeGate(IConfiguration configuration, string? bearerToken)
    {
        // The fail-fast gate is a top-level local static function in Program.cs.
        // Reflect into the generated <Program>$ class so we can exercise it without
        // spinning up a full WebApplication.
        var programType = typeof(Program).Assembly.GetType("Program")
            ?? throw new InvalidOperationException("Program type not found.");
        var method = programType.GetMethod(
            "<<Main>$>g__EnsureSafeHttpBinding|0_2",
            BindingFlags.NonPublic | BindingFlags.Static);

        if (method is null)
        {
            // Compiler-generated names can shift across SDKs — fall back to a name-suffix search.
            method = programType
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m => m.Name.Contains("EnsureSafeHttpBinding"));
        }

        method.Should().NotBeNull("EnsureSafeHttpBinding must be reachable for testing");

        try
        {
            method!.Invoke(null, new object?[] { configuration, bearerToken });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
