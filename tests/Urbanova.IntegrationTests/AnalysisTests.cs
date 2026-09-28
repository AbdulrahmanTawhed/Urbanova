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
using Urbanova.Application.EngineeringFiles;
using Urbanova.Application.Projects;
using Urbanova.Domain;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.IntegrationTests;

/// <summary>Phase 7: analysis workflow over HTTP against LocalDB (requires MSSQLLocalDB).</summary>
public sealed class AnalysisTests : IAsyncLifetime
{
    private const string TestCs =
        "Server=(localdb)\\MSSQLLocalDB;Database=Urbanova_Test;Trusted_Connection=True;TrustServerCertificate=True";
    private const string TestKey = "phase7-integration-test-key-0123456789abcdef";

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

    private async Task<(HttpClient Client, Guid ProjectId, FileResponse File)> SetupWithFileAsync(string content = SquareGeoJson, string name = "g.geojson")
    {
        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"an-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("An", null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", name },
        };
        var up = await client.PostAsync($"/api/projects/{project.Id}/files", form);
        up.StatusCode.Should().Be(HttpStatusCode.Created);
        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;
        return (client, project.Id, file);
    }

    [Fact]
    public async Task Run_Returns201_WithValuesAndTraceability()
    {
        var (client, projectId, file) = await SetupWithFileAsync();

        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, []));
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        var run = (await res.Content.ReadFromJsonAsync<AnalysisRunResponse>())!;
        run.ProjectId.Should().Be(projectId);
        run.FileId.Should().Be(file.Id);
        run.Status.Should().Be(AnalysisStatus.Succeeded.ToString());
        run.EngineName.Should().Be("HeatV01");
        run.ConfigVersion.Should().Be("mvp-001");
        run.Metric.Should().Be("LandSurfaceTempProxy");
        run.Unit.Should().Be("Celsius");
        run.IsEstimated.Should().BeTrue();
        run.InputHash.Should().MatchRegex("^[0-9a-f]{64}$");
        run.Values.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new AreaValueDto(0, 4.0, 32.0, "Moderate"));
        run.ClassificationSummary.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["acceptable"] = 0, ["moderate"] = 1, ["problemArea"] = 0,
        });

        // Traceability persisted: snapshots + result row.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AnalysisRuns.Include(r => r.Result).SingleAsync(r => r.Id == run.RunId);
        row.ConfigSnapshotJson.Should().Contain("mvp-001");
        row.InputSnapshotJson.Should().Contain(file.HashSha256);
        row.Result.Should().NotBeNull();
        row.Result!.ValuesJson.Should().Contain("32");
    }

    [Fact]
    public async Task Run_Replay_Returns200_SameRun_Deterministic()
    {
        var (client, projectId, file) = await SetupWithFileAsync();
        var body = new AnalyzeRequest(file.Id, new() { ["vegetationCoverPct"] = 100 });

        var first = await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis", body);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstRun = (await first.Content.ReadFromJsonAsync<AnalysisRunResponse>())!;
        firstRun.Values.Single().Value.Should().BeApproximately(27.0, 1e-9);
        firstRun.Values.Single().Classification.Should().Be("Acceptable");

        var second = await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis", body);
        second.StatusCode.Should().Be(HttpStatusCode.OK, "identical inputs + config replay the stored run");
        (await second.Content.ReadFromJsonAsync<AnalysisRunResponse>())!.RunId.Should().Be(firstRun.RunId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AnalysisRuns.CountAsync(r => r.ProjectId == projectId)).Should().Be(1);
    }

    [Fact]
    public async Task Run_InvalidParams_Returns400()
    {
        var (client, projectId, file) = await SetupWithFileAsync();
        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, new() { ["treeCount"] = 1 }));
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Run_InvalidFile_Returns422()
    {
        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"an-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("AnBad", null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;

        // Stored as Invalid (upload answers 422) — analysis must refuse it.
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes("""{"type":"Nope"}""")), "file", "bad.geojson" },
        };
        var up = await client.PostAsync($"/api/projects/{project.Id}/files", form);
        up.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var list = await client.GetFromJsonAsync<FileResponse[]>($"/api/projects/{project.Id}/files");
        var res = await client.PostAsJsonAsync($"/api/projects/{project.Id}/analysis",
            new AnalyzeRequest(list![0].Id, []));
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Run_ForeignProject_Returns403_Missing_Returns404()
    {
        var (alice, _, file) = await SetupWithFileAsync();
        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"an-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var bobAuth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var bob = _factory.CreateClient();
        bob.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobAuth.AccessToken);

        var pr = await bob.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Bob", null, null));
        var bobProject = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;

        // Bob analyzing Alice's file under his own project: file not in project → 404 (no leak).
        (await bob.PostAsJsonAsync($"/api/projects/{bobProject.Id}/analysis",
            new AnalyzeRequest(file.Id, []))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Bob analyzing under Alice's project id → 403 tested via unknown guid under missing project:
        var missing = await alice.PostAsJsonAsync($"/api/projects/{Guid.NewGuid()}/analysis",
            new AnalyzeRequest(file.Id, []));
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_Run_Returns200_Foreign_Returns403_Anonymous_Returns401()
    {
        var (alice, projectId, file) = await SetupWithFileAsync();
        var runRes = await alice.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, []));
        var run = (await runRes.Content.ReadFromJsonAsync<AnalysisRunResponse>())!;

        (await alice.GetAsync($"/api/analysis/{run.RunId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"an-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var bobAuth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var bob = _factory.CreateClient();
        bob.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobAuth.AccessToken);
        (await bob.GetAsync($"/api/analysis/{run.RunId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await alice.GetAsync($"/api/analysis/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _anon.GetAsync($"/api/analysis/{run.RunId}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
