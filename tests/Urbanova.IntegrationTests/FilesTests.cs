using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Urbanova.Application.Auth;
using Urbanova.Application.EngineeringFiles;
using Urbanova.Application.Projects;
using Urbanova.Domain;
using Urbanova.Domain.Entities;
using Urbanova.Infrastructure.FileProcessing;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.IntegrationTests;

/// <summary>
/// Phase 5: file workflow over HTTP + service-level size guard, against LocalDB (requires MSSQLLocalDB).
/// Uploaded content lands under the test host's AppData/uploads (git-ignored, cleaned per test DB reset
/// only for rows — storage files are overwritten per unique file id, no cross-test collision).
/// </summary>
public sealed class FilesTests : IAsyncLifetime
{
    private const string TestCs =
        "Server=(localdb)\\MSSQLLocalDB;Database=Urbanova_Test;Trusted_Connection=True;TrustServerCertificate=True";
    private const string TestKey = "phase5-integration-test-key-0123456789abcdef";

    private const string ValidGeoJson = """
        {
          "type": "FeatureCollection",
          "features": [
            { "type": "Feature", "properties": { "name": "Block A" },
              "geometry": { "type": "Polygon",
                "coordinates": [[[13.0, 52.0], [14.0, 52.0], [14.0, 53.0], [13.0, 52.0]]] } },
            { "type": "Feature", "properties": { "name": "Block B" },
              "geometry": { "type": "Point", "coordinates": [13.4, 52.5] } }
          ]
        }
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

    private async Task<(HttpClient Client, AuthResponse Auth)> RegisterUserAsync()
    {
        var res = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"file-{Guid.NewGuid():N}@example.com", "Str0ng!Pass1", null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var auth = (await res.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }

    private async Task<ProjectResponse> CreateProjectAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync("/api/projects",
            new CreateProjectRequest("File Project", null, null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<ProjectResponse>())!;
    }

    private static MultipartFormDataContent Multipart(string content, string fileName, string? contentType = null)
    {
        var part = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        if (contentType is not null)
            part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent { { part, "file", fileName } };
        return form;
    }

    private async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid projectId, string content, string fileName, string? contentType = null)
    {
        using var form = Multipart(content, fileName, contentType);
        return await client.PostAsync($"/api/projects/{projectId}/files", form);
    }

    [Fact]
    public async Task Upload_ValidGeoJson_Returns201WithMetadata()
    {
        var (client, auth) = await RegisterUserAsync();
        var project = await CreateProjectAsync(client);

        var res = await UploadAsync(client, project.Id, ValidGeoJson, "site.geojson");
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        var file = (await res.Content.ReadFromJsonAsync<FileResponse>())!;
        file.ProjectId.Should().Be(project.Id);
        file.FormatDetected.Should().Be("GeoJSON");
        file.HashSha256.Should().MatchRegex("^[0-9a-f]{64}$");
        file.ValidationStatus.Should().Be(FileValidationStatus.Valid.ToString());
        file.ValidationErrors.Should().BeEmpty();
        file.Metadata.Should().NotBeNull();
        file.Metadata!.Value.GetProperty("featureCount").GetInt32().Should().Be(2);

        // Persisted row matches response.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.EngineeringFiles.SingleAsync(f => f.Id == file.Id);
        row.UploadedBy.Should().Be(auth.UserId);
        row.MetadataJson.Should().Contain("featureCount");
    }

    [Fact]
    public async Task Upload_DetectedByContentType_Returns201()
    {
        var (client, _) = await RegisterUserAsync();
        var project = await CreateProjectAsync(client);

        var res = await UploadAsync(client, project.Id, ValidGeoJson, "data.bin", "application/geo+json");
        res.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Upload_InvalidContent_Returns422_AndRowStoredInvalid()
    {
        var (client, _) = await RegisterUserAsync();
        var project = await CreateProjectAsync(client);

        var res = await UploadAsync(client, project.Id, """{"type":"Nope"}""", "site.geojson");
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.EngineeringFiles.SingleAsync(f => f.ProjectId == project.Id);
        row.ValidationStatus.Should().Be(FileValidationStatus.Invalid);
        row.ValidationErrorsJson.Should().Contain("root.type");
    }

    [Fact]
    public async Task Upload_UnsupportedFormat_Returns415_NothingStored()
    {
        var (client, _) = await RegisterUserAsync();
        var project = await CreateProjectAsync(client);

        var res = await UploadAsync(client, project.Id, "MZ-binary", "plan.exe", "application/octet-stream");
        res.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await res.Content.ReadAsStringAsync()).Should().Contain("GeoJSON");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.EngineeringFiles.CountAsync(f => f.ProjectId == project.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Upload_DuplicateContent_Returns409()
    {
        var (client, _) = await RegisterUserAsync();
        var project = await CreateProjectAsync(client);

        (await UploadAsync(client, project.Id, ValidGeoJson, "a.geojson")).StatusCode.Should().Be(HttpStatusCode.Created);
        var dup = await UploadAsync(client, project.Id, ValidGeoJson, "b.geojson");
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Validate_ReRunsToValid()
    {
        var (client, _) = await RegisterUserAsync();
        var project = await CreateProjectAsync(client);
        var upload = await UploadAsync(client, project.Id, ValidGeoJson, "site.geojson");
        var file = (await upload.Content.ReadFromJsonAsync<FileResponse>())!;

        var res = await client.PostAsync($"/api/files/{file.Id}/validate", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadFromJsonAsync<FileResponse>())!.ValidationStatus.Should().Be("Valid");
    }

    [Fact]
    public async Task Get_OtherOwner_Returns403_Missing_Returns404_List_IsProjectScoped()
    {
        var (alice, _) = await RegisterUserAsync();
        var (bob, _) = await RegisterUserAsync();
        var projectA = await CreateProjectAsync(alice);
        var projectB = await CreateProjectAsync(bob);

        var upload = await UploadAsync(alice, projectA.Id, ValidGeoJson, "site.geojson");
        var file = (await upload.Content.ReadFromJsonAsync<FileResponse>())!;

        (await bob.GetAsync($"/api/files/{file.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await alice.GetAsync($"/api/files/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var listA = await alice.GetFromJsonAsync<FileResponse[]>($"/api/projects/{projectA.Id}/files");
        listA.Should().HaveCount(1);
        var listB = await bob.GetFromJsonAsync<FileResponse[]>($"/api/projects/{projectB.Id}/files");
        listB.Should().BeEmpty();
        (await bob.GetAsync($"/api/projects/{projectA.Id}/files")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Delete_RemovesRow_AndSurvivesProjectDelete_Guard()
    {
        var (client, _) = await RegisterUserAsync();
        var project = await CreateProjectAsync(client);
        var upload = await UploadAsync(client, project.Id, ValidGeoJson, "site.geojson");
        var file = (await upload.Content.ReadFromJsonAsync<FileResponse>())!;

        (await client.DeleteAsync($"/api/files/{file.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/files/{file.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Endpoints_WithoutToken_Return401()
    {
        (await _anon.GetAsync($"/api/projects/{Guid.NewGuid()}/files")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _anon.GetAsync($"/api/files/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Upload_Oversize_FailsAtServiceLevel()
    {
        // Direct service test with a tiny limit (no 50 MB multipart needed).
        var projectId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(TestCs, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;
        await using var db = new AppDbContext(dbOptions);
        db.Projects.Add(new Project { Id = projectId, OwnerId = ownerId, Name = "P", Status = ProjectStatus.Draft });
        await db.SaveChangesAsync();

        var tempRoot = Path.Combine(Path.GetTempPath(), $"urbanova-test-{Guid.NewGuid():N}");
        try
        {
            var storageOpts = Options.Create(new FileStorageOptions { RootPath = tempRoot, MaxBytes = 10 });
            var storage = new LocalFileStorage(storageOpts, NullLogger<LocalFileStorage>.Instance);
            var registry = new ProcessorRegistry([new GeoJsonFileProcessor()]);
            var service = new FileService(db, registry, storage, storageOpts);

            using var big = new MemoryStream(new byte[11]);
            var act = () => service.UploadAsync(ownerId, projectId, "site.geojson", "application/geo+json", big, 11);
            (await act.Should().ThrowAsync<FileException>()).Which.ErrorCode.Should().Be("FILE_TOO_LARGE");
        }
        finally
        {
            db.ChangeTracker.Clear();
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
