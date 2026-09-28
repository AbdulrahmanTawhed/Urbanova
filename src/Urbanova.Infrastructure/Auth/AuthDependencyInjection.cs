using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Urbanova.Application.Auth;
using Urbanova.Infrastructure.Auth.Ownership;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.Infrastructure.Auth;

/// <summary>
/// Phase 3 auth wiring: IdentityCore + EF stores, JWT Bearer, fallback + MustOwnProject policies.
/// Requires persistence (AppDbContext) to be registered first.
/// Key policy: explicit Jwt:Key wins; otherwise Development gets an ephemeral
/// session key (warning — set a persistent secret via user-secrets); non-Development
/// without a key fails fast with a clear error.
/// </summary>
public static class AuthDependencyInjection
{
    public static void AddUrbanovaAuth(
        IServiceCollection services,
        IConfiguration config,
        IHostEnvironment env)
    {
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));
        var jwt = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        if (string.IsNullOrWhiteSpace(jwt.Key))
        {
            if (env.IsDevelopment())
            {
                jwt.Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                Console.WriteLine("[Urbanova] Jwt:Key not configured — using an ephemeral Development signing key. " +
                                  "Set a persistent secret: dotnet user-secrets set \"Jwt:Key\" \"<256-bit>\" --project src/Urbanova.Api");
            }
            else
            {
                throw new InvalidOperationException(
                    "Missing Jwt:Key. Set the Jwt__Key environment variable to a 256-bit+ secret.");
            }
        }
        else if (Encoding.UTF8.GetBytes(jwt.Key).Length < 32)
        {
            throw new InvalidOperationException("Jwt:Key must be at least 256 bits (32 bytes when UTF-8 encoded).");
        }

        // Persist the resolved key so TokenService + validation share it (covers ephemeral dev keys).
        services.Configure<JwtOptions>(o =>
        {
            o.Issuer = jwt.Issuer;
            o.Audience = jwt.Audience;
            o.Key = jwt.Key;
            o.AccessTokenMinutes = jwt.AccessTokenMinutes;
            o.RefreshTokenDays = jwt.RefreshTokenDays;
        });

        services.AddIdentityCore<UrbanovaIdentityUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 8;
                // Keep default strength (upper/lower/digit/non-alphanumeric); PRD roles arrive later.
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ClockSkew = TimeSpan.FromMinutes(2),
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(MustOwnProjectHandler.PolicyName, p => p
                .RequireAuthenticatedUser()
                .AddRequirements(new MustOwnProjectRequirement()));

        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IProjectAccessChecker, ProjectAccessChecker>();
        services.AddScoped<IAuthorizationHandler, MustOwnProjectHandler>();
    }
}
