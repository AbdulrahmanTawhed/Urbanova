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
using Urbanova.Application.Costing;
using Urbanova.Application.EngineeringFiles;
using Urbanova.Application.Projects;
using Urbanova.Domain;
using Urbanova.Domain.Entities;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.IntegrationTests;

/// <summary>
/// Phase 13: PRD §19 failure sweep — every documented failure returns an explained
/// error and never destroys project data or leaks internals.
/// </summary>
public sealed class FailureTests : IAsyncLifetime
{
    private const string TestCs =
        "Server=(localdb)\\MSSQLLocalDB;Database=Urbanova_Test;Trusted_Connection=True;TrustServerCertificate=True";
    private const string TestKey = "phase13f-integration-test-key-0123456789abcdef";

    private const string SquareGeoJson = """
        { "type": "FeatureCollection", "features": [
          { "type": "Feature", "properties": { "name": "Lot" },
            "geometry": { "type": "Polygon",
              "coordinates": [[[0,0],[2,0],[2,2],[0,2],[0,0]]] } } ] }
        """;

    private const string PointsOnlyGeoJson = """
        { "type": "FeatureCollection", "features": [
          { "type": "Feature", "properties": {},
            "geometry": { "type": "Point", "coordinates": [1,2] } } ] }
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

    private async Task<(HttpClient Client, Guid ProjectId)> SetupAsync()
    {
        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"fl-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Fragile", null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;
        return (client, project.Id);
    }

    private static MultipartFormDataContent Multipart(string content, string name)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", name);
        return form;
    }

    [Fact]
    public async Task UnsupportedFile_Rejected_WithExplanation_NothingStored()
    {
        var (client, projectId) = await SetupAsync();
        using var form = Multipart("MZ", "plan.exe");
        var res = await client.PostAsync($"/api/projects/{projectId}/files", form);

        res.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("UNSUPPORTED_FORMAT").And.Contain("GeoJSON");
        body.Should().NotContain("exception", "no internals leak");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.EngineeringFiles.CountAsync()).Should().Be(0);
        (await db.Projects.CountAsync()).Should().Be(1, "project data survives the failure");
    }

    [Fact]
    public async Task InvalidFile_Rejected_Upload_And_Analysis_Blocked()
    {
        var (client, projectId) = await SetupAsync();
        using var form = Multipart("""{"type":"Nope"}""", "bad.geojson");
        var up = await client.PostAsync($"/api/projects/{projectId}/files", form);
        up.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var list = await client.GetFromJsonAsync<FileResponse[]>($"/api/projects/{projectId}/files");
        var file = list.Should().ContainSingle().Subject;

        var an = await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, []));
        an.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await an.Content.ReadAsStringAsync()).Should().Contain("INVALID_FILE");
    }

    [Fact]
    public async Task GeometryExtractionFailure_StopsAnalysis_WithExplanation()
    {
        var (client, projectId) = await SetupAsync();
        using var form = Multipart(PointsOnlyGeoJson, "points.geojson");
        var up = await client.PostAsync($"/api/projects/{projectId}/files", form);
        up.StatusCode.Should().Be(HttpStatusCode.Created, "points are valid GeoJSON");

        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;
        var geo = await client.PostAsync($"/api/files/{file.Id}/extract-geometry", null);
        geo.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await geo.Content.ReadAsStringAsync()).Should().Contain("GEOMETRY_EXTRACTION_FAILED");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AnalysisRuns.CountAsync()).Should().Be(0, "no incomplete analysis stored as valid");
    }

    [Fact]
    public async Task FailedRun_NeverReadsAsValid()
    {
        var (client, projectId) = await SetupAsync();
        Guid runId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var run = new AnalysisRun
            {
                ProjectId = projectId, EngineName = "HeatV01", EngineVersion = "x",
                ConfigVersion = "x", ConfigSnapshotJson = "{}", InputSnapshotJson = "{}",
                InputHash = new string('e', 64), Status = AnalysisStatus.Failed,
                ErrorCode = "ANALYSIS_FAILED", StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
            };
            db.AnalysisRuns.Add(run);
            await db.SaveChangesAsync();
            runId = run.Id;
        }

        var res = await client.GetAsync($"/api/analysis/{runId}");
        res.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await res.Content.ReadAsStringAsync()).Should().Contain("ANALYSIS_FAILED");
    }

    [Fact]
    public async Task MissingCost_ReturnsUnavailable_NeverInvented()
    {
        var (client, projectId) = await SetupAsync();
        var res = await client.PostAsJsonAsync($"/api/projects/{projectId}/cost-estimates",
            new CreateCostEstimateRequest(5, "m2", null, "no-such-item", null, null, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = (await res.Content.ReadFromJsonAsync<CostEstimateResponse>())!;
        body.Status.Should().Be("Unavailable");
        body.Total.Should().Be(0);
        body.UnitPrice.Should().Be(0);
    }

    [Fact]
    public async Task DeletingFile_KeepsRun_Readable()
    {
        var (client, projectId) = await SetupAsync();
        using var form = Multipart(SquareGeoJson, "g.geojson");
        var up = await client.PostAsync($"/api/projects/{projectId}/files", form);
        var file = (await up.Content.ReadFromJsonAsync<FileResponse>())!;
        var an = await client.PostAsJsonAsync($"/api/projects/{projectId}/analysis",
            new AnalyzeRequest(file.Id, []));
        var run = (await an.Content.ReadFromJsonAsync<AnalysisRunResponse>())!;

        (await client.DeleteAsync($"/api/files/{file.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Run survives (file FK SET NULL); stored values still readable.
        var get = await client.GetAsync($"/api/analysis/{run.RunId}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        (await get.Content.ReadFromJsonAsync<AnalysisRunResponse>())!.Values.Should().HaveCount(1);
    }
}
