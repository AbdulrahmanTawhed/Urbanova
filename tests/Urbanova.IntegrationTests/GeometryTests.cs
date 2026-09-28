using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Urbanova.Application.Auth;
using Urbanova.Application.EngineeringFiles;
using Urbanova.Application.Projects;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.IntegrationTests;

/// <summary>Phase 6: extract-geometry endpoint over HTTP against LocalDB (requires MSSQLLocalDB).</summary>
public sealed class GeometryTests : IAsyncLifetime
{
    private const string TestCs =
        "Server=(localdb)\\MSSQLLocalDB;Database=Urbanova_Test;Trusted_Connection=True;TrustServerCertificate=True";
    private const string TestKey = "phase6-integration-test-key-0123456789abcdef";

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
            new RegisterRequest($"geo-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var pr = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Geo", null, null));
        var project = (await pr.Content.ReadFromJsonAsync<ProjectResponse>())!;
        return (client, project.Id);
    }

    private async Task<FileResponse> UploadAsync(HttpClient client, Guid projectId, string content, string name = "g.geojson")
    {
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", name },
        };
        var res = await client.PostAsync($"/api/projects/{projectId}/files", form);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<FileResponse>())!;
    }

    [Fact]
    public async Task Extract_ReturnsNormalizedAreas()
    {
        var (client, projectId) = await SetupAsync();
        var file = await UploadAsync(client, projectId, SquareGeoJson);

        var res = await client.PostAsync($"/api/files/{file.Id}/extract-geometry", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var geo = (await res.Content.ReadFromJsonAsync<GeometryResponse>())!;
        geo.FileId.Should().Be(file.Id);
        geo.FormatDetected.Should().Be("GeoJSON");
        geo.Crs.Should().Be("EPSG:4326");
        geo.TotalArea.Should().BeApproximately(4.0, 1e-9);
        geo.PolygonCount.Should().Be(1);
        geo.Polygons[0].Area.Should().BeApproximately(4.0, 1e-9);
    }

    [Fact]
    public async Task Extract_InvalidFile_Returns422()
    {
        var (client, projectId) = await SetupAsync();
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes("""{"type":"Nope"}""")), "file", "bad.geojson" },
        };
        var upload = await client.PostAsync($"/api/projects/{projectId}/files", form);
        upload.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Row exists with Invalid status — find it via list, then extraction must refuse.
        var list = await client.GetFromJsonAsync<FileResponse[]>($"/api/projects/{projectId}/files");
        var res = await client.PostAsync($"/api/files/{list![0].Id}/extract-geometry", null);
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Extract_PointsOnly_Returns422_ExplainingStop()
    {
        var (client, projectId) = await SetupAsync();
        var file = await UploadAsync(client, projectId, PointsOnlyGeoJson);

        var res = await client.PostAsync($"/api/files/{file.Id}/extract-geometry", null);
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res.Content.ReadAsStringAsync()).Should().Contain("GEOMETRY_EXTRACTION_FAILED");
    }

    [Fact]
    public async Task Extract_OtherOwner_Returns403_Missing_Returns404_Anonymous_Returns401()
    {
        var (alice, projectId) = await SetupAsync();
        var file = await UploadAsync(alice, projectId, SquareGeoJson);

        var reg = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"geo-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        var bobAuth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        var bob = _factory.CreateClient();
        bob.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobAuth.AccessToken);

        (await bob.PostAsync($"/api/files/{file.Id}/extract-geometry", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await alice.PostAsync($"/api/files/{Guid.NewGuid()}/extract-geometry", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _anon.PostAsync($"/api/files/{file.Id}/extract-geometry", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
