# Contributing to URBANOVA Backend

## Prerequisites

- .NET 10 SDK (pinned minimum in `global.json`)
- SQL Server: LocalDB for local dev (`sqllocaldb start MSSQLLocalDB`),
  any SQL Server for shared environments via `ConnectionStrings__UrbanovaDb`

## Workflow

```powershell
dotnet user-secrets init --project src/Urbanova.Api
dotnet user-secrets set "Jwt:Key" "<32-plus-character-secret>" --project src/Urbanova.Api
dotnet ef database update --project src/Urbanova.Infrastructure --startup-project src/Urbanova.Api
dotnet build Urbanova.slnx
dotnet test Urbanova.slnx   # integration tests use LocalDB database Urbanova_Test
```

## Conventions

- Clean Architecture: `Api → Application → Domain ← Infrastructure`. Domain stays
  dependency-free (enforced by `ArchitectureGuardTests`).
- Controllers stay thin: validation → application service → domain → persistence.
  Never return EF entities from controllers — DTOs only.
- Unresolved requirements become abstractions + configurable MVP defaults, documented
  in `docs/assumptions.md`. Never hard-code a temporary default as final.
- One phase = build + test + fix + verify + document (`docs/phase-NN.md`).
- Never commit secrets, `bin/`, `obj/`, uploads, or rendered reports (see `.gitignore`).
- Error codes follow the catalog in `docs/api.md`; failures must explain without leaking internals.
