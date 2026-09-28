# URBANOVA — Backend (.NET 10 + SQL Server)

[![CI](https://github.com/AbdulrahmanTawhed/Urbanova/actions/workflows/ci.yml/badge.svg)](https://github.com/AbdulrahmanTawhed/Urbanova/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Production-quality backend for the URBANOVA MVP. Implements the PRD workflow
Project → File → Validation → Geometry → Analysis → Baseline/Alternative →
Modify → Recalculate → Compare → Recommend → Cost → Report.

> Status: **Phase 14 complete + review fixes** — all modules built, 200/200 tests green.
> Every unresolved PRD requirement maps to an abstraction + configurable MVP
> default documented in `docs/assumptions.md` (nothing hard-coded as final).

## Stack

- .NET 10 (SDK 10.0.401+, pinned in `global.json`), C# latest
- ASP.NET Core Web API, OpenAPI + Scalar UI, Serilog, HealthChecks, RFC 7807 errors
- EF Core 10 + SQL Server, ASP.NET Identity + JWT Bearer (access + rotating refresh)
- xUnit + FluentAssertions + WebApplicationFactory (unit + LocalDB integration)

## Layout

```text
Urbanova.slnx
├── src/Urbanova.Domain/          # entities, VOs, enums, ports, rules (dependency-free)
├── src/Urbanova.Application/     # use cases, DTOs, validators (→ Domain only)
├── src/Urbanova.Infrastructure/  # EF Core, Identity, file/analysis/report adapters
├── src/Urbanova.Api/             # controllers, middleware, auth, config, OpenAPI
├── tests/Urbanova.UnitTests/         # guards, rules, engine math
├── tests/Urbanova.IntegrationTests/  # HTTP + persistence (LocalDB Urbanova_Test)
└── docs/  # api, architecture, assumptions, database-schema, env-vars,
           # known-limitations, open-validations, phase-01…phase-14
```

## Setup

Prerequisites: .NET 10 SDK, SQL Server LocalDB (`sqllocaldb start MSSQLLocalDB`).

```powershell
# 1. Restore + configure secrets (dev key is ephemeral otherwise — see docs/env-vars.md)
dotnet user-secrets init --project src/Urbanova.Api
dotnet user-secrets set "Jwt:Key" "<32-plus-char-secret>" --project src/Urbanova.Api

# 2. Create/migrate the database (default: (localdb)\MSSQLLocalDB, Urbanova_Dev)
dotnet ef database update --project src/Urbanova.Infrastructure --startup-project src/Urbanova.Api

# 3. Build + test (integration tests use LocalDB database Urbanova_Test)
dotnet build Urbanova.slnx
dotnet test Urbanova.slnx

# 4. Run — http://localhost:5202 (/scalar/v1 docs UI in Development)
dotnet run --project src/Urbanova.Api
```

Config: `src/Urbanova.Api/appsettings.json` (placeholders documented in `docs/env-vars.md`).
Price catalog seed ships at `src/Urbanova.Api/AppData/pricing/mvp-prices.json`
(copied to output; **placeholder prices, not market data**). Uploads land in
`AppData/uploads/`, rendered reports in `AppData/reports/` (both git-ignored).

## Workflow in 60 seconds (Scalar or curl)

Register → create project → upload GeoJSON → validate → extract geometry →
run analysis → create baseline → create alternative → analyze → compare →
recommendations → cost estimate → report. `WorkflowTests.FullWorkflow_ProjectToReport`
executes exactly this chain; `docs/api.md` lists every route, body, and status code.

## Docs

- `docs/api.md` — endpoint reference (routes, bodies, status + error codes)
- `docs/architecture.md` — layering + request pipeline
- `docs/assumptions.md` — every temporary MVP default (explicitly marked)
- `docs/database-schema.md` — tables, keys, indexes, delete policy
- `docs/env-vars.md` — config + secrets
- `docs/known-limitations.md` — gaps and future work
- `docs/open-validations.md` — 11 unresolved PRD items awaiting sign-off
- `docs/phase-01.md` … `docs/phase-14.md` — per-phase build + verification logs
- `docs/review-fixes.md` — post-review correctness/concurrency fixes + regression tests
