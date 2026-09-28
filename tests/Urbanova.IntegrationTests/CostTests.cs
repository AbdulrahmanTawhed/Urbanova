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
using Urbanova.Application.Costing;
using Urbanova.Application.EngineeringFiles;
using Urbanova.Application.Projects;
using Urbanova.Application.Recommendations;
using Urbanova.Application.Scenarios;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.IntegrationTests;

/// <summary>Phase 11: cost estimation over HTTP against LocalDB (requires MSSQLLocalDB).
/// A temp catalog with known prices overrides PriceCatalog:Path per test run.</summary>
public sealed class CostTests : IAsyncLifetime
{
    private const string TestCs =
        "Server=(localdb)\\MSSQLLocalDB;Database=Urbanova_Test;Trusted_Connection=True;TrustServerCertificate=True";
    private const string TestKey = "phase11-integration-test-key-0123456789abcdef";

    private const string CatalogJson = """
        {"currency":"USD","items":[
          {"code":"veg-planting-m2","description":"fixture","unit":"m2","unitPrice":10.00,"source":"fixture-catalog"}
        ]}
        """;

    private const string SquareGeoJson = """
        { "type": "FeatureCollection", "features": [
          { "type": "Feature", "properties": { "name": "Lot" },
            "geometry": { "type": "Polygon",
              "coordinates": [[[0,0],[2,0],[2,2],[0,2],[0,0]]] } } ] }
        """;

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _anon = null!;
    private string _catalogPath = null!;

    public async Task InitializeAsync()
    {
        _catalogPath = Path.Combine(Path.GetTempPath(), $"urbanova-cost-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(_catalogPath, CatalogJson);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:UrbanovaDb"] = TestCs,
                ["Jwt:Key"] = TestKey,
                ["PriceCatalog:Path"] = _catalogPath,
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
        if (File.Exists(_catalogPath))
            File.Delete(_catalogPath);
    }

    private async Task<(HttpClient Client, Guid ProjectId)> SetupAsync()
    {
        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"cost-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Cost", null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;
        return (client, project.Id);
    }

