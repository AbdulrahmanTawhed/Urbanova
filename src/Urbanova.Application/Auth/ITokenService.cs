namespace Urbanova.Application.Auth;

/// <summary>JWT + refresh-token primitives. Implementation is infrastructure (needs signing key).</summary>
public interface ITokenService
{
    /// <summary>Issues a signed access token + its UTC expiry for the given user.</summary>
    (string Token, DateTimeOffset ExpiresAtUtc) CreateAccessToken(Guid userId, string email);

    /// <summary>Generates a plaintext refresh token with its SHA-256 hash (hash is what gets stored).</summary>
    (string Token, string TokenHash) CreateRefreshToken();

    /// <summary>SHA-256 hex of a presented refresh token for hash comparison.</summary>
    string HashRefreshToken(string token);
}
