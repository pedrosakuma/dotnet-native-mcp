using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DotnetNativeMcp.Server.Tests;

public class HttpTransportAuthenticationTests
{
    [Fact]
    public async Task NoTokenConfigured_AllowsHealthAndMcpRequests()
    {
        using var environmentScope = new EnvironmentVariableScope();
        await using var factory = new NativeMcpWebApplicationFactory();
        using var client = factory.CreateClient();

        var healthResponse = await client.GetAsync("/health");
        var mcpResponse = await client.GetAsync("/mcp");

        healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        mcpResponse.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenConfigured_CorrectHeader_AllowsMcpRequests()
    {
        const string token = "secret-token";

        using var environmentScope = new EnvironmentVariableScope(token);
        await using var factory = new NativeMcpWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/mcp");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenConfigured_MissingHeader_ReturnsUnauthorized()
    {
        using var environmentScope = new EnvironmentVariableScope("secret-token");
        await using var factory = new NativeMcpWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/mcp");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Be("unauthorized");
    }

    [Fact]
    public async Task TokenConfigured_WrongToken_ReturnsUnauthorized()
    {
        using var environmentScope = new EnvironmentVariableScope("secret-token");
        await using var factory = new NativeMcpWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");

        var response = await client.GetAsync("/mcp");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Be("unauthorized");
    }

    [Fact]
    public async Task TokenConfigured_HealthRemainsOpen()
    {
        using var environmentScope = new EnvironmentVariableScope("secret-token");
        await using var factory = new NativeMcpWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class NativeMcpWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string? previousNativeToken;
        private readonly string? previousSharedToken;
        private readonly string? previousTransport;

        public EnvironmentVariableScope(string? token = null)
        {
            previousNativeToken = Environment.GetEnvironmentVariable("NATIVE_MCP_BEARER_TOKEN");
            previousSharedToken = Environment.GetEnvironmentVariable("MCP_BEARER_TOKEN");
            previousTransport = Environment.GetEnvironmentVariable("NATIVE_MCP_TRANSPORT");

            Environment.SetEnvironmentVariable("NATIVE_MCP_BEARER_TOKEN", token);
            Environment.SetEnvironmentVariable("MCP_BEARER_TOKEN", null);
            Environment.SetEnvironmentVariable("NATIVE_MCP_TRANSPORT", null);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("NATIVE_MCP_BEARER_TOKEN", previousNativeToken);
            Environment.SetEnvironmentVariable("MCP_BEARER_TOKEN", previousSharedToken);
            Environment.SetEnvironmentVariable("NATIVE_MCP_TRANSPORT", previousTransport);
        }
    }
}
