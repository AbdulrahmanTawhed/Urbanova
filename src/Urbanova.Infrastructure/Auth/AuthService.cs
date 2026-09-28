using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Urbanova.Application.Auth;
using Urbanova.Domain.Entities;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.Infrastructure.Auth;

/// <summary>
/// Identity-backed auth use cases. Creates the Domain.User mirror row on register
/// (shared Guid with the Identity user) for ownership/audit display.
/// </summary>
public sealed class AuthService(
    UserManager<UrbanovaIdentityUser> users,
    AppDbContext db,
    ITokenService tokens,
    IOptions<JwtOptions> jwtOptions) : IAuthService
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var existing = await users.FindByEmailAsync(request.Email);
        if (existing is not null)
            throw new AuthException("EMAIL_TAKEN", "An account with this email already exists.");

        var user = new UrbanovaIdentityUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
        };
        // One transaction for Identity create + profile mirror + token issue: a failure
        // anywhere rolls everything back instead of stranding an account with no tokens
        // (whose retry would then misreport EMAIL_TAKEN). Runs inside the execution
        // strategy because EnableRetryOnFailure forbids user-initiated transactions.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await users.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                // DuplicateUserName surfaces as EMAIL_TAKEN; anything else is a validation failure
                // (Identity already enforces password strength — API returns 400 via ProblemDetails).
                if (result.Errors.Any(e => e.Code.Contains("Duplicate", StringComparison.OrdinalIgnoreCase)))
                    throw new AuthException("EMAIL_TAKEN", "An account with this email already exists.");
                throw new AuthException("REGISTRATION_INVALID",
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }

            db.DomainUsers.Add(new User
            {
                Id = user.Id,
                Email = user.Email!,
                DisplayName = user.DisplayName,
                CreatedBy = user.Id,
            });
            await db.SaveChangesAsync(ct);

            var response = await IssueTokensAsync(user, ct);
            await tx.CommitAsync(ct);
            return response;
        });
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null || !await users.CheckPasswordAsync(user, request.Password))
            throw new AuthException("INVALID_CREDENTIALS", "Invalid email or password.");

        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(r => r.TokenHash == hash, ct);
        if (stored is null || !stored.IsActive(DateTimeOffset.UtcNow))
        {
            // Re-presenting a rotated token signals reuse (possible theft): revoke the
            // whole descendant chain so the stolen lineage cannot be used further.
            if (stored?.ReplacedByTokenHash is not null)
                await RevokeFamilyAsync(stored, ct);
            throw new AuthException("INVALID_REFRESH_TOKEN", "Refresh token is invalid or expired.");
        }

        var user = await users.FindByIdAsync(stored.UserId.ToString());
        if (user is null)
            throw new AuthException("INVALID_REFRESH_TOKEN", "Refresh token is invalid or expired.");

        // Rotate: revoke the presented token, issue a fresh pair (reuse detection via ReplacedByTokenHash).
        var (newToken, newHash) = tokens.CreateRefreshToken();
        stored.RevokedAt = DateTimeOffset.UtcNow;
        stored.ReplacedByTokenHash = newHash;
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = newHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwt.RefreshTokenDays),
            CreatedBy = user.Id,
        });
        await db.SaveChangesAsync(ct);

        var (access, expires) = tokens.CreateAccessToken(user.Id, user.Email!);
        return new AuthResponse(access, newToken, expires, user.Id, user.Email!);
    }

    /// <summary>Walks ReplacedByTokenHash links (cycle-guarded) revoking every active descendant.</summary>
    private async Task RevokeFamilyAsync(RefreshToken start, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { start.TokenHash };
        var next = start.ReplacedByTokenHash;
        var dirty = false;
        for (var hops = 0; hops < 100 && next is not null && seen.Add(next); hops++)
        {
            var token = await db.RefreshTokens.SingleOrDefaultAsync(r => r.TokenHash == next, ct);
            if (token is null)
                break;
            if (token.RevokedAt is null)
            {
                token.RevokedAt = DateTimeOffset.UtcNow;
                dirty = true;
            }
            next = token.ReplacedByTokenHash;
        }
        if (dirty)
            await db.SaveChangesAsync(ct);
    }

    public async Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is null
            ? null
            : new CurrentUserResponse(user.Id, user.Email!, user.DisplayName);
    }

    private async Task<AuthResponse> IssueTokensAsync(UrbanovaIdentityUser user, CancellationToken ct)
    {
        var (access, expires) = tokens.CreateAccessToken(user.Id, user.Email!);
        var (refresh, hash) = tokens.CreateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwt.RefreshTokenDays),
            CreatedBy = user.Id,
        });
        await db.SaveChangesAsync(ct);
        return new AuthResponse(access, refresh, expires, user.Id, user.Email!);
    }
}
