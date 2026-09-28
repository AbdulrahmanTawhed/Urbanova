using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Urbanova.Application.Analysis;
using Urbanova.Application.Auth;
using Urbanova.Application.Comparison;
using Urbanova.Application.EngineeringFiles;
using Urbanova.Application.Projects;
using Urbanova.Application.Scenarios;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.IntegrationTests;

/// <summary>Phase 9: comparison over HTTP against LocalDB (requires MSSQLLocalDB).</summary>
public sealed class ComparisonTests : IAsyncLifetime
{
    private const string TestCs =
        "Server=(localdb)\\MSSQLLocalDB;Database=Urbanova_Test;Trusted_Connection=True;TrustServerCertificate=True";
    private const string TestKey = "phase9-integration-test-key-0123456789abcdef";

    private const string SquareGeoJson = """
        { "type": "FeatureCollection", "features": [
          { "type": "Feature", "properties": { "name": "Lot" },
            "geometry": { "type": "Polygon",
              "coordinates": [[[0,0],[2,0],[2,2],[0,2],[0,0]]] } } ] }
        """;

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _anon = null!;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:UrbanovaDb"] = TestCs,
                ["Jwt:Key"] = TestKey,
            })));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        _anon = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _anon.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<HttpClient> RegisterAsync()
    {
        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"cp-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private async Task<(Guid ProjectId, ScenarioResponse Baseline, ScenarioResponse Alternative)> SetupPairAsync(
        HttpClient client, double baselineVeg = 10, double altVeg = 100)
    {
        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Cp", null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(SquareGeoJson)), "file", "g.geojson" },
        };
        var up = await client.PostAsync($"/api/projects/{project.Id}/files", form);
        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;

        var an = await client.PostAsJsonAsync($"/api/projects/{project.Id}/analysis",
            new AnalyzeRequest(file.Id, new() { ["vegetationCoverPct"] = baselineVeg }));
        var run = (await an.Content.ReadFromJsonAsync<AnalysisRunResponse>())!;

        var bl = await client.PostAsJsonAsync($"/api/projects/{project.Id}/scenarios",
            new CreateScenarioRequest("Baseline", run.RunId, null, null));
        var baseline = (await bl.Content.ReadFromJsonAsync<ScenarioResponse>())!;

        var al = await client.PostAsJsonAsync($"/api/projects/{project.Id}/scenarios",
            new CreateScenarioRequest("Green", null, baseline.Id, new() { ["vegetationCoverPct"] = altVeg }));
        var alternative = (await al.Content.ReadFromJsonAsync<ScenarioResponse>())!;

        var ar = await client.PostAsJsonAsync($"/api/scenarios/{alternative.Id}/analyze",
            new AnalyzeScenarioRequest(null));
        ar.StatusCode.Should().Be(HttpStatusCode.Created);

        return (project.Id, baseline, alternative);
    }

    [Fact]
    public async Task Compare_ReturnsDeltas_AndTradeoffs()
    {
        var client = await RegisterAsync();
        var (projectId, baseline, alternative) = await SetupPairAsync(client);

        var res = await client.GetAsync(
            $"/api/projects/{projectId}/comparison?baselineId={baseline.Id}&alternativeId={alternative.Id}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var cmp = (await res.Content.ReadFromJsonAsync<ComparisonResponse>())!;
        cmp.Baseline.ScenarioId.Should().Be(baseline.Id);
        cmp.Alternative.ScenarioId.Should().Be(alternative.Id);
        cmp.Baseline.RunId.Should().NotBe(cmp.Alternative.RunId);
        cmp.WhatChanged.Should().ContainSingle()
            .Which.Should().Be(new ParameterChangeDto("vegetationCoverPct", 10, 100));
        cmp.Environmental.Areas.Should().ContainSingle();
        cmp.Environmental.Areas[0].BaselineValue.Should().BeApproximately(31.5, 1e-9);
        cmp.Environmental.Areas[0].AlternativeValue.Should().BeApproximately(27.0, 1e-9);
        cmp.Environmental.Areas[0].Delta.Should().BeApproximately(-4.5, 1e-9);
        cmp.Environmental.Areas[0].Improved.Should().BeTrue();
        cmp.Environmental.MeanDelta.Should().BeApproximately(-4.5, 1e-9);
        cmp.Environmental.ImprovedClassTransitions.Should().Be(1);
        cmp.Cost.Status.Should().Be("Unavailable");
        cmp.Feasibility.Status.Should().Be("Unavailable");
        cmp.Feasibility.Reason.Should().NotBeNullOrWhiteSpace();
        cmp.Tradeoffs.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Compare_UnanalyzedAlternative_Returns400()
    {
        var client = await RegisterAsync();
        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Cp2", null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;
        var b = await client.PostAsJsonAsync($"/api/projects/{project.Id}/scenarios",
            new CreateScenarioRequest("B", null, null, null));
        var baseline = (await b.Content.ReadFromJsonAsync<ScenarioResponse>())!;
        var a = await client.PostAsJsonAsync($"/api/projects/{project.Id}/scenarios",
            new CreateScenarioRequest("A", null, baseline.Id, null));
        var alternative = (await a.Content.ReadFromJsonAsync<ScenarioResponse>())!;

        var res = await client.GetAsync(
            $"/api/projects/{project.Id}/comparison?baselineId={baseline.Id}&alternativeId={alternative.Id}");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync()).Should().Contain("no succeeded analysis");
    }

    [Fact]
    public async Task Compare_MixedCurrencies_StaysUnavailable()
    {
        // Regression: totals across USD + EUR were summed and mislabeled with one currency.
        var client = await RegisterAsync();
        var (projectId, baseline, alternative) = await SetupPairAsync(client);

        await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new Urbanova.Application.Costing.CreateCostEstimateRequest(
                10, "m2", 10m, null, "USD", null, baseline.Id));
        await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new Urbanova.Application.Costing.CreateCostEstimateRequest(
                10, "m2", 6m, null, "EUR", null, alternative.Id));

        var cmp = await client.GetFromJsonAsync<ComparisonResponse>(
            $"/api/projects/{projectId}/comparison?baselineId={baseline.Id}&alternativeId={alternative.Id}");
        cmp!.Cost.Status.Should().Be("Unavailable");
        cmp.Cost.Reason.Should().Contain("multiple currencies");
        cmp.Cost.BaselineTotal.Should().BeNull();
    }

    [Fact]
    public async Task Compare_WrongKinds_Returns400()
    {
        var client = await RegisterAsync();
        var (projectId, baseline, alternative) = await SetupPairAsync(client);

        // Swapped roles: alternative passed as baseline.
        var res = await client.GetAsync(
            $"/api/projects/{projectId}/comparison?baselineId={alternative.Id}&alternativeId={baseline.Id}");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Compare_CrossOwner_Returns403_MissingQuery_Returns400()
    {
        var alice = await RegisterAsync();
        var bob = await RegisterAsync();
        var (projectId, baseline, alternative) = await SetupPairAsync(alice);

        (await bob.GetAsync(
            $"/api/projects/{projectId}/comparison?baselineId={baseline.Id}&alternativeId={alternative.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await alice.GetAsync($"/api/projects/{projectId}/comparison"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
