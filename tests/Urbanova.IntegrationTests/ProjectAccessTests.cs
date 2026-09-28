using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Urbanova.Domain;
using Urbanova.Domain.Entities;
using Urbanova.Infrastructure.Auth.Ownership;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.IntegrationTests;

/// <summary>
/// Phase 3: ownership authorization against LocalDB (requires MSSQLLocalDB).
/// HTTP-level 403 coverage lands with ProjectsController in Phase 4; here the
/// checker + MustOwnProject handler decisions are verified directly.
/// </summary>
public sealed class ProjectAccessTests : IAsyncLifetime
{
    private const string TestCs =
        "Server=(localdb)\\MSSQLLocalDB;Database=Urbanova_Test;Trusted_Connection=True;TrustServerCertificate=True";

    private ServiceProvider _services = null!;
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(TestCs,
            sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));
        services.AddScoped<IProjectAccessChecker, ProjectAccessChecker>();
        services.AddScoped<IAuthorizationHandler, MustOwnProjectHandler>();
        services.AddLogging();
        _services = services.BuildServiceProvider();

        _db = _services.GetRequiredService<AppDbContext>();
        await _db.Database.EnsureDeletedAsync();
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _services.DisposeAsync();
    }

    private async Task<Project> SeedProjectAsync(Guid ownerId)
    {
        var project = new Project { OwnerId = ownerId, Name = "Owned", Status = ProjectStatus.Draft };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return project;
    }

    [Fact]
    public async Task Checker_Owner_ReturnsTrue_NonOwner_ReturnsFalse()
    {
        var owner = Guid.NewGuid();
        var project = await SeedProjectAsync(owner);

        using var scope = _services.CreateScope();
        var checker = scope.ServiceProvider.GetRequiredService<IProjectAccessChecker>();
        (await checker.IsOwnerAsync(owner, project.Id)).Should().BeTrue();
        (await checker.IsOwnerAsync(Guid.NewGuid(), project.Id)).Should().BeFalse();
        (await checker.IsOwnerAsync(owner, Guid.NewGuid())).Should().BeFalse();
    }

    private AuthorizationHandlerContext ContextFor(Guid? userId, Guid projectId)
    {
        ClaimsPrincipal principal = userId is null
            ? new ClaimsPrincipal(new ClaimsIdentity())
            : new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(JwtRegisteredClaimNames.Sub, userId.Value.ToString())], "jwt"));
        return new AuthorizationHandlerContext(
            [new MustOwnProjectRequirement()], principal, projectId);
    }

    [Fact]
    public async Task Handler_Owner_Succeeds_NonOwner_DoesNot()
    {
        var owner = Guid.NewGuid();
        var project = await SeedProjectAsync(owner);

        using var scope = _services.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IAuthorizationHandler>().ToList();

        var ownerCtx = ContextFor(owner, project.Id);
        foreach (var h in handlers) await h.HandleAsync(ownerCtx);
        ownerCtx.HasSucceeded.Should().BeTrue();

        var strangerCtx = ContextFor(Guid.NewGuid(), project.Id);
        foreach (var h in handlers) await h.HandleAsync(strangerCtx);
        strangerCtx.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Handler_Unauthenticated_DoesNotSucceed()
    {
        var project = await SeedProjectAsync(Guid.NewGuid());

        using var scope = _services.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IAuthorizationHandler>().ToList();

        var ctx = ContextFor(null, project.Id);
        foreach (var h in handlers) await h.HandleAsync(ctx);
        ctx.HasSucceeded.Should().BeFalse();
    }
}
