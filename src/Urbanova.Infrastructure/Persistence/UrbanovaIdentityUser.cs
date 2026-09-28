using Microsoft.AspNetCore.Identity;

namespace Urbanova.Infrastructure.Persistence;

/// <summary>
/// ASP.NET Identity user (auth credentials). Domain.Entities.User mirrors
/// Id/Email/DisplayName for ownership display; ids are shared Guids (synced Phase 3).
/// </summary>
public sealed class UrbanovaIdentityUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
