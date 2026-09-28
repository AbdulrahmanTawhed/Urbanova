using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Urbanova.Application.Auth;
using Urbanova.Application.Projects;
using Urbanova.Domain;
using Urbanova.Domain.Entities;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.IntegrationTests;

/// <summary>
/// Phase 4: project CRUD over HTTP against LocalDB (requires MSSQLLocalDB).
/// Ownership, pagination, concurrency, and explicit delete order covered.
/// </summary>
public sealed class ProjectsTests : IAsyncLifetime
{
    private const string TestCs =
        "Server=(localdb)\\MSSQLLocalDB;Database=Urbanova_Test;Trusted_Connection=True;TrustServerCertificate=True";
    private const string TestKey = "phase4-integration-test-key-0123456789abcdef";

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
        var email = $"proj-{Guid.NewGuid():N}@example.com";
        var res = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Str0ng!Pass1", null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var auth = (await res.Content.ReadFromJsonAsync<AuthResponse>())!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }

    private static CreateProjectRequest NewProject(string name = "Test Project") =>
        new(name, "A test project",
            new SiteInput("Main St 1", 52.52, 13.405, null, "EPSG:4326", 1500));

    private async Task<ProjectResponse> CreateAsync(HttpClient client, string name = "Test Project")
    {
        var res = await client.PostAsJsonAsync("/api/projects", NewProject(name));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<ProjectResponse>())!;
    }

    [Fact]
    public async Task Create_Returns201WithSite()
    {
        var (client, auth) = await RegisterUserAsync();
        var project = await CreateAsync(client);

        project.Id.Should().NotBeEmpty();
        project.OwnerId.Should().Be(auth.UserId);
        project.Name.Should().Be("Test Project");
        project.Status.Should().Be("Draft");
        project.Site.Should().NotBeNull();
        project.Site!.Address.Should().Be("Main St 1");
        project.Site.Crs.Should().Be("EPSG:4326");
        project.RowVersion.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_InvalidBody_Returns400()
    {
        var (client, _) = await RegisterUserAsync();
        var res = await client.PostAsJsonAsync("/api/projects",
            new CreateProjectRequest("", null, null));
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Endpoints_WithoutToken_Return401()
    {
        (await _anon.GetAsync("/api/projects")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _anon.PostAsJsonAsync("/api/projects", NewProject())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_ReturnsOnlyOwnProjects_WithPagination()
    {
        var (alice, _) = await RegisterUserAsync();
        var (bob, _) = await RegisterUserAsync();

        await CreateAsync(alice, "A-1");
        await CreateAsync(alice, "A-2");
        await CreateAsync(alice, "A-3");
        await CreateAsync(bob, "B-1");

        var page1 = await alice.GetFromJsonAsync<PagedResult<ProjectResponse>>("/api/projects?page=1&pageSize=2");
        page1!.TotalCount.Should().Be(3);
        page1.TotalPages.Should().Be(2);
        page1.Items.Should().HaveCount(2);

        var page2 = await alice.GetFromJsonAsync<PagedResult<ProjectResponse>>("/api/projects?page=2&pageSize=2");
        page2!.Items.Should().HaveCount(1);

        var bobs = await bob.GetFromJsonAsync<PagedResult<ProjectResponse>>("/api/projects");
        bobs!.TotalCount.Should().Be(1);
        bobs.Items.Single().Name.Should().Be("B-1");
    }

    [Fact]
    public async Task GetById_Own_Returns200()
    {
        var (client, _) = await RegisterUserAsync();
        var created = await CreateAsync(client);

        var res = await client.GetAsync($"/api/projects/{created.Id}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_OtherOwner_Returns403_Missing_Returns404()
    {
        var (alice, _) = await RegisterUserAsync();
        var (bob, _) = await RegisterUserAsync();
        var created = await CreateAsync(alice);

        (await bob.GetAsync($"/api/projects/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await alice.GetAsync($"/api/projects/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_Own_Returns200WithChanges()
    {
        var (client, _) = await RegisterUserAsync();
        var created = await CreateAsync(client);

        var update = new UpdateProjectRequest("Renamed", "New desc", "Active",
            new SiteInput("Other St 9", 48.1, 11.5, null, null, 2000), created.RowVersion);
        var res = await client.PutAsJsonAsync($"/api/projects/{created.Id}", update);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = (await res.Content.ReadFromJsonAsync<ProjectResponse>())!;
        body.Name.Should().Be("Renamed");
        body.Status.Should().Be("Active");
        body.Site!.Address.Should().Be("Other St 9");
        body.Site.Crs.Should().Be("EPSG:4326", "blank CRS defaults instead of failing");
    }

    [Fact]
    public async Task Update_StaleRowVersion_Returns409()
    {
        var (client, _) = await RegisterUserAsync();
        var created = await CreateAsync(client);

        var first = new UpdateProjectRequest("V2", null, null, null, created.RowVersion);
        (await client.PutAsJsonAsync($"/api/projects/{created.Id}", first)).StatusCode.Should().Be(HttpStatusCode.OK);

        var stale = new UpdateProjectRequest("V3", null, null, null, created.RowVersion);
        (await client.PutAsJsonAsync($"/api/projects/{created.Id}", stale)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_OtherOwner_Returns403_BadStatus_Returns400()
    {
        var (alice, _) = await RegisterUserAsync();
        var (bob, _) = await RegisterUserAsync();
        var created = await CreateAsync(alice);

        var cross = new UpdateProjectRequest("X", null, null, null, created.RowVersion);
        (await bob.PutAsJsonAsync($"/api/projects/{created.Id}", cross)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var bad = new UpdateProjectRequest("X", null, "Deleted", null, created.RowVersion);
        (await alice.PutAsJsonAsync($"/api/projects/{created.Id}", bad)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_Own_RemovesProjectAndChildren()
    {
        var (client, auth) = await RegisterUserAsync();
        var created = await CreateAsync(client);

        // Seed a child row directly (file module arrives Phase 5; service must still delete it).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.EngineeringFiles.Add(new EngineeringFile
            {
                ProjectId = created.Id, UploadedBy = auth.UserId, FileName = "seed.geojson",
                ContentType = "application/geo+json", SizeBytes = 10,
                StoragePath = "AppData/uploads/seed.geojson",
                HashSha256 = new string('c', 64), ValidationStatus = FileValidationStatus.Pending,
            });
            await db.SaveChangesAsync();
        }

        (await client.DeleteAsync($"/api/projects/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/projects/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Projects.AnyAsync(p => p.Id == created.Id)).Should().BeFalse();
            (await db.Sites.AnyAsync(s => s.ProjectId == created.Id)).Should().BeFalse();
            (await db.EngineeringFiles.AnyAsync(f => f.ProjectId == created.Id)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Delete_OtherOwner_Returns403_Missing_Returns404()
    {
        var (alice, _) = await RegisterUserAsync();
        var (bob, _) = await RegisterUserAsync();
        var created = await CreateAsync(alice);

        (await bob.DeleteAsync($"/api/projects/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await alice.DeleteAsync($"/api/projects/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Alice's project survived Bob's attempt.
        (await alice.GetAsync($"/api/projects/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
