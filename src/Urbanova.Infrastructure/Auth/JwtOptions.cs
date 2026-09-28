namespace Urbanova.Infrastructure.Auth;

/// <summary>Binds to the "Jwt" configuration section. Key must be ≥ 256 bits when set explicitly.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "urbanova";

    public string Audience { get; set; } = "urbanova-api";

    public string Key { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 30;

    public int RefreshTokenDays { get; set; } = 7;
}
