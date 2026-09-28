using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Urbanova.Application.Auth;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.IntegrationTests;

/// <summary>
/// Phase 3: auth over HTTP against LocalDB (requires MSSQLLocalDB).
/// Each test rebuilds Urbanova_Test via migrations; a fixed test signing key overrides config.
/// </summary>
public sealed class AuthTests : IAsyncLifetime
{
    private const string TestCs =
        "Server=(localdb)\\MSSQLLocalDB;Database=Urbanova_Test;Trusted_Connection=True;TrustServerCertificate=True";
    private const string TestKey = "phase3-integration-test-key-0123456789abcdef";

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

        // Rebuild schema (same CS the app itself uses).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private async Task<AuthResponse> RegisterAsync(string? email = null, string password = "Str0ng!Pass1")
    {
        var res = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email ?? UniqueEmail("user"), password, "Test User"));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await res.Content.ReadFromJsonAsync<AuthResponse>();
        body.Should().NotBeNull();
        return body!;
    }

    [Fact]
    public async Task Register_Returns201WithTokens()
    {
        var body = await RegisterAsync();
        body.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.RefreshToken.Should().NotBeNullOrWhiteSpace();
        body.UserId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var email = UniqueEmail("dup");
        await RegisterAsync(email);
        var res = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Str0ng!Pass1", null));
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_InvalidBody_Returns400()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("not-an-email", "short", null));
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_Valid_Returns200WithTokens()
    {
        var email = UniqueEmail("login");
        await RegisterAsync(email);
        var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Str0ng!Pass1"));
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<AuthResponse>();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var email = UniqueEmail("badpass");
        await RegisterAsync(email);
        var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Wr0ng!Pass9"));
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_RotatesTokens_AndInvalidatesOld()
    {
        var first = await RegisterAsync();

        var res = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(first.RefreshToken));
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await res.Content.ReadFromJsonAsync<AuthResponse>();
        second!.RefreshToken.Should().NotBe(first.RefreshToken);

        // Replaying the old token must fail (rotation / reuse detection).
        var replay = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(first.RefreshToken));
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_UnknownToken_Returns401()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest("not-a-real-token"));
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        var res = await _client.GetAsync("/api/auth/me");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithToken_Returns200()
    {
        var registered = await RegisterAsync();
        using var authed = _factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", registered.AccessToken);

        var res = await authed.GetAsync("/api/auth/me");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await res.Content.ReadFromJsonAsync<CurrentUserResponse>();
        me!.UserId.Should().Be(registered.UserId);
        me.Email.Should().Be(registered.Email);
    }
}
