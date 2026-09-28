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
using Urbanova.Domain;
using Urbanova.Domain.Entities;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.IntegrationTests;

/// <summary>Phase 10: recommendations over HTTP against LocalDB (requires MSSQLLocalDB).</summary>
public sealed class RecommendationsTests : IAsyncLifetime
{
    private const string TestCs =
        "Server=(localdb)\\MSSQLLocalDB;Database=Urbanova_Test;Trusted_Connection=True;TrustServerCertificate=True";
    private const string TestKey = "phase10-integration-test-key-0123456789abcdef";

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

    private async Task<(HttpClient Client, Guid ProjectId, FileResponse File)> SetupWithFileAsync()
    {
        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"rc-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Rc", null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(SquareGeoJson)), "file", "g.geojson" },
        };
        var up = await client.PostAsync($"/api/projects/{project.Id}/files", form);
        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;
        return (client, project.Id, file);
    }

    [Fact]
    public async Task ModerateRun_YieldsPreventive_Idempotent()
    {
        var (client, projectId, file) = await SetupWithFileAsync();
        var an = await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, [])); // 32°C → Moderate
        an.StatusCode.Should().Be(HttpStatusCode.Created);

        var first = await client.GetAsync($"/api/projects/{projectId}/recommendations");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var recs = (await first.Content.ReadFromJsonAsync<RecommendationResponse[]>())!;
        var rec = recs.Should().ContainSingle().Subject;
        rec.PolygonIndex.Should().Be(0);
        rec.EvidenceLevel.Should().Be(EvidenceLevel.Estimated.ToString());
        rec.Confidence.Should().BeNull();
        rec.Cost.Status.Should().Be("Unavailable");

        var second = await client.GetAsync($"/api/projects/{projectId}/recommendations");
        var recs2 = (await second.Content.ReadFromJsonAsync<RecommendationResponse[]>())!;
        recs2.Should().HaveCount(1, "deterministic regeneration is idempotent in content");
        recs2[0].Intervention.Should().Be(rec.Intervention);
    }

    [Fact]
    public async Task RepeatedRetrieval_PreservesCostLink_AndStableIds()
    {
        var (client, projectId, file) = await SetupWithFileAsync();
        await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, []));

        var first = (await client.GetFromJsonAsync<RecommendationResponse[]>(
            $"/api/projects/{projectId}/recommendations"))!;
        var rec = first.Should().ContainSingle().Subject;

        var cost = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(100, "m2", 8.50m, null, "USD", rec.Id, null));
        cost.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = (await client.GetFromJsonAsync<RecommendationResponse[]>(
            $"/api/projects/{projectId}/recommendations"))!;
        var again = second.Should().ContainSingle().Subject;
        again.Id.Should().Be(rec.Id, "retrieval must be non-destructive for an immutable run");
        again.Intervention.Should().Be(rec.Intervention);
        again.Cost.Status.Should().Be("Calculated", "the persisted link is the source of truth");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Recommendations.SingleAsync(r => r.Id == rec.Id);
            row.CostEstimateId.Should().NotBeNull("repeated GET must not orphan the linked estimate");
            var estimate = await db.CostEstimates.SingleAsync(e => e.Total == 850m && e.ProjectId == projectId);
            estimate.RecommendationId.Should().Be(rec.Id);
        }
    }

    [Fact]
    public async Task Get_AfterLinkedCalculatedEstimate_ExposesCalculatedCostStatus()
    {
        var (client, projectId, file) = await SetupWithFileAsync();
        await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, []));

        var first = (await client.GetFromJsonAsync<RecommendationResponse[]>(
            $"/api/projects/{projectId}/recommendations"))!;
        var rec = first.Should().ContainSingle().Subject;
        rec.Cost.Status.Should().Be("Unavailable");

        var cost = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(100, "m2", 8.50m, null, "USD", rec.Id, null));
        cost.StatusCode.Should().Be(HttpStatusCode.Created);

        var res = await client.GetAsync($"/api/projects/{projectId}/recommendations");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var again = ((await res.Content.ReadFromJsonAsync<RecommendationResponse[]>())!)
            .Should().ContainSingle().Subject;
        again.Id.Should().Be(rec.Id);
        again.Cost.Status.Should().Be("Calculated");
        again.Cost.Reason.Should().BeNull();
        again.RuleCode.Should().Be(rec.RuleCode);
        again.EvidenceLevel.Should().Be(rec.EvidenceLevel);
        again.Intervention.Should().Be(rec.Intervention);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Recommendations.CountAsync(r => r.ProjectId == projectId)).Should().Be(1,
                "mapping the linked status must not duplicate recommendations");
            var row = await db.Recommendations.SingleAsync(r => r.Id == rec.Id);
            row.CostEstimateId.Should().NotBeNull();
            var estimate = await db.CostEstimates.SingleAsync(e => e.Id == row.CostEstimateId);
            estimate.Status.Should().Be(CostStatus.Calculated);
            estimate.RecommendationId.Should().Be(rec.Id);
        }
    }

    [Fact]
    public async Task UnlinkedRecommendation_StaysUnavailable_WithHonestReason()
    {
        var (client, projectId, file) = await SetupWithFileAsync();
        await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, []));

        var first = (await client.GetFromJsonAsync<RecommendationResponse[]>(
            $"/api/projects/{projectId}/recommendations"))!;
        var rec = first.Should().ContainSingle().Subject;
        rec.Cost.Status.Should().Be("Unavailable");
        rec.Cost.Reason.Should().NotBeNullOrWhiteSpace();

        var second = (await client.GetFromJsonAsync<RecommendationResponse[]>(
            $"/api/projects/{projectId}/recommendations"))!;
        var again = second.Should().ContainSingle().Subject;
        again.Id.Should().Be(rec.Id);
        again.Cost.Status.Should().Be("Unavailable");
        again.Cost.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Get_AfterLinkedUnavailableEstimate_StaysUnavailable_WithHonestReason()
    {
        var (client, projectId, file) = await SetupWithFileAsync();
        await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, []));

        var first = (await client.GetFromJsonAsync<RecommendationResponse[]>(
            $"/api/projects/{projectId}/recommendations"))!;
        var rec = first.Should().ContainSingle().Subject;

        // Unknown catalog code stores an Unavailable row; linking it to a
        // recommendation is permitted by the current cost contract.
        var cost = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(5, "m2", null, "no-such-item", null, rec.Id, null));
        cost.StatusCode.Should().Be(HttpStatusCode.Created);
        (await cost.Content.ReadFromJsonAsync<CostEstimateResponse>())!.Status.Should().Be("Unavailable");

        var again = ((await client.GetFromJsonAsync<RecommendationResponse[]>(
            $"/api/projects/{projectId}/recommendations"))!)
            .Should().ContainSingle().Subject;
        again.Id.Should().Be(rec.Id);
        again.Cost.Status.Should().Be("Unavailable");
        again.Cost.Reason.Should().NotBeNullOrWhiteSpace();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Recommendations.SingleAsync(r => r.Id == rec.Id);
            row.CostEstimateId.Should().NotBeNull("the unavailable link must survive reads");
        }
    }

    [Fact]
    public async Task Recs_CarryRuleCode_AndReferences()
    {
        var (client, projectId, file) = await SetupWithFileAsync();
        await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, []));

        var res = await client.GetAsync($"/api/projects/{projectId}/recommendations");
        var rec = ((await res.Content.ReadFromJsonAsync<RecommendationResponse[]>())!)
            .Should().ContainSingle().Subject;
        rec.RuleCode.Should().Be("HEAT-PREVENT-001");
        rec.ScientificReferences.Should().NotBeEmpty();
    }

    [Fact]
    public async Task InactiveRule_BlocksGeneration_WithNoEvidence()
    {
        var (client, projectId, file) = await SetupWithFileAsync();
        await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, [])); // Moderate → HEAT-PREVENT-001

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rule = await db.RecommendationRules.SingleAsync(r => r.Code == "HEAT-PREVENT-001");
            rule.IsActive = false;
            await db.SaveChangesAsync();
        }

        var res = await client.GetAsync($"/api/projects/{projectId}/recommendations");
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res.Content.ReadAsStringAsync()).Should().Contain("NO_EVIDENCE");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Recommendations.CountAsync()).Should().Be(0, "blocked generation stores nothing");
        }
    }

    [Fact]
    public async Task ProblemRun_YieldsCalculatedIntervention()
    {
        // Engine max (34.4°C) cannot reach ProblemArea, so a stored 36°C run exercises the path.
        var (client, projectId, _) = await SetupWithFileAsync();
        Guid runId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var run = new AnalysisRun
            {
                ProjectId = projectId,
                EngineName = "HeatV01", EngineVersion = "0.1.0-mvp", ConfigVersion = "mvp-001",
                ConfigSnapshotJson = "{}", InputSnapshotJson = """{"parameters":{}}""",
                InputHash = new string('d', 64), Status = AnalysisStatus.Succeeded,
                StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow,
                Result = new AnalysisResult
                {
                    Metric = "LandSurfaceTempProxy", Unit = "Celsius", IsEstimated = true,
                    ValuesJson = """[{"polygonIndex":0,"area":4,"value":36,"classification":"ProblemArea"}]""",
                    ClassificationSummaryJson = """{"acceptable":0,"moderate":0,"problemArea":1}""",
                },
            };
            db.AnalysisRuns.Add(run);
            await db.SaveChangesAsync();
            runId = run.Id;
        }

        var res = await client.GetAsync($"/api/projects/{projectId}/recommendations?runId={runId}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rec = ((await res.Content.ReadFromJsonAsync<RecommendationResponse[]>())!)
            .Should().ContainSingle().Subject;
        rec.EvidenceLevel.Should().Be(EvidenceLevel.Calculated.ToString());
        rec.Intervention.Should().Contain("vegetation");
        rec.ExpectedImpact.Should().NotBeNull();
    }

    [Fact]
    public async Task NoAnalysis_Returns400_ForeignRun_Returns404_CrossOwner_Returns403()
    {
        var (alice, projectId, _) = await SetupWithFileAsync();

        var pr = await alice.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Empty", null, null));
        var empty = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;
        var noAnalysis = await alice.GetAsync($"/api/projects/{empty.Id}/recommendations");
        noAnalysis.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await alice.GetAsync($"/api/projects/{projectId}/recommendations?runId={Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"rc-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var bobAuth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var bob = _factory.CreateClient();
        bob.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobAuth.AccessToken);
        (await bob.GetAsync($"/api/projects/{projectId}/recommendations"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _anon.GetAsync($"/api/projects/{projectId}/recommendations"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnknownRule_BlocksGeneration_WithNoEvidence_AndPersistsNothing()
    {
        var (client, projectId, file) = await SetupWithFileAsync();
        var an = await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, [])); // 32°C → Moderate → HEAT-PREVENT-001
        an.StatusCode.Should().Be(HttpStatusCode.Created);
        var runId = (await an.Content.ReadFromJsonAsync<AnalysisRunResponse>())!.RunId;

        // Empty the registry through the database (no fake engine, no production
        // change): the engine-emitted code is now absent, so rule resolution must
        // block the whole set before writing anything.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.RecommendationRules.ExecuteDeleteAsync();
        }

        var res = await client.GetAsync($"/api/projects/{projectId}/recommendations?runId={runId}");
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("NO_EVIDENCE");
        doc.RootElement.GetProperty("detail").GetString().Should().Contain("HEAT-PREVENT-001");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Recommendations.CountAsync(r => r.ProjectId == projectId)).Should().Be(0);
            (await db.Recommendations.CountAsync(r => r.AnalysisRunId == runId)).Should().Be(0,
                "blocked generation stores nothing, not even a partial set");
            var run = await db.AnalysisRuns.Include(r => r.Result).SingleAsync(r => r.Id == runId);
            run.Status.Should().Be(Domain.AnalysisStatus.Succeeded);
            run.Result.Should().NotBeNull("the failed recommendation read must not touch the run");
        }
    }

    [Fact]
    public async Task AcceptableRun_RepeatedRetrieval_ReturnsEmptyWithoutPersistence()
    {
        var (client, projectId, file) = await SetupWithFileAsync();
        var an = await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, new() { ["vegetationCoverPct"] = 100.0 })); // 27°C → Acceptable
        an.StatusCode.Should().Be(HttpStatusCode.Created);

        var first = await client.GetAsync($"/api/projects/{projectId}/recommendations");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.Content.ReadFromJsonAsync<RecommendationResponse[]>())
            .Should().BeEmpty();

        var second = await client.GetAsync($"/api/projects/{projectId}/recommendations");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadFromJsonAsync<RecommendationResponse[]>())
            .Should().BeEmpty("repeated retrieval of an empty result stores nothing");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Recommendations.CountAsync(r => r.ProjectId == projectId)).Should().Be(0);
        }
    }

    [Fact]
    public async Task LinkedCost_DoesNotReclassifyStoredFeasibility()
    {
        var (client, projectId, file) = await SetupWithFileAsync();
        await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, []));

        var before = (await client.GetFromJsonAsync<RecommendationResponse[]>(
            $"/api/projects/{projectId}/recommendations"))!.Should().ContainSingle().Subject;
        before.Cost.Status.Should().Be("Unavailable");
        before.Feasibility.Should().NotBeNullOrWhiteSpace();

        var cost = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(100, "m2", 8.50m, null, "USD", before.Id, null));
        cost.StatusCode.Should().Be(HttpStatusCode.Created);

        var after = (await client.GetFromJsonAsync<RecommendationResponse[]>(
            $"/api/projects/{projectId}/recommendations"))!.Should().ContainSingle().Subject;
        after.Id.Should().Be(before.Id, "linking a cost must not duplicate the recommendation");
        after.Feasibility.Should().Be(before.Feasibility, "stored feasibility is cost-independent");
        after.Cost.Status.Should().Be("Calculated");
        after.Feasibility.Should().NotContain("afford")
            .And.NotContain("Validat", "a linked price adds no affordability or validation wording");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Recommendations.CountAsync(r => r.ProjectId == projectId)).Should().Be(1);
            (await db.Recommendations.SingleAsync(r => r.Id == before.Id))
                .Feasibility.Should().Be(before.Feasibility);
        }
    }
}
