# URBANOVA
AI-powered Urban Climate & Site Intelligence Platform  
Grow Cooler Cities, One Decision at a Time.


## What is URBANOVA?
URBANOVA helps real estate developers, consulting firms, and engineers analyze a site before and during design, and make design decisions based on data instead of experience or manual estimation alone.

The platform ingests site data — land surface temperature, green area coverage, building density, sun exposure, shading, wind, and more — entirely from open satellite imagery and open climate datasets, then uses AI to identify areas and factors negatively affecting Outdoor Comfort (starting with Urban Heat Island risk).

Because URBANOVA relies on openly available data rather than on-site hardware, any site can be analyzed remotely, at no acquisition cost, anywhere satellite and climate coverage exists.

## What it delivers
- **Site Assessment & Environmental Report** — current site status, key problem areas, contributing factors, and a data-driven Future Outlook
- **Findings & Recommendations** — ranked mitigation options (e.g. increased green cover, shading structures, material changes) compared by expected impact, feasibility, and preliminary cost
- **Scenario Comparison** — current state vs. proposed interventions, to support developer/consultant decision-making

## MVP Scope
The MVP focuses on Urban Heat as the first strong use case: open satellite + climate data → AI-based risk classification → prescriptive, cost-aware recommendations.

## Roadmap
The architecture is designed to extend without a rebuild:

- **Year-round intelligence:** winter use cases (rainfall, humidity, air quality, water accumulation prediction) on the same data pipeline
- **Design-phase support:** pre-design site insights for engineers, reducing manual site analysis time
- **3D Architecture Simulation:** a digital twin of the project enabling virtual walkthroughs — usable both as a real estate marketing tool and, more importantly, connected to the same Analysis Engine to simulate the impact of design changes
- **Smart City expansion:** broader environmental intelligence use cases beyond individual sites

## Tech Direction
- **Backend:** .NET
- **AI/ML:** Python (geospatial & climate data processing, prediction models)
- **Data sources:** open satellite imagery and open climate datasets (e.g. Landsat, Sentinel, ECOSTRESS, ERA5) — specific datasets still being finalized

---

## Backend (.NET 10 + SQL Server)

[![CI](https://github.com/AbdulrahmanTawhed/Urbanova/actions/workflows/ci.yml/badge.svg)](https://github.com/AbdulrahmanTawhed/Urbanova/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Production-quality backend implementing the PRD workflow
Project → File → Validation → Geometry → Analysis → Baseline/Alternative →
Modify → Recalculate → Compare → Recommend → Cost → Report.

> Status: **PRD v0.2 compliant** — configurable scenario catalog, evidence-backed
> recommendations, traceable quantities; 219/219 tests green. See `docs/prd-v0.2.md`.
> Every unresolved PRD requirement maps to an abstraction + configurable MVP
> default documented in `docs/assumptions.md` (nothing hard-coded as final).

Stack: .NET 10 (SDK pinned in `global.json`), ASP.NET Core Web API, OpenAPI + Scalar UI,
EF Core 10 + SQL Server, ASP.NET Identity + JWT Bearer (access + rotating refresh),
xUnit + FluentAssertions + WebApplicationFactory.

Layout: `src/Urbanova.Domain/` (dependency-free) · `src/Urbanova.Application/`
(use cases, DTOs, validators) · `src/Urbanova.Infrastructure/` (EF Core, Identity,
adapters) · `src/Urbanova.Api/` (controllers, auth, OpenAPI) · `tests/` · `docs/`.

### Backend setup

Prerequisites: .NET 10 SDK, SQL Server LocalDB (`sqllocaldb start MSSQLLocalDB`).

```powershell
dotnet user-secrets init --project src/Urbanova.Api
dotnet user-secrets set "Jwt:Key" "<32-plus-character-secret>" --project src/Urbanova.Api
dotnet ef database update --project src/Urbanova.Infrastructure --startup-project src/Urbanova.Api
dotnet build Urbanova.slnx
dotnet test Urbanova.slnx   # integration tests use LocalDB database Urbanova_Test
dotnet run --project src/Urbanova.Api  # http://localhost:5202, docs at /scalar/v1
```

`WorkflowTests.FullWorkflow_ProjectToReport` executes the whole chain end to end;
`docs/api.md` lists every route, body, and status code. Price catalog seed
(`src/Urbanova.Api/AppData/pricing/mvp-prices.json`) holds **placeholder prices,
not market data**. Uploads (`AppData/uploads/`) and rendered reports
(`AppData/reports/`) are git-ignored.

### Backend docs

- `docs/api.md` — endpoint reference (routes, bodies, status + error codes)
- `docs/architecture.md` — layering + request pipeline
- `docs/assumptions.md` — every temporary MVP default (explicitly marked)
- `docs/database-schema.md` — tables, keys, indexes, delete policy
- `docs/env-vars.md` — config + secrets
- `docs/known-limitations.md` — gaps and future work
- `docs/open-validations.md` — 11 unresolved PRD items awaiting sign-off
- `docs/phase-01.md` … `docs/phase-14.md` — per-phase build + verification logs
- `docs/review-fixes.md` — post-review correctness/concurrency fixes + regression tests
