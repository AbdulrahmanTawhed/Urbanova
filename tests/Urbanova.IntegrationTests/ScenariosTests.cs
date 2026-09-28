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
using Urbanova.Application.Scenarios;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.IntegrationTests;

/// <summary>Phase 8: scenario workflow over HTTP against LocalDB (requires MSSQLLocalDB).</summary>
public sealed class ScenariosTests : IAsyncLifetime
{
    private const string TestCs =
        "Server=(localdb)\\MSSQLLocalDB;Database=Urbanova_Test;Trusted_Connection=True;TrustServerCertificate=True";
    private const string TestKey = "phase8-integration-test-key-0123456789abcdef";

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
            new RegisterRequest($"sc-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private async Task<(Guid ProjectId, FileResponse File, AnalysisRunResponse Run)> SetupWithRunAsync(HttpClient client)
    {
        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Sc", null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(SquareGeoJson)), "file", "g.geojson" },
        };
        var up = await client.PostAsync($"/api/projects/{project.Id}/files", form);
        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;

        var an = await client.PostAsJsonAsync($"/api/projects/{project.Id}/analysis",
            new AnalyzeRequest(file.Id, new() { ["vegetationCoverPct"] = 10 }));
        var run = (await an.Content.ReadFromJsonAsync<AnalysisRunResponse>())!;
        return (project.Id, file, run);
    }

    [Fact]
    public async Task CreateBaseline_FromRun_InheritsParams_AndLocks()
    {
        var client = await RegisterAsync();
        var (projectId, _, run) = await SetupWithRunAsync(client);

        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/scenarios",
            new CreateScenarioRequest("Baseline", run.RunId, null, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        var baseline = (await res.Content.ReadFromJsonAsync<ScenarioResponse>())!;
        baseline.Kind.Should().Be("Baseline");
        baseline.IsLocked.Should().BeTrue();
        baseline.Version.Should().Be(1);
        baseline.BaseAnalysisRunId.Should().Be(run.RunId);
        baseline.Parameters.Should().BeEquivalentTo(new Dictionary<string, double> { ["vegetationCoverPct"] = 10 });
    }

    [Fact]
    public async Task CreateAlternative_Inherits_AndMerges()
    {
        var client = await RegisterAsync();
        var (projectId, _, run) = await SetupWithRunAsync(client);
        var bl = await client.PostAsJsonAsync($"/api/projects/{projectId}/scenarios",
            new CreateScenarioRequest("Baseline", run.RunId, null, null));
        var baseline = (await bl.Content.ReadFromJsonAsync<ScenarioResponse>())!;

        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/scenarios",
            new CreateScenarioRequest("Green", null, baseline.Id, new() { ["vegetationCoverPct"] = 80 }));
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        var alt = (await res.Content.ReadFromJsonAsync<ScenarioResponse>())!;
        alt.Kind.Should().Be("Alternative");
        alt.IsLocked.Should().BeFalse();
        alt.ParentScenarioId.Should().Be(baseline.Id);
        alt.Parameters["vegetationCoverPct"].Should().Be(80);
    }

