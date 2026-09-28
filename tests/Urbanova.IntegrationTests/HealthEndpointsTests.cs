using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Urbanova.IntegrationTests;

/// <summary>Phase 1 smoke: pipeline boots, health + diagnostics respond, errors are ProblemDetails.</summary>
public sealed class HealthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Live_Returns200()
    {
        var res = await _client.GetAsync("/health/live");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_Returns200()
    {
        var res = await _client.GetAsync("/health/ready");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SystemInfo_ReturnsServiceMetadata()
    {
        var info = await _client.GetFromJsonAsync<SystemInfo>("/api/system/info");
        info.Should().NotBeNull();
        info!.Service.Should().Be("urbanova-backend");
        info.Phase.Should().StartWith("phase-");
    }

    [Fact]
    public async Task UnknownApiRoute_Anonymous_Returns401_SecureByDefault()
    {
        // Fallback policy requires auth before routing resolves: unknown /api paths
        // challenge anonymous callers instead of leaking route existence (404).
        var res = await _client.GetAsync("/api/does-not-exist");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed record SystemInfo(string Service, string Phase, string Dotnet, string Environment, DateTimeOffset TimeUtc);
}