    [Fact]
    public async Task Create_DirectPrice_CalculatesTotal()
    {
        var (client, projectId) = await SetupAsync();
        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(4, "m2", 12.50m, null, "USD", null, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = (await res.Content.ReadFromJsonAsync<CostEstimateResponse>())!;
        body.Total.Should().Be(50.00m);
        body.Status.Should().Be("Calculated");
        body.PriceSource.Should().Be("user-provided");
    }

    [Fact]
    public async Task Create_CatalogCode_UsesCatalogPrice()
    {
        var (client, projectId) = await SetupAsync();
        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(3, "m2", null, "veg-planting-m2", null, null, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = (await res.Content.ReadFromJsonAsync<CostEstimateResponse>())!;
        body.UnitPrice.Should().Be(10.00m);
        body.Total.Should().Be(30.00m);
        body.Status.Should().Be("Calculated");
        body.PriceSource.Should().Be("fixture-catalog");
    }

    [Fact]
    public async Task Create_UnknownCode_ReturnsUnavailable_NotInvented()
    {
        var (client, projectId) = await SetupAsync();
        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(3, "m2", null, "unobtanium-m2", null, null, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = (await res.Content.ReadFromJsonAsync<CostEstimateResponse>())!;
        body.Status.Should().Be("Unavailable");
        body.Total.Should().Be(0);
    }

    [Fact]
    public async Task Create_NoPrice_Returns400()
    {
        var (client, projectId) = await SetupAsync();
        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(3, "m2", null, null, null, null, null));
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_LinkedToRecommendation_Backfills()
    {
        var (client, projectId) = await SetupAsync();

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(SquareGeoJson)), "file", "g.geojson" },
        };
        var up = await client.PostAsync($"/api/projects/{projectId}/files", form);
        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;
        await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, []));
        var recs = await client.GetFromJsonAsync<RecommendationResponse[]>(
            $"/api/projects/{projectId}/recommendations");
        var rec = recs.Should().ContainSingle().Subject;

        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(100, "m2", 8.50m, null, null, rec.Id, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        (await res.Content.ReadFromJsonAsync<CostEstimateResponse>())!.RecommendationId.Should().Be(rec.Id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Recommendations.SingleAsync(r => r.Id == rec.Id);
        row.CostEstimateId.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_NullQuantity_DerivesPolygonArea()
    {
        var (client, projectId) = await SetupAsync();

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(SquareGeoJson)), "file", "g.geojson" },
        };
        var up = await client.PostAsync($"/api/projects/{projectId}/files", form);
        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;
        await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, []));
        var recs = await client.GetFromJsonAsync<RecommendationResponse[]>(
            $"/api/projects/{projectId}/recommendations");
        var rec = recs.Should().ContainSingle().Subject;

        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(null, "m2", 8.50m, null, null, rec.Id, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = (await res.Content.ReadFromJsonAsync<CostEstimateResponse>())!;
        // Spherical reference for the 2°×2° (lat 0–2) square: R²·Δλ·(sin2°−sin0°) ≈ 4.9447e10 m².
        // Must be positive real square meters — never the planar 4 deg², never rounded to 0.
        body.Quantity.Should().BeGreaterThan(0);
        body.Quantity.Should().BeApproximately(49447203765.22m, 1_000_000m);
        body.QuantitySource.Should().Be("DerivedFromGeometry");
        body.Unit.Should().Be("m2");
        body.Total.Should().Be(body.Quantity * 8.50m);
        body.Status.Should().Be("Calculated");
    }

    [Fact]
    public async Task Create_UnicodeUnit_DerivesQuantity()
    {
        var (client, projectId) = await SetupAsync();

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(SquareGeoJson)), "file", "g.geojson" },
        };
        var up = await client.PostAsync($"/api/projects/{projectId}/files", form);
        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;
        await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, []));
        var recs = await client.GetFromJsonAsync<RecommendationResponse[]>(
            $"/api/projects/{projectId}/recommendations");
        var rec = recs.Should().ContainSingle().Subject;

        // "m²" spelling must behave like "m2" (docs use both).
        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(null, "m²", 8.50m, null, null, rec.Id, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = (await res.Content.ReadFromJsonAsync<CostEstimateResponse>())!;
        body.Quantity.Should().BeGreaterThan(0);
        body.Quantity.Should().BeApproximately(49447203765.22m, 1_000_000m);
        body.QuantitySource.Should().Be("DerivedFromGeometry");
        body.Total.Should().Be(body.Quantity * 8.50m);
    }

    [Fact]
    public async Task Create_TinyRealWorldPolygon_DerivesPositiveQuantity()
    {
        // Regression: ~3.2e-6 deg² (real urban plot) rounded to quantity 0.
        const string tiny = """
            { "type": "FeatureCollection", "features": [
              { "type": "Feature", "properties": { "name": "Plot" },
                "geometry": { "type": "Polygon", "coordinates": [[
                  [31.2357, 30.0444], [31.2377, 30.0444],
                  [31.2377, 30.0460], [31.2357, 30.0460], [31.2357, 30.0444]
                ]] } } ] }
            """;
        var (client, projectId) = await SetupAsync();

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(tiny)), "file", "plot.geojson" },
        };
        var up = await client.PostAsync($"/api/projects/{projectId}/files", form);
        up.StatusCode.Should().Be(HttpStatusCode.Created);
        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;
        await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, []));
        var recs = await client.GetFromJsonAsync<RecommendationResponse[]>(
            $"/api/projects/{projectId}/recommendations");
        var rec = recs.Should().ContainSingle().Subject;

        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(null, "m2", 50m, null, "EGP", rec.Id, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = (await res.Content.ReadFromJsonAsync<CostEstimateResponse>())!;
        body.Quantity.Should().BeGreaterThan(0, "a valid non-zero polygon must never derive zero");
        body.QuantitySource.Should().Be("DerivedFromGeometry");
        body.Unit.Should().Be("m2");
        body.Total.Should().Be(body.Quantity * 50m);
        body.Status.Should().Be("Calculated");
    }

    [Fact]
    public async Task Create_NullQuantity_WithoutRecommendation_Returns400()
    {
        var (client, projectId) = await SetupAsync();
        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(null, "m2", 8.50m, null, null, null, null));
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Comparison_CostDimension_FillsFromScenarioEstimates()
    {
        var (client, projectId) = await SetupAsync();

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(SquareGeoJson)), "file", "g.geojson" },
        };
        var up = await client.PostAsync($"/api/projects/{projectId}/files", form);
        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;
        var an = await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, []));
        var run = (await an.Content.ReadFromJsonAsync<AnalysisRunResponse>())!;

        var bl = await client.PostAsJsonAsync($"/api/projects/{projectId}/scenarios",
            new CreateScenarioRequest("Baseline", run.RunId, null, null));
        var baseline = (await bl.Content.ReadFromJsonAsync<ScenarioResponse>())!;
        var al = await client.PostAsJsonAsync($"/api/projects/{projectId}/scenarios",
            new CreateScenarioRequest("Alt", null, baseline.Id, new() { ["vegetationCoverPct"] = 50 }));
        var alternative = (await al.Content.ReadFromJsonAsync<ScenarioResponse>())!;
        await client.PostAsJsonAsync($"/api/scenarios/{alternative.Id}/analyze", new AnalyzeScenarioRequest(null));

        await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(10, "m2", 10m, null, null, null, baseline.Id));
        await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(10, "m2", 6m, null, null, null, alternative.Id));

        var cmp = await client.GetFromJsonAsync<ComparisonResponse>(
            $"/api/projects/{projectId}/comparison?baselineId={baseline.Id}&alternativeId={alternative.Id}");
        cmp!.Cost.Status.Should().Be("Calculated");
        cmp.Cost.BaselineTotal.Should().Be(100m);
        cmp.Cost.AlternativeTotal.Should().Be(60m);
        cmp.Cost.Delta.Should().Be(-40m);
    }

    [Fact]
    public async Task Get_List_Guards()
    {
        var (alice, projectId) = await SetupAsync();
        var created = await alice.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(2, "m2", 5m, null, null, null, null));
        var estimate = (await created.Content.ReadFromJsonAsync<CostEstimateResponse>())!;

        (await alice.GetAsync($"/api/cost-estimates/{estimate.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await alice.GetFromJsonAsync<CostEstimateResponse[]>($"/api/projects/{projectId}/cost-estimates");
        list.Should().ContainSingle();

        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"cost-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var bobAuth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var bob = _factory.CreateClient();
        bob.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobAuth.AccessToken);
        (await bob.GetAsync($"/api/cost-estimates/{estimate.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await alice.GetAsync($"/api/cost-estimates/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _anon.GetAsync($"/api/cost-estimates/{estimate.Id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
