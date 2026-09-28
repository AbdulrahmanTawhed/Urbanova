using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
using Urbanova.Application.Reporting;
using Urbanova.Application.Scenarios;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.IntegrationTests;

/// <summary>
/// Phase 13: the PRD §23 Definition of Done as one executable chain —
/// Project → File → Validate → Geometry → Analysis → Baseline → Alternative →
/// Modify → Recalculate → Compare → Recommend → Cost → Report — against LocalDB.
/// </summary>
public sealed class WorkflowTests : IAsyncLifetime
{
    private const string TestCs =
        "Server=(localdb)\\MSSQLLocalDB;Database=Urbanova_Test;Trusted_Connection=True;TrustServerCertificate=True";
    private const string TestKey = "phase13-integration-test-key-0123456789abcdef";

    private const string SiteGeoJson = """
        { "type": "FeatureCollection", "features": [
          { "type": "Feature", "properties": { "name": "Lot" },
            "geometry": { "type": "Polygon",
              "coordinates": [[[0,0],[2,0],[2,2],[0,2],[0,0]]] } } ] }
        """;

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

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

        var reg = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"wf-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task FullWorkflow_ProjectToReport()
    {
        // 1. Project (+ site).
        var pr = await _client.PostAsJsonAsync("/api/projects",
            new CreateProjectRequest("Workflow Town", "DoD proof",
                new SiteInput("Main St 1", 52.5, 13.4, null, "EPSG:4326", 1500)));
        pr.StatusCode.Should().Be(HttpStatusCode.Created);
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;
        project.Site!.Crs.Should().Be("EPSG:4326");

        // 2–3. Upload + validate file.
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(SiteGeoJson)), "file", "site.geojson" },
        };
        var up = await _client.PostAsync($"/api/projects/{project.Id}/files", form);
        up.StatusCode.Should().Be(HttpStatusCode.Created);
        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;
        file.ValidationStatus.Should().Be("Valid");

        var val = await _client.PostAsync($"/api/files/{file.Id}/validate", null);
        val.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Geometry extraction.
        var geoRes = await _client.PostAsync($"/api/files/{file.Id}/extract-geometry", null);
        geoRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var geometry = (await geoRes.Content.ReadFromJsonAsync<GeometryResponse>())!;
        geometry.TotalArea.Should().BeApproximately(4.0, 1e-9);
        geometry.Crs.Should().Be("EPSG:4326");

        // 5–6. Analysis + stored result.
        var an = await _client.PostAsJsonAsync($"/api/projects/{project.Id}/analysis",
            new AnalyzeRequest(file.Id, []));
        an.StatusCode.Should().Be(HttpStatusCode.Created);
        var run = (await an.Content.ReadFromJsonAsync<AnalysisRunResponse>())!;
        run.Values.Should().ContainSingle();
        run.IsEstimated.Should().BeTrue();

        var fetched = await _client.GetFromJsonAsync<AnalysisRunResponse>($"/api/analysis/{run.RunId}");
        fetched!.InputHash.Should().Be(run.InputHash);

        // 7. Baseline (locked, inherits run inputs).
        var bl = await _client.PostAsJsonAsync($"/api/projects/{project.Id}/scenarios",
            new CreateScenarioRequest("Baseline", run.RunId, null, null));
        var baseline = (await bl.Content.ReadFromJsonAsync<ScenarioResponse>())!;
        baseline.IsLocked.Should().BeTrue();

        // 8–9. Alternative + modify + recalculate (stays Moderate so the report keeps recs).
        var al = await _client.PostAsJsonAsync($"/api/projects/{project.Id}/scenarios",
            new CreateScenarioRequest("Green", null, baseline.Id, new() { ["vegetationCoverPct"] = 100 }));
        var alternative = (await al.Content.ReadFromJsonAsync<ScenarioResponse>())!;

        var upd = await _client.PutAsJsonAsync($"/api/scenarios/{alternative.Id}",
            new UpdateScenarioRequest(null, new() { ["vegetationCoverPct"] = 20 }, alternative.RowVersion));
        upd.StatusCode.Should().Be(HttpStatusCode.OK);
        (await upd.Content.ReadFromJsonAsync<ScenarioResponse>())!.Version.Should().Be(2);

        var recalc = await _client.PostAsJsonAsync($"/api/scenarios/{alternative.Id}/analyze",
            new AnalyzeScenarioRequest(null));
        recalc.StatusCode.Should().Be(HttpStatusCode.Created);
        var altRun = (await recalc.Content.ReadFromJsonAsync<AnalysisRunResponse>())!;
        altRun.Values.Single().Value.Should().BeApproximately(31.0, 1e-9); // 32 − 20×0.05

        // 10. Compare.
        var cmp = await _client.GetFromJsonAsync<ComparisonResponse>(
            $"/api/projects/{project.Id}/comparison?baselineId={baseline.Id}&alternativeId={alternative.Id}");
        cmp!.Environmental.MeanDelta.Should().BeApproximately(-1.0, 1e-9);
        cmp.WhatChanged.Should().ContainSingle();
        cmp.Tradeoffs.Should().NotBeEmpty();

        // 11. Recommendations (evidence-graded, never validated).
        var recs = await _client.GetFromJsonAsync<RecommendationResponse[]>(
            $"/api/projects/{project.Id}/recommendations?runId={run.RunId}");
        recs.Should().NotBeEmpty();
        recs.Should().OnlyContain(r => r.EvidenceLevel != "Validated");
        recs.Should().OnlyContain(r => !r.Confidence.HasValue);

        // 12. Cost estimate (direct price, linked to the recommendation).
        var cost = await _client.PostAsJsonAsync($"/api/projects/{project.Id}/cost-estimates",
            new CreateCostEstimateRequest(100, "m2", 8.50m, null, "USD", recs![0].Id, alternative.Id));
        cost.StatusCode.Should().Be(HttpStatusCode.Created);
        (await cost.Content.ReadFromJsonAsync<CostEstimateResponse>())!.Total.Should().Be(850m);

        // 13. Report with comparison.
        var rep = await _client.PostAsJsonAsync($"/api/projects/{project.Id}/reports",
            new CreateReportRequest("Json", baseline.Id, alternative.Id));
        rep.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = (await rep.Content.ReadFromJsonAsync<ReportResponse>())!;
        using var doc = JsonDocument.Parse(report.Content!);
        var root = doc.RootElement;
        root.GetProperty("project").GetProperty("name").GetString().Should().Be("Workflow Town");
        root.GetProperty("analysis").GetProperty("availability").GetProperty("status").GetString().Should().Be("Available");
        root.GetProperty("comparison").GetProperty("availability").GetProperty("status").GetString().Should().Be("Available");
        root.GetProperty("recommendations").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("decisionSummary").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("caveats").GetArrayLength().Should().BeGreaterThan(0);

        // 14. Everything persisted.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Projects.CountAsync()).Should().Be(1);
        (await db.EngineeringFiles.CountAsync()).Should().Be(1);
        (await db.AnalysisRuns.CountAsync(r => r.Status == Domain.AnalysisStatus.Succeeded)).Should().Be(2);
        (await db.Scenarios.CountAsync()).Should().Be(2);
        (await db.Recommendations.CountAsync()).Should().BeGreaterThan(0);
        (await db.CostEstimates.CountAsync()).Should().Be(1);
        (await db.Reports.CountAsync()).Should().Be(1);
    }
}
