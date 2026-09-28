using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Urbanova.Application.Common;

namespace Urbanova.Api.Services;

/// <summary>Caller identity from the validated JWT sub claim (Phase 3 tokens).</summary>
public sealed class HttpContextCurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    public Guid? UserId
    {
        get
        {
            var sub = accessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }

    public string? Email => accessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Email);
}
