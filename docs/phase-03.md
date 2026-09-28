# Phase 3 — Authentication & Authorization ✅

## Built

- Domain: `RefreshToken` entity (hash-only storage, rotation via `RevokedAt`/`ReplacedByTokenHash`).
- Application: `Auth` slice — `AuthModels` DTOs, `AuthException` (coded failures),
  `IAuthService` + `ITokenService` ports, FluentValidation validators.
- Infrastructure:
  - `Auth/JwtOptions`, `TokenService` (HMAC-SHA256 access, 32-byte opaque refresh + SHA-256 hash),
    `AuthService` (Identity-backed register/login/refresh + Domain.User mirror sync),
  - `Auth/Ownership`: `IProjectAccessChecker` + `MustOwnProjectRequirement/Handler`
    (resource-based, `AuthorizeAsync(User, projectId, "MustOwnProject")`), owner-only per assumptions #10,
  - `AuthDependencyInjection.AddUrbanovaAuth`: IdentityCore + EF stores, JWT Bearer
    (issuer/audience/lifetime/key validation, 2-min skew), fallback + named policies.
- Api: `AuthController` (`POST register→201/login→200/refresh→200/GET me→200`,
  `AuthException` → 400/401/409 ProblemDetails), explicit validator DI,
  `UseAuthentication/UseAuthorization`, `[AllowAnonymous]` on health/system/OpenAPI/Scalar,
  ephemeral Development signing key fallback (warns; prod without `Jwt:Key` fails fast).
- Migration `AuthRefreshTokens` applied to LocalDB `Urbanova_Dev`.
- Secure-by-default verified: anonymous `GET /api/does-not-exist` → 401 (fallback challenges
  before routing leaks existence).

## Verified

```text
dotnet test Urbanova.slnx → Unit 26/26, Integration 19/19 (45 total, 0 failed)
```

New tests: `TokenServiceTests` (4), `AuthValidatorsTests` (6),
`AuthTests` (9 HTTP: register/login/refresh rotation+reuse/400/401/409/me),
`ProjectAccessTests` (3: checker owner/stranger/missing, handler succeed/forbid/anonymous).
Integration tests serialized (`DisableTestParallelization`) — all share `Urbanova_Test`.
HTTP-level 403 via a business controller is deferred to Phase 4 (ProjectsController);
the handler decision itself is covered here.

## Next (Phase 4 — Project Management)

`ProjectsController` CRUD + Site boundary, DTOs (never entities), ownership enforcement
(checker + `MustOwnProject` → 403/404), pagination, explicit delete order (RESTRICT FKs).
