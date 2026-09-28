# Environment & Configuration (Phase 3)

| Key | Source | Phase used | Notes |
|---|---|---|---|
| `ConnectionStrings__UrbanovaDb` | env / user-secrets | 2+ | SQL Server. Dev default in `appsettings.Development.json` (LocalDB). Never commit prod value. |
| `Jwt__Key` | env / user-secrets | 3+ | 256-bit+ secret. Empty in repo by design. Development without a key uses an ephemeral session key (warning at startup — tokens die on restart); non-Development without a key fails fast. |
| `Jwt__Issuer` / `Jwt__Audience` | appsettings | 3+ | Defaults `urbanova` / `urbanova-api`. |
| `FileStorage__RootPath` | appsettings | 5+ | Default `AppData/uploads` (git-ignored). |
| `ASPNETCORE_ENVIRONMENT` | env | 1+ | `Development` enables `/openapi/v1.json` + `/scalar/v1`. |
| `Serilog:*` | appsettings | 1+ | Console sink in Phase 1; file/sink hardening later. |

Secrets: `dotnet user-secrets init --project src/Urbanova.Api` then
`dotnet user-secrets set "Jwt:Key" "<256-bit>" --project src/Urbanova.Api`.
