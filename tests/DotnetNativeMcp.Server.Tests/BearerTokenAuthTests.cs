using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DotnetNativeMcp.Server.Tests;

public sealed class BearerTokenAuthTests
{
    [Fact]
    public async Task Rejects_requests_without_token_when_auth_is_configured()
    {
        await using var factory = CreateFactory("triad-secret");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/mcp");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_is_accessible_without_token_when_auth_is_configured()
    {
        await using var factory = CreateFactory("triad-secret");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Requests_with_valid_token_are_not_rejected()
    {
        await using var factory = CreateFactory("triad-secret");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "triad-secret");

        var response = await client.GetAsync("/mcp");

        response.StatusCode.Should().NotBe(System.Net.HttpStatusCode.Unauthorized);
    }

    private static WebApplicationFactory<Program> CreateFactory(string token) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["NATIVE_MCP_BEARER_TOKEN"] = token,
                    ["MCP_BEARER_TOKEN"] = null,
                });
            });
        });
}
