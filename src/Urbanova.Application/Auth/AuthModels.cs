namespace Urbanova.Application.Auth;

/// <summary>Auth DTOs. Controllers map these 1:1 — never expose Identity or EF types.</summary>
public sealed record RegisterRequest(string Email, string Password, string? DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    Guid UserId,
    string Email);

public sealed record CurrentUserResponse(Guid UserId, string Email, string? DisplayName);
