using Urbanova.Domain.Common;

namespace Urbanova.Domain.Entities;

/// <summary>
/// Domain profile for the authenticated user. Auth credentials live in
/// Infrastructure.UrbanovaIdentityUser (ASP.NET Identity); this row mirrors
/// Id + Email + DisplayName for ownership/audit display. Synced on register (Phase 3).
/// Id MUST equal the Identity user id (shared Guid).
/// </summary>
public sealed class User : EntityBase
{
    public string Email { get; set; } = string.Empty;

    public string? DisplayName { get; set; }
}
