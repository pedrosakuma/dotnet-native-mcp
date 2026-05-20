using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DotnetNativeMcp.Server.Tests;

public class HealthEndpointTests
{
    [Fact]
    public async Task HealthEndpoint_ReturnsOkStatus()
    {
        await using var factory = new NativeMcpWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsValidJson()
    {
        await using var factory = new NativeMcpWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();

        var jsonDoc = JsonDocument.Parse(content);
        jsonDoc.RootElement.GetProperty("status").GetString().Should().Be("ok");
        jsonDoc.RootElement.TryGetProperty("version", out var versionElement).Should().BeTrue();
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsAssemblyVersion()
    {
        await using var factory = new NativeMcpWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();

        var jsonDoc = JsonDocument.Parse(content);
        var returnedVersion = jsonDoc.RootElement.GetProperty("version").GetString();

        // Get expected version from assembly
        var expectedVersion = GetExpectedAssemblyVersion();

        returnedVersion.Should().NotBeNull();
        returnedVersion.Should().NotBe("v0"); // Should not be hardcoded old value
        returnedVersion.Should().Be(expectedVersion);
    }

    private static string GetExpectedAssemblyVersion()
    {
        var assembly = typeof(Program).Assembly;
        
        // Try to get InformationalVersion first
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        
        if (!string.IsNullOrEmpty(informationalVersion))
        {
            return informationalVersion;
        }

        // Fall back to assembly version
        var assemblyVersion = assembly.GetName().Version?.ToString();
        if (!string.IsNullOrEmpty(assemblyVersion))
        {
            return assemblyVersion;
        }

        return "unknown";
    }

    private sealed class NativeMcpWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
        }
    }
}
