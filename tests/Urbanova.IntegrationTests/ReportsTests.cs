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
using Urbanova.Domain;
using Urbanova.Domain.Entities;
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
        root.GetProperty("costs").GetProperty("status").GetString().Should().Be("Calculated");
        root.GetProperty("costs").GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("costs").GetProperty("currency").GetString().Should().Be("USD");
        root.GetProperty("decisionSummary").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("caveats").GetArrayLength().Should().BeGreaterThan(0);

        // Version increments per project.
        var second = await client.PostAsJsonAsync($"/api/projects/{projectId}/reports",
            new CreateReportRequest(null, null, null));
        (await second.Content.ReadFromJsonAsync<ReportResponse>())!.Version.Should().Be(2);
    }

    private async Task<(HttpClient Client, Guid ProjectId)> SetupCostProjectAsync(string name)
    {
        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"rp-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest(name, null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;
        return (client, project.Id);
    }

    [Fact]
    public async Task GenerateJson_SameCurrency_SumsTotal()
    {
        var (client, projectId) = await SetupCostProjectAsync("CostSame");

        var first = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(10, "m2", 10m, null, "USD", null, null));
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var secondCost = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(10, "m2", 6m, null, "USD", null, null));
        secondCost.StatusCode.Should().Be(HttpStatusCode.Created);

        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/reports",
            new CreateReportRequest("Json", null, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        using var doc = JsonDocument.Parse((await res.Content.ReadFromJsonAsync<ReportResponse>())!.Content!);
        var costs = doc.RootElement.GetProperty("costs");
        costs.GetProperty("status").GetString().Should().Be("Calculated");
        costs.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
        costs.GetProperty("calculatedTotal").GetDecimal().Should().Be(160m);
        costs.GetProperty("currency").GetString().Should().Be("USD");
        costs.GetProperty("calculatedCount").GetInt32().Should().Be(2);

        doc.RootElement.GetProperty("decisionSummary").EnumerateArray()
            .Select(e => e.GetString()).Should().Contain(s => s!.Contains("160") && s.Contains("USD"));

        var htmlRes = await client.PostAsJsonAsync($"/api/projects/{projectId}/reports",
            new CreateReportRequest("Html", null, null));
        var htmlReport = (await htmlRes.Content.ReadFromJsonAsync<ReportResponse>())!;
        var html = await (await client.GetAsync($"/api/reports/{htmlReport.Id}/file")).Content.ReadAsStringAsync();
        html.Should().Contain("total 160").And.Contain("USD");
    }

    [Fact]
    public async Task GenerateJson_MixedCurrencies_NoSingleTotal()
    {
        var (client, projectId) = await SetupCostProjectAsync("CostMixed");

        var usd = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(10, "m2", 10m, null, "USD", null, null));
        usd.StatusCode.Should().Be(HttpStatusCode.Created);
        (await usd.Content.ReadFromJsonAsync<CostEstimateResponse>())!.Status.Should().Be("Calculated");
        var eur = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(10, "m2", 6m, null, "EUR", null, null));
        eur.StatusCode.Should().Be(HttpStatusCode.Created);
        (await eur.Content.ReadFromJsonAsync<CostEstimateResponse>())!.Status.Should().Be("Calculated");

        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/reports",
            new CreateReportRequest("Json", null, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        using var doc = JsonDocument.Parse((await res.Content.ReadFromJsonAsync<ReportResponse>())!.Content!);
        var costs = doc.RootElement.GetProperty("costs");
        costs.GetProperty("status").GetString().Should().Be("Unavailable");
        costs.GetProperty("reason").GetString().Should().Contain("multiple currencies");
        costs.GetProperty("calculatedTotal").ValueKind.Should().Be(JsonValueKind.Null);
        costs.GetProperty("currency").ValueKind.Should().Be(JsonValueKind.Null);
        costs.GetProperty("calculatedCount").GetInt32().Should().Be(2);

        doc.RootElement.GetProperty("decisionSummary").EnumerateArray()
            .Select(e => e.GetString()).Should().NotContain(s => s!.Contains("160"));

        var htmlRes = await client.PostAsJsonAsync($"/api/projects/{projectId}/reports",
            new CreateReportRequest("Html", null, null));
        var htmlReport = (await htmlRes.Content.ReadFromJsonAsync<ReportResponse>())!;
        var html = await (await client.GetAsync($"/api/reports/{htmlReport.Id}/file")).Content.ReadAsStringAsync();
        html.Should().Contain("multiple currencies").And.NotContain("160");
    }

    [Fact]
    public async Task GenerateJson_NoCalculatedEstimates_HonestlyUnavailable()
    {
        var (client, projectId) = await SetupCostProjectAsync("CostNone");

        var unavailable = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(3, "m2", null, "unobtanium-m2", null, null, null));
        unavailable.StatusCode.Should().Be(HttpStatusCode.Created);
        (await unavailable.Content.ReadFromJsonAsync<CostEstimateResponse>())!.Status.Should().Be("Unavailable");

        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/reports",
            new CreateReportRequest("Json", null, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        using var doc = JsonDocument.Parse((await res.Content.ReadFromJsonAsync<ReportResponse>())!.Content!);
        var costs = doc.RootElement.GetProperty("costs");
        costs.GetProperty("status").GetString().Should().Be("Unavailable");
        costs.GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace();
        costs.GetProperty("calculatedTotal").ValueKind.Should().Be(JsonValueKind.Null);
        costs.GetProperty("currency").ValueKind.Should().Be(JsonValueKind.Null);
        costs.GetProperty("calculatedCount").GetInt32().Should().Be(0);
        costs.GetProperty("unavailableCount").GetInt32().Should().Be(1);

        var htmlRes = await client.PostAsJsonAsync($"/api/projects/{projectId}/reports",
            new CreateReportRequest("Html", null, null));
        var htmlReport = (await htmlRes.Content.ReadFromJsonAsync<ReportResponse>())!;
        var html = await (await client.GetAsync($"/api/reports/{htmlReport.Id}/file")).Content.ReadAsStringAsync();
        html.Should().NotContain("total 0 USD");
    }

    [Fact]
    public async Task GenerateJson_CalculatedPlusUnavailable_CountsTruthful()
    {
        var (client, projectId) = await SetupCostProjectAsync("CostPartial");

        var calculated = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(10, "m2", 10m, null, "USD", null, null));
        calculated.StatusCode.Should().Be(HttpStatusCode.Created);
        var unavailable = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(3, "m2", null, "unobtanium-m2", null, null, null));
        unavailable.StatusCode.Should().Be(HttpStatusCode.Created);

        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/reports",
            new CreateReportRequest("Json", null, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        using var doc = JsonDocument.Parse((await res.Content.ReadFromJsonAsync<ReportResponse>())!.Content!);
        var costs = doc.RootElement.GetProperty("costs");
        costs.GetProperty("status").GetString().Should().Be("Calculated");
        costs.GetProperty("calculatedTotal").GetDecimal().Should().Be(100m);
        costs.GetProperty("currency").GetString().Should().Be("USD");
        costs.GetProperty("calculatedCount").GetInt32().Should().Be(1);
        costs.GetProperty("unavailableCount").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("caveats").GetArrayLength().Should().BeGreaterThan(0);
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
        reported.GetProperty("feasibility").GetString().Should().Be(rec.Feasibility,
            "the report passes stored feasibility through without rewriting it from the cost");

        // Linked calculated estimate must surface — never the old hardcoded Unavailable.
        // Report regen wipes rec rows, so this also proves the pre-regen link snapshot.
        reported.GetProperty("costStatus").GetString().Should().Be("Calculated");
        var detail = reported.GetProperty("cost");
        detail.GetProperty("total").GetDecimal().Should().Be(850m);
        detail.GetProperty("currency").GetString().Should().Be("USD");
        detail.GetProperty("quantitySource").GetString().Should().Be("UserProvided");
        detail.GetProperty("priceSource").GetString().Should().Be("user-provided");

        // DecisionSummary stays neutral: no feasibility verdict, no winner language.
        var summary = doc.RootElement.GetProperty("decisionSummary")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        summary.Should().OnlyContain(s => !s!.ToLowerInvariant().Contains("feasib"));
        summary.Should().OnlyContain(s =>
            !s!.Contains("winner") && !s.Contains("best scenario") && !s.Contains("affordable"));
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

    private async Task<(HttpClient Client, Guid ProjectId, Guid FileId, Guid DisplayedRunId,
        Guid BaselineId, Guid AlternativeId, Guid BaselineRunId, Guid AlternativeRunId)>
        SetupSpatialDivergenceAsync()
    {
        // Displayed project-latest run (run3) is deliberately newer than both
        // comparison pair runs: the report must attribute each section correctly.
        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"rp-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("SpatialCtx", null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(SquareGeoJson)), "file", "g.geojson" },
        };
        var up = await client.PostAsync($"/api/projects/{project.Id}/files", form);
        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;

        var an1 = await client.PostAsJsonAsync($"/api/projects/{project.Id}/analysis",
            new AnalyzeRequest(file.Id, [])); // 32°C → Moderate
        an1.StatusCode.Should().Be(HttpStatusCode.Created);

        var run1 = (await an1.Content.ReadFromJsonAsync<AnalysisRunResponse>())!;
        var bl = await client.PostAsJsonAsync($"/api/projects/{project.Id}/scenarios",
            new CreateScenarioRequest("Baseline", run1.RunId, null, null));
        var baseline = (await bl.Content.ReadFromJsonAsync<ScenarioResponse>())!;
        var al = await client.PostAsJsonAsync($"/api/projects/{project.Id}/scenarios",
            new CreateScenarioRequest("Green", null, baseline.Id, new() { ["albedo"] = 0.0 }));
        var alternative = (await al.Content.ReadFromJsonAsync<ScenarioResponse>())!;
        await client.PostAsJsonAsync($"/api/scenarios/{alternative.Id}/analyze", new AnalyzeScenarioRequest(null));

        var an3 = await client.PostAsJsonAsync($"/api/projects/{project.Id}/analysis",
            new AnalyzeRequest(file.Id, new() { ["vegetationCoverPct"] = 40.0 })); // 30°C → Moderate, latest
        an3.StatusCode.Should().Be(HttpStatusCode.Created);
        var run3 = (await an3.Content.ReadFromJsonAsync<AnalysisRunResponse>())!;

        var cmp = await client.GetFromJsonAsync<ComparisonResponse>(
            $"/api/projects/{project.Id}/comparison?baselineId={baseline.Id}&alternativeId={alternative.Id}");
        return (client, project.Id, file.Id, run3.RunId,
            baseline.Id, alternative.Id, cmp!.Baseline.RunId, cmp.Alternative.RunId);
    }

    [Fact]
    public async Task GenerateJson_SpatialReferences_FreezeExactContexts()
    {
        var (client, projectId, fileId, displayedRunId,
            baselineId, alternativeId, baselineRunId, alternativeRunId) =
            await SetupSpatialDivergenceAsync();

        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/reports",
            new CreateReportRequest("Json", baselineId, alternativeId));
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        using var doc = JsonDocument.Parse((await res.Content.ReadFromJsonAsync<ReportResponse>())!.Content!);
        var root = doc.RootElement;
        var refs = root.GetProperty("spatialReferences");
        refs.GetProperty("polygonIndexBase").GetInt32().Should().Be(0);

        var displayed = refs.GetProperty("displayedAnalysis");
        displayed.GetProperty("analysisRunId").GetString().Should().Be(displayedRunId.ToString());
        root.GetProperty("analysis").GetProperty("runId").GetString().Should().Be(displayedRunId.ToString());
        displayed.GetProperty("sourceEngineeringFileId").GetString().Should().Be(fileId.ToString());
        displayed.GetProperty("liveEngineeringFileId").GetString().Should().Be(fileId.ToString());
        displayed.GetProperty("polygonCount").GetInt32().Should().Be(
            root.GetProperty("analysis").GetProperty("values").GetArrayLength());
        displayed.GetProperty("geometryCrs").ValueKind.Should().Be(JsonValueKind.Null);
        displayed.GetProperty("retrieval").GetProperty("method").GetString().Should().Be("POST");
        displayed.GetProperty("retrieval").GetProperty("relativePath").GetString()
            .Should().StartWith("/api/").And.Contain("extract-geometry");

        var recCtx = refs.GetProperty("recommendationContext");
        recCtx.GetProperty("recommendationAnalysisRunId").GetString().Should().Be(displayedRunId.ToString());
        recCtx.GetProperty("sameAsDisplayedAnalysis").GetBoolean().Should().BeTrue();
        recCtx.GetProperty("recommendationCount").GetInt32().Should().Be(1);

        var baselineRef = refs.GetProperty("comparisonBaseline");
        baselineRef.GetProperty("scenarioId").GetString().Should().Be(baselineId.ToString());
        baselineRef.GetProperty("analysisRunId").GetString().Should().Be(baselineRunId.ToString());
        var alternativeRef = refs.GetProperty("comparisonAlternative");
        alternativeRef.GetProperty("scenarioId").GetString().Should().Be(alternativeId.ToString());
        alternativeRef.GetProperty("analysisRunId").GetString().Should().Be(alternativeRunId.ToString());

        // Intentional mixed scope: displayed run differs from both pair runs, and
        // the pair sides are not shared with each other.
        displayed.GetProperty("analysisRunId").GetString().Should()
            .NotBe(baselineRef.GetProperty("analysisRunId").GetString())
            .And.NotBe(alternativeRef.GetProperty("analysisRunId").GetString());
        baselineRef.GetProperty("analysisRunId").GetString().Should()
            .NotBe(alternativeRef.GetProperty("analysisRunId").GetString());

        // Each reference carries its run's persisted identity (hashes from the snapshot).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var run = await db.AnalysisRuns.SingleAsync(r => r.Id == displayedRunId);
            displayed.GetProperty("inputHash").GetString().Should().Be(run.InputHash);
            using var snapshot = JsonDocument.Parse(run.InputSnapshotJson);
            displayed.GetProperty("geometryHash").GetString().Should().Be(
                snapshot.RootElement.GetProperty("geometryHash").GetString());
            displayed.GetProperty("fileHash").GetString().Should().Be(
                snapshot.RootElement.GetProperty("fileHash").GetString());
        }

        refs.ToString().Should().NotContain("rings").And.NotContain("coordinates");
        root.GetProperty("project").GetProperty("name").GetString().Should().Be("SpatialCtx");
        root.GetProperty("comparison").GetProperty("meanDelta").ValueKind.Should()
            .NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GenerateJson_NoComparison_NullSpatialComparisonRefs()
    {
        var (client, projectId, _, displayedRunId, _, _, _, _) = await SetupSpatialDivergenceAsync();

        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/reports",
            new CreateReportRequest("Json", null, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        using var doc = JsonDocument.Parse((await res.Content.ReadFromJsonAsync<ReportResponse>())!.Content!);
        var refs = doc.RootElement.GetProperty("spatialReferences");

        refs.GetProperty("displayedAnalysis").GetProperty("analysisRunId").GetString()
            .Should().Be(displayedRunId.ToString());
        refs.GetProperty("recommendationContext").GetProperty("sameAsDisplayedAnalysis")
            .GetBoolean().Should().BeTrue();
        refs.GetProperty("comparisonBaseline").ValueKind.Should().Be(JsonValueKind.Null);
        refs.GetProperty("comparisonAlternative").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GenerateJson_SourceDeleted_PreservesHistoricalIdentity()
    {
        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"rp-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("SpatialGone", null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(SquareGeoJson)), "file", "g.geojson" },
        };
        var up = await client.PostAsync($"/api/projects/{project.Id}/files", form);
        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;
        var an = await client.PostAsJsonAsync($"/api/projects/{project.Id}/analysis",
            new AnalyzeRequest(file.Id, []));
        var runId = (await an.Content.ReadFromJsonAsync<AnalysisRunResponse>())!.RunId;

        // Delete through the real API: the run survives with a nulled FK (SET NULL).
        (await client.DeleteAsync($"/api/files/{file.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.AnalysisRuns.SingleAsync(r => r.Id == runId)).EngineeringFileId.Should().BeNull();
        }

        var res = await client.PostAsJsonAsync($"/api/projects/{project.Id}/reports",
            new CreateReportRequest("Json", null, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        using var doc = JsonDocument.Parse((await res.Content.ReadFromJsonAsync<ReportResponse>())!.Content!);
        var displayed = doc.RootElement.GetProperty("spatialReferences").GetProperty("displayedAnalysis");
        displayed.GetProperty("sourceEngineeringFileId").GetString().Should().Be(file.Id.ToString(),
            "historical snapshot identity survives source deletion");
        displayed.GetProperty("liveEngineeringFileId").ValueKind.Should().Be(JsonValueKind.Null);
        displayed.GetProperty("inputHash").GetString().Should().NotBeNullOrWhiteSpace();
        displayed.GetProperty("geometryHash").GetString().Should().NotBeNullOrWhiteSpace();
        displayed.GetProperty("fileHash").GetString().Should().NotBeNullOrWhiteSpace();
        displayed.GetProperty("polygonCount").GetInt32().Should().Be(
            doc.RootElement.GetProperty("analysis").GetProperty("values").GetArrayLength());
        displayed.GetProperty("retrieval").GetProperty("requiresSourceFile")
            .GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("analysis").GetProperty("values").GetArrayLength()
            .Should().BeGreaterThan(0, "stored numeric content stays readable");
    }

    [Fact]
    public async Task GenerateJson_EvaluatedEmpty_KeepsRecommendationContext()
    {
        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"rp-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("SpatialEmpty", null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(SquareGeoJson)), "file", "g.geojson" },
        };
        var up = await client.PostAsync($"/api/projects/{project.Id}/files", form);
        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;
        var an = await client.PostAsJsonAsync($"/api/projects/{project.Id}/analysis",
            new AnalyzeRequest(file.Id, new() { ["vegetationCoverPct"] = 100.0 })); // 27°C → Acceptable
        var runId = (await an.Content.ReadFromJsonAsync<AnalysisRunResponse>())!.RunId;

        var res = await client.PostAsJsonAsync($"/api/projects/{project.Id}/reports",
            new CreateReportRequest("Json", null, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        using var doc = JsonDocument.Parse((await res.Content.ReadFromJsonAsync<ReportResponse>())!.Content!);
        var refs = doc.RootElement.GetProperty("spatialReferences");
        refs.GetProperty("displayedAnalysis").GetProperty("analysisRunId").GetString()
            .Should().Be(runId.ToString());

        var recCtx = refs.GetProperty("recommendationContext");
        recCtx.ValueKind.Should().NotBe(JsonValueKind.Null,
            "an evaluated run keeps context even with zero rows");
        recCtx.GetProperty("recommendationAnalysisRunId").GetString().Should().Be(runId.ToString());
        recCtx.GetProperty("sameAsDisplayedAnalysis").GetBoolean().Should().BeTrue();
        recCtx.GetProperty("recommendationCount").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("recommendations").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GenerateJson_MalformedSnapshot_StaysHonestWithoutCrash()
    {
        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"rp-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("SpatialBroken", null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;

        // Seeded legacy-style row: valid result, unparseable snapshot (established
        // direct-seeding pattern; fresh per-test database).
        Guid runId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var run = new AnalysisRun
            {
                ProjectId = project.Id,
                EngineName = "HeatV01", EngineVersion = "0.1.0-mvp", ConfigVersion = "mvp-001",
                ConfigSnapshotJson = "{}", InputSnapshotJson = "not-json",
                InputHash = new string('e', 64), Status = AnalysisStatus.Succeeded,
                StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow,
                Result = new AnalysisResult
                {
                    Metric = "LandSurfaceTempProxy", Unit = "Celsius", IsEstimated = true,
                    ValuesJson = """[{"polygonIndex":0,"area":4,"value":32,"classification":"Moderate"}]""",
                    ClassificationSummaryJson = """{"acceptable":0,"moderate":1,"problemArea":0}""",
                },
            };
            db.AnalysisRuns.Add(run);
            await db.SaveChangesAsync();
            runId = run.Id;
        }

        var res = await client.PostAsJsonAsync($"/api/projects/{project.Id}/reports",
            new CreateReportRequest("Json", null, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        using var doc = JsonDocument.Parse((await res.Content.ReadFromJsonAsync<ReportResponse>())!.Content!);
        var displayed = doc.RootElement.GetProperty("spatialReferences").GetProperty("displayedAnalysis");
        displayed.GetProperty("analysisRunId").GetString().Should().Be(runId.ToString());
        displayed.GetProperty("sourceEngineeringFileId").ValueKind.Should().Be(JsonValueKind.Null);
        displayed.GetProperty("geometryHash").ValueKind.Should().Be(JsonValueKind.Null);
        displayed.GetProperty("fileHash").ValueKind.Should().Be(JsonValueKind.Null);
        displayed.GetProperty("inputHash").GetString().Should().Be(new string('e', 64));
    }
}
