using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Urbanova.IntegrationTests;

/// <summary>Phase 14: OpenAPI annotations generate a valid document (no broken schemas).</summary>
public sealed class ApiDocsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiDocsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b => b.UseEnvironment("Development"));
    }

    [Fact]
    public async Task OpenApiDocument_IsReachable()
    {
        using var client = _factory.CreateClient();
        var res = await client.GetAsync("/openapi/v1.json");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("/api/projects").And.Contain("/api/auth/register");
    }
}