    [Fact]
    public async Task CreateAlternative_WithParentAndRun_MergesAllLayers()
    {
        // Regression: a supplied BaseAnalysisRunId was stored but its params were dropped
        // for alternatives. Merge order: parent → run → request wins.
        var client = await RegisterAsync();
        var (projectId, file, run) = await SetupWithRunAndFileAsync(client);

        var run2Res = await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, new() { ["albedo"] = 0.5 }));
        var run2 = (await run2Res.Content.ReadFromJsonAsync<AnalysisRunResponse>())!;

        var bl = await client.PostAsJsonAsync($"/api/projects/{projectId}/scenarios",
            new CreateScenarioRequest("Baseline", run.RunId, null, null)); // params {veg: 10}
        var baseline = (await bl.Content.ReadFromJsonAsync<ScenarioResponse>())!;

        var al = await client.PostAsJsonAsync($"/api/projects/{projectId}/scenarios",
            new CreateScenarioRequest("Alt", run2.RunId, baseline.Id, new() { ["shadingPct"] = 5 }));
        al.StatusCode.Should().Be(HttpStatusCode.Created);
        var alt = (await al.Content.ReadFromJsonAsync<ScenarioResponse>())!;
        alt.Parameters.Should().BeEquivalentTo(new Dictionary<string, double>
        {
            ["vegetationCoverPct"] = 10, // parent
            ["albedo"] = 0.5,            // linked run (was silently dropped)
            ["shadingPct"] = 5,          // request wins
        });
        alt.BaseAnalysisRunId.Should().Be(run2.RunId);
    }

    private async Task<(Guid ProjectId, FileResponse File, AnalysisRunResponse Run)> SetupWithRunAndFileAsync(
        HttpClient client)
    {
        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("ScM", null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(SquareGeoJson)), "file", "g.geojson" },
        };
        var up = await client.PostAsync($"/api/projects/{project.Id}/files", form);
        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;

        var an = await client.PostAsJsonAsync($"/api/projects/{project.Id}/analysis",
            new AnalyzeRequest(file.Id, new() { ["vegetationCoverPct"] = 10 }));
        var run = (await an.Content.ReadFromJsonAsync<AnalysisRunResponse>())!;
        return (project.Id, file, run);
    }

    [Fact]
    public async Task UpdateBaseline_Returns409_Locked()
    {
        var client = await RegisterAsync();
        var (projectId, _, run) = await SetupWithRunAsync(client);
        var bl = await client.PostAsJsonAsync($"/api/projects/{projectId}/scenarios",
            new CreateScenarioRequest("Baseline", run.RunId, null, null));
        var baseline = (await bl.Content.ReadFromJsonAsync<ScenarioResponse>())!;

        var res = await client.PutAsJsonAsync($"/api/scenarios/{baseline.Id}",
            new UpdateScenarioRequest("Renamed", null, baseline.RowVersion));
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await res.Content.ReadAsStringAsync()).Should().Contain("SCENARIO_LOCKED");
    }

    [Fact]
    public async Task UpdateAlternative_BumpsVersion_StaleRowVersion_Conflicts()
    {
        var client = await RegisterAsync();
        var (projectId, _, _) = await SetupWithRunAsync(client);
        var bl = await client.PostAsJsonAsync($"/api/projects/{projectId}/scenarios",
            new CreateScenarioRequest("Baseline", null, null, new() { ["albedo"] = 0.4 }));
        var baseline = (await bl.Content.ReadFromJsonAsync<ScenarioResponse>())!;
        var al = await client.PostAsJsonAsync($"/api/projects/{projectId}/scenarios",
            new CreateScenarioRequest("Alt", null, baseline.Id, null));
        var alt = (await al.Content.ReadFromJsonAsync<ScenarioResponse>())!;

        var first = await client.PutAsJsonAsync($"/api/scenarios/{alt.Id}",
            new UpdateScenarioRequest("Alt-2", new() { ["albedo"] = 0.6 }, alt.RowVersion));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await first.Content.ReadFromJsonAsync<ScenarioResponse>())!;
        updated.Version.Should().Be(2);
        updated.Parameters["albedo"].Should().Be(0.6);

        var stale = await client.PutAsJsonAsync($"/api/scenarios/{alt.Id}",
            new UpdateScenarioRequest("Alt-3", null, alt.RowVersion));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_TooManyParameters_Returns400()
    {
        var client = await RegisterAsync();
        var (projectId, _, _) = await SetupWithRunAsync(client);
        var bl = await client.PostAsJsonAsync($"/api/projects/{projectId}/scenarios",
            new CreateScenarioRequest("Baseline", null, null, null));
        var baseline = (await bl.Content.ReadFromJsonAsync<ScenarioResponse>())!;
        var al = await client.PostAsJsonAsync($"/api/projects/{projectId}/scenarios",
            new CreateScenarioRequest("Alt", null, baseline.Id, null));
        var alt = (await al.Content.ReadFromJsonAsync<ScenarioResponse>())!;

        var res = await client.PutAsJsonAsync($"/api/scenarios/{alt.Id}",
            new UpdateScenarioRequest(null, new()
            {
                ["vegetationCoverPct"] = 10, ["albedo"] = 0.5, ["shadingPct"] = 10, ["extra"] = 1,
            }, alt.RowVersion));
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync()).Should().Contain("TOO_MANY_SCENARIO_PARAMETERS");
    }

    [Fact]
    public async Task Update_UnsupportedParameter_Returns400()
    {
        var client = await RegisterAsync();
        var (projectId, _, _) = await SetupWithRunAsync(client);
        var bl = await client.PostAsJsonAsync($"/api/projects/{projectId}/scenarios",
            new CreateScenarioRequest("Baseline", null, null, null));
        var baseline = (await bl.Content.ReadFromJsonAsync<ScenarioResponse>())!;
        var al = await client.PostAsJsonAsync($"/api/projects/{projectId}/scenarios",
            new CreateScenarioRequest("Alt", null, baseline.Id, null));
        var alt = (await al.Content.ReadFromJsonAsync<ScenarioResponse>())!;

        var res = await client.PutAsJsonAsync($"/api/scenarios/{alt.Id}",
            new UpdateScenarioRequest(null, new() { ["building_orientation"] = 45 }, alt.RowVersion));
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync()).Should().Contain("UNSUPPORTED_SCENARIO_PARAMETER");
    }

    [Fact]
    public async Task AnalyzeAlternative_UsesScenarioParams_Replay_IsIdempotent()
    {
        var client = await RegisterAsync();
        var (projectId, _, run) = await SetupWithRunAsync(client);
        var bl = await client.PostAsJsonAsync($"/api/projects/{projectId}/scenarios",
            new CreateScenarioRequest("Baseline", run.RunId, null, null));
        var baseline = (await bl.Content.ReadFromJsonAsync<ScenarioResponse>())!;
        var al = await client.PostAsJsonAsync($"/api/projects/{projectId}/scenarios",
            new CreateScenarioRequest("Green", null, baseline.Id, new() { ["vegetationCoverPct"] = 100 }));
        var alt = (await al.Content.ReadFromJsonAsync<ScenarioResponse>())!;

        var first = await client.PostAsJsonAsync($"/api/scenarios/{alt.Id}/analyze",
            new AnalyzeScenarioRequest(null));
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var altRun = (await first.Content.ReadFromJsonAsync<AnalysisRunResponse>())!;
        altRun.ScenarioId.Should().Be(alt.Id);
        altRun.Values.Single().Value.Should().BeApproximately(27.0, 1e-9);
        altRun.Values.Single().Classification.Should().Be("Acceptable");

        var second = await client.PostAsJsonAsync($"/api/scenarios/{alt.Id}/analyze",
            new AnalyzeScenarioRequest(null));
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadFromJsonAsync<AnalysisRunResponse>())!.RunId.Should().Be(altRun.RunId);
    }

    [Fact]
    public async Task Analyze_WithoutBaselineFile_Returns422()
    {
        var client = await RegisterAsync();
        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Sc2", null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;
        var bl = await client.PostAsJsonAsync($"/api/projects/{project.Id}/scenarios",
            new CreateScenarioRequest("Baseline", null, null, null));

        var res = await client.PostAsJsonAsync(
            $"/api/scenarios/{(await bl.Content.ReadFromJsonAsync<ScenarioResponse>())!.Id}/analyze",
            new AnalyzeScenarioRequest(null));
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CrossOwner_Returns403_Missing_Returns404()
    {
        var alice = await RegisterAsync();
        var bob = await RegisterAsync();
        var (projectId, _, _) = await SetupWithRunAsync(alice);
        var bl = await alice.PostAsJsonAsync($"/api/projects/{projectId}/scenarios",
            new CreateScenarioRequest("Baseline", null, null, null));
        var baseline = (await bl.Content.ReadFromJsonAsync<ScenarioResponse>())!;

        (await bob.GetAsync($"/api/scenarios/{baseline.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var bobCreate = await bob.PostAsJsonAsync($"/api/projects/{projectId}/scenarios",
            new CreateScenarioRequest("X", null, null, null));
        bobCreate.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        (await alice.GetAsync($"/api/scenarios/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_IsProjectScoped()
    {
        var client = await RegisterAsync();
        var (projectId, _, _) = await SetupWithRunAsync(client);
        await client.PostAsJsonAsync($"/api/projects/{projectId}/scenarios",
            new CreateScenarioRequest("B", null, null, null));

        var list = await client.GetFromJsonAsync<ScenarioResponse[]>($"/api/projects/{projectId}/scenarios");
        list.Should().HaveCount(1);

        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Other", null, null));
        var other = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;
        (await client.GetFromJsonAsync<ScenarioResponse[]>($"/api/projects/{other.Id}/scenarios")).Should().BeEmpty();
    }
}
