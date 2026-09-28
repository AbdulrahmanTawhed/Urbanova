using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Urbanova.Infrastructure.Auth.Ownership;

/// <summary>
/// Resource-based requirement: the caller must own the project (Guid resource).
/// Usage: IAuthorizationService.AuthorizeAsync(User, projectId, "MustOwnProject").
/// Controllers return 403 (Forbid) on failure; users can never see another user's data.
/// </summary>
public sealed class MustOwnProjectRequirement : IAuthorizationRequirement;

public sealed class MustOwnProjectHandler(IServiceProvider services)
    : AuthorizationHandler<MustOwnProjectRequirement, Guid>
{
    public const string PolicyName = "MustOwnProject";

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MustOwnProjectRequirement requirement,
        Guid projectId)
    {
        var sub = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(sub, out var userId))
            return; // unauthenticated -> no success (framework yields 401/403)

        using var scope = services.CreateScope();
        var checker = scope.ServiceProvider.GetRequiredService<IProjectAccessChecker>();
        if (await checker.IsOwnerAsync(userId, projectId))
            context.Succeed(requirement);
    }
}
