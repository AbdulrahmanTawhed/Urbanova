using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Urbanova.Infrastructure.Auth;

namespace Urbanova.UnitTests;

/// <summary>Phase 3: token primitives. No DB — signing only.</summary>
public sealed class TokenServiceTests
{
    private const string TestKey = "phase3-unit-test-key-0123456789abcdef";

    private static TokenService Service() =>
        new(Options.Create(new JwtOptions
        {
            Issuer = "urbanova-test",
            Audience = "urbanova-api-test",
            Key = TestKey,
            AccessTokenMinutes = 30,
            RefreshTokenDays = 7,
        }));

    [Fact]
    public void AccessToken_ContainsSubClaimAndExpiry()
    {
        var userId = Guid.NewGuid();
        var (token, expires) = Service().CreateAccessToken(userId, "a@b.c");

        token.Should().NotBeNullOrWhiteSpace();
        expires.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(30), TimeSpan.FromMinutes(2));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == userId.ToString());
        jwt.Issuer.Should().Be("urbanova-test");
    }

    [Fact]
    public void AccessTokens_AreUniquePerCall()
    {
        var svc = Service();
        var userId = Guid.NewGuid();
        var (t1, _) = svc.CreateAccessToken(userId, "a@b.c");
        var (t2, _) = svc.CreateAccessToken(userId, "a@b.c");
        t1.Should().NotBe(t2, "each token carries a fresh jti");
    }

    [Fact]
    public void RefreshToken_HashIsDeterministicHex()
    {
        var svc = Service();
        var (token, hash) = svc.CreateRefreshToken();

        token.Should().NotBeNullOrWhiteSpace();
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
        svc.HashRefreshToken(token).Should().Be(hash);
    }

    [Fact]
    public void RefreshTokens_AreUnique()
    {
        var svc = Service();
        var (t1, h1) = svc.CreateRefreshToken();
        var (t2, h2) = svc.CreateRefreshToken();
        t1.Should().NotBe(t2);
        h1.Should().NotBe(h2);
    }
}
