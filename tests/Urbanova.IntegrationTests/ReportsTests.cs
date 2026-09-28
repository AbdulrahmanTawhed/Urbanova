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
using Urbanova.Application.Costing;
using Urbanova.Application.EngineeringFiles;
using Urbanova.Application.Projects;
using Urbanova.Application.Recommendations;
using Urbanova.Application.Reporting;
using Urbanova.Application.Scenarios;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.IntegrationTests;

/// <summary>Phase 12: reporting over HTTP against LocalDB (requires MSSQLLocalDB).</summary>
public sealed class ReportsTests : IAsyncLifetime
{
    private const string TestCs =
        "Server=(localdb)\\MSSQLLocalDB;Database=Urbanova_Test;Trusted_Connection=True;TrustServerCertificate=True";
    private const string TestKey = "phase12-integration-test-key-0123456789abcdef";

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

    private async Task<(HttpClient Client, Guid ProjectId)> SetupFullAsync()
    {
        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"rp-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var pr = await client.PostAsJsonAsync("/api/projects",
            new CreateProjectRequest("Reportville", "A reporting town",
                new SiteInput("Main St 1", 52.5, 13.4, null, "EPSG:4326", 1500)));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(SquareGeoJson)), "file", "g.geojson" },
        };
        var up = await client.PostAsync($"/api/projects/{project.Id}/files", form);
        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;

        var an = await client.PostAsJsonAsync($"/api/projects/{project.Id}/analysis",
            new AnalyzeRequest(file.Id, []));
        var run = (await an.Content.ReadFromJsonAsync<AnalysisRunResponse>())!;

        var bl = await client.PostAsJsonAsync($"/api/projects/{project.Id}/scenarios",
            new CreateScenarioRequest("Baseline", run.RunId, null, null));
        var baseline = (await bl.Content.ReadFromJsonAsync<ScenarioResponse>())!;
        // Alternative stays non-acceptable (albedo 0 → 34.4°C Moderate) so the latest run
        // still yields recommendations in the report.
        var al = await client.PostAsJsonAsync($"/api/projects/{project.Id}/scenarios",
            new CreateScenarioRequest("Green", null, baseline.Id, new() { ["albedo"] = 0.0 }));
        var alternative = (await al.Content.ReadFromJsonAsync<ScenarioResponse>())!;
        await client.PostAsJsonAsync($"/api/scenarios/{alternative.Id}/analyze", new AnalyzeScenarioRequest(null));

        await client.PostAsJsonAsync($"/api/projects/{project.Id}/cost-estimates",
            new CreateCostEstimateRequest(10, "m2", 10m, null, null, null, alternative.Id));

        // Store scenario ids for comparison requests via project-scoped lookup.
        _scenarios[(client, project.Id)] = (baseline.Id, alternative.Id);
        return (client, project.Id);
    }

    private readonly Dictionary<(HttpClient, Guid), (Guid Baseline, Guid Alternative)> _scenarios = [];

    [Fact]
    public async Task GenerateJson_FullReport_WithComparison()
    {
        var (client, projectId) = await SetupFullAsync();
        var (baseline, alternative) = _scenarios[(client, projectId)];

        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/reports",
            new CreateReportRequest("Json", baseline, alternative));
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        var report = (await res.Content.ReadFromJsonAsync<ReportResponse>())!;
        report.Format.Should().Be("Json");
        report.Version.Should().Be(1);
        report.PayloadHash.Should().MatchRegex("^[0-9a-f]{64}$");
        report.HasFile.Should().BeFalse();

        using var doc = JsonDocument.Parse(report.Content!);
        var root = doc.RootElement;
        root.GetProperty("project").GetProperty("name").GetString().Should().Be("Reportville");
        root.GetProperty("site").GetProperty("address").GetString().Should().Be("Main St 1");
        root.GetProperty("analysis").GetProperty("availability").GetProperty("status").GetString().Should().Be("Available");
        root.GetProperty("comparison").GetProperty("availability").GetProperty("status").GetString().Should().Be("Available");
        root.GetProperty("comparison").GetProperty("meanDelta").GetDouble().Should().BeApproximately(2.4, 1e-9);
        root.GetProperty("recommendations").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("costs").GetProperty("calculatedTotal").GetDecimal().Should().Be(100m);
        root.GetProperty("decisionSummary").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("caveats").GetArrayLength().Should().BeGreaterThan(0);

        // Version increments per project.
        var second = await client.PostAsJsonAsync($"/api/projects/{projectId}/reports",
            new CreateReportRequest(null, null, null));
        (await second.Content.ReadFromJsonAsync<ReportResponse>())!.Version.Should().Be(2);
    }

    [Fact]
    public async Task GenerateJson_RecommendationTraceability_AndLinkedCost()
    {
        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"rp-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Traceville", null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(SquareGeoJson)), "file", "g.geojson" },
        };
        var up = await client.PostAsync($"/api/projects/{project.Id}/files", form);
        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;
        await client.PostAsJsonAsync($"/api/projects/{project.Id}/analysis",
            new AnalyzeRequest(file.Id, []));

        var recs = await client.GetFromJsonAsync<RecommendationResponse[]>(
            $"/api/projects/{project.Id}/recommendations");
        var rec = recs.Should().ContainSingle().Subject;

        var cost = await client.PostAsJsonAsync($"/api/projects/{project.Id}/cost-estimates",
            new CreateCostEstimateRequest(100, "m2", 8.50m, null, "USD", rec.Id, null));
        cost.StatusCode.Should().Be(HttpStatusCode.Created);

        var res = await client.PostAsJsonAsync($"/api/projects/{project.Id}/reports",
            new CreateReportRequest("Json", null, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        using var doc = JsonDocument.Parse((await res.Content.ReadFromJsonAsync<ReportResponse>())!.Content!);
        var reported = doc.RootElement.GetProperty("recommendations")[0];
        reported.GetProperty("cause").GetString().Should().NotBeNullOrWhiteSpace();
        reported.GetProperty("expectedImpact").ValueKind.Should().Be(JsonValueKind.Object);
        reported.GetProperty("expectedImpact").GetProperty("targetClassification").GetString()
            .Should().NotBeNullOrWhiteSpace();
        reported.GetProperty("ruleCode").GetString().Should().Be("HEAT-PREVENT-001");
        reported.GetProperty("evidenceSource").GetString().Should().Contain("HeatV01");
        reported.GetProperty("evidenceLevel").GetString().Should().Be("Estimated");
        reported.GetProperty("scientificReferences").GetArrayLength().Should().BeGreaterThan(0);
        reported.GetProperty("feasibility").GetString().Should().NotBeNullOrWhiteSpace();

        // Linked calculated estimate must surface — never the old hardcoded Unavailable.
        // Report regen wipes rec rows, so this also proves the pre-regen link snapshot.
        reported.GetProperty("costStatus").GetString().Should().Be("Calculated");
        var detail = reported.GetProperty("cost");
        detail.GetProperty("total").GetDecimal().Should().Be(850m);
        detail.GetProperty("currency").GetString().Should().Be("USD");
        detail.GetProperty("quantitySource").GetString().Should().Be("UserProvided");
        detail.GetProperty("priceSource").GetString().Should().Be("user-provided");
    }

    [Fact]
    public async Task GenerateJson_UnlinkedRecommendation_StaysUnavailable()
    {
        // SetupFullAsync links its estimate to a scenario, not to a recommendation.
        var (client, projectId) = await SetupFullAsync();

        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/reports",
            new CreateReportRequest("Json", null, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        using var doc = JsonDocument.Parse((await res.Content.ReadFromJsonAsync<ReportResponse>())!.Content!);
        var reported = doc.RootElement.GetProperty("recommendations")[0];
        reported.GetProperty("costStatus").GetString().Should().Be("Unavailable");
        reported.GetProperty("cost").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GenerateHtml_ExposesTraceability_AndTrueCost()
    {
        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"rp-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Traceville", null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(SquareGeoJson)), "file", "g.geojson" },
        };
        var up = await client.PostAsync($"/api/projects/{project.Id}/files", form);
        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;
        await client.PostAsJsonAsync($"/api/projects/{project.Id}/analysis",
            new AnalyzeRequest(file.Id, []));

        var recs = await client.GetFromJsonAsync<RecommendationResponse[]>(
            $"/api/projects/{project.Id}/recommendations");
        var rec = recs.Should().ContainSingle().Subject;
        await client.PostAsJsonAsync($"/api/projects/{project.Id}/cost-estimates",
            new CreateCostEstimateRequest(100, "m2", 8.50m, null, "USD", rec.Id, null));

        var res = await client.PostAsJsonAsync($"/api/projects/{project.Id}/reports",
            new CreateReportRequest("Html", null, null));
        var report = (await res.Content.ReadFromJsonAsync<ReportResponse>())!;
        var html = await (await client.GetAsync($"/api/reports/{report.Id}/file")).Content.ReadAsStringAsync();

        html.Should().Contain("Cause:")
            .And.Contain("Expected impact:")
            .And.Contain("HEAT-PREVENT-001")
            .And.Contain("Calculated — 850")
            .And.Contain("References:")
            .And.NotContain("best scenario")
            .And.NotContain("winner");
    }

    [Fact]
    public async Task GenerateRepeatedReports_KeepsLinkedCalculatedCost()
    {
        // Root-consistency regression: the first report must not consume the
        // Recommendation→CostEstimate link; the second report must match it.
        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"rp-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Repeatville", null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(SquareGeoJson)), "file", "g.geojson" },
        };
        var up = await client.PostAsync($"/api/projects/{project.Id}/files", form);
        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;
        var an = await client.PostAsJsonAsync($"/api/projects/{project.Id}/analysis",
            new AnalyzeRequest(file.Id, []));
        var run = (await an.Content.ReadFromJsonAsync<AnalysisRunResponse>())!;

        var recs = await client.GetFromJsonAsync<RecommendationResponse[]>(
            $"/api/projects/{project.Id}/recommendations");
        var rec = recs.Should().ContainSingle().Subject;

        var cost = await client.PostAsJsonAsync($"/api/projects/{project.Id}/cost-estimates",
            new CreateCostEstimateRequest(100, "m2", 8.50m, null, "USD", rec.Id, null));
        cost.StatusCode.Should().Be(HttpStatusCode.Created);

        var first = await client.PostAsJsonAsync($"/api/projects/{project.Id}/reports",
            new CreateReportRequest("Json", null, null));
        var second = await client.PostAsJsonAsync($"/api/projects/{project.Id}/reports",
            new CreateReportRequest("Json", null, null));

        using var doc1 = JsonDocument.Parse((await first.Content.ReadFromJsonAsync<ReportResponse>())!.Content!);
        using var doc2 = JsonDocument.Parse((await second.Content.ReadFromJsonAsync<ReportResponse>())!.Content!);
        var r1 = doc1.RootElement.GetProperty("recommendations")[0];
        var r2 = doc2.RootElement.GetProperty("recommendations")[0];

        r1.GetProperty("costStatus").GetString().Should().Be("Calculated");
        r2.GetProperty("costStatus").GetString().Should().Be("Calculated",
            "the first report must not destroy the persistent link");
        r2.GetProperty("cost").GetProperty("total").GetDecimal().Should()
            .Be(r1.GetProperty("cost").GetProperty("total").GetDecimal());
        r2.GetProperty("cost").GetProperty("currency").GetString().Should()
            .Be(r1.GetProperty("cost").GetProperty("currency").GetString());
        r2.GetProperty("cost").GetProperty("quantitySource").GetString().Should()
            .Be(r1.GetProperty("cost").GetProperty("quantitySource").GetString());
        r2.GetProperty("cost").GetProperty("priceSource").GetString().Should()
            .Be(r1.GetProperty("cost").GetProperty("priceSource").GetString());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var estimate = await db.CostEstimates
                .SingleAsync(e => e.ProjectId == project.Id && e.Total == 850m);
            estimate.RecommendationId.Should().Be(rec.Id, "report generation must not orphan the estimate");
            var row = await db.Recommendations.SingleAsync(r => r.Id == rec.Id);
            row.CostEstimateId.Should().NotBeNull();
            (await db.Recommendations.CountAsync(r => r.AnalysisRunId == run.RunId)).Should().Be(1,
                "repeat reads/reports must not duplicate recommendations");
        }
    }

    [Fact]
    public async Task GenerateHtml_FileDownloadable()
    {
        var (client, projectId) = await SetupFullAsync();

        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/reports",
            new CreateReportRequest("Html", null, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = (await res.Content.ReadFromJsonAsync<ReportResponse>())!;
        report.HasFile.Should().BeTrue();

        var file = await client.GetAsync($"/api/reports/{report.Id}/file");
        file.StatusCode.Should().Be(HttpStatusCode.OK);
        file.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await file.Content.ReadAsStringAsync()).Should().Contain("Reportville").And.Contain("<html");
    }

    [Fact]
    public async Task Generate_EmptyProject_UnavailableSections_Still200()
    {
        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"rp-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Empty", null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;

        var res = await client.PostAsJsonAsync($"/api/projects/{project.Id}/reports",
            new CreateReportRequest(null, null, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse((await res.Content.ReadFromJsonAsync<ReportResponse>())!.Content!);
        doc.RootElement.GetProperty("analysis").GetProperty("availability").GetProperty("status")
            .GetString().Should().Be("Unavailable");
    }

    [Fact]
    public async Task Generate_BadFormat_Returns400_FileOnJson_Returns404_Guards()
    {
        var (client, projectId) = await SetupFullAsync();

        var bad = await client.PostAsJsonAsync($"/api/projects/{projectId}/reports",
            new CreateReportRequest("Pdf", null, null));
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await client.PostAsJsonAsync($"/api/projects/{projectId}/reports",
            new CreateReportRequest("Json", null, null));
        var report = (await json.Content.ReadFromJsonAsync<ReportResponse>())!;
        (await client.GetAsync($"/api/reports/{report.Id}/file")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/reports/{report.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"rp-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var bobAuth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var bob = _factory.CreateClient();
        bob.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobAuth.AccessToken);
        (await bob.GetAsync($"/api/reports/{report.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await bob.GetAsync($"/api/reports/{report.Id}/file")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/reports/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _anon.GetAsync($"/api/reports/{report.Id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
