using Urbanova.Domain.Common;

namespace Urbanova.Domain.Entities;

/// <summary>
/// Persisted refresh-token record. Only the SHA-256 hash is stored — the plaintext
/// token is shown to the client once and never persisted (rotation on every refresh).
/// </summary>
public sealed class RefreshToken : EntityBase
{
    public Guid UserId { get; set; }

    /// <summary>SHA-256 hex of the plaintext token. Unique.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;
}
