# URBANOVA
AI-powered Urban Climate & Site Intelligence Platform  
Grow Cooler Cities, One Decision at a Time.

[![CI](https://github.com/AbdulrahmanTawhed/Urbanova/actions/workflows/ci.yml/badge.svg)](https://github.com/AbdulrahmanTawhed/Urbanova/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)


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

Production-quality backend implementing the PRD workflow
Project → File → Validation → Geometry → Analysis → Baseline/Alternative →
Modify → Recalculate → Compare → Recommend → Cost → Report.

> Status: **PRD v0.2 compliant + review fixes** — all modules built, **226/226 tests green**
> (`dotnet test Urbanova.slnx`: 130 unit + 96 integration).
> Every unresolved PRD requirement maps to an abstraction + configurable MVP
> default documented in `docs/assumptions.md` (nothing hard-coded as final).

### What was built, in order

- **Phases 1–2 — Foundation:** Clean Architecture solution (`Domain`, `Application`,
  `Infrastructure`, `Api`), SQL Server schema with 4 EF Core migrations
  (`InitialCreate`, `AuthRefreshTokens`, `RecommendationPolygonIndex`,
  `ConcurrencyGuards`), audit stamps, ownership + concurrency tokens.
- **Phase 3 — Auth:** ASP.NET Identity + JWT Bearer (access + rotating refresh tokens
  with reuse detection), `MustOwnProject` resource policy, secure-by-default
  fallback authorization, RFC7807 error responses.
- **Phase 4 — Projects:** full CRUD with sites/boundaries, owner-scoped listing +
  pagination, optimistic concurrency, explicit child delete order.
- **Phase 5 — Engineering files:** pluggable `IEngineeringFileProcessor` registry
  (GeoJSON + reject-all-else stub), upload → detect → validate → metadata,
  SHA-256 dedupe, size guards, local-disk storage behind an abstraction.
- **Phase 6 — Geometry:** format-independent `NormalizedGeometry` (extract +
  normalize), polygon areas + bounding boxes; analysis consumes only this type.
- **Phase 7 — Heat analysis:** config-driven `HeatV01` engine, threshold
  classification service, traceable `AnalysisRun` snapshots with `InputHash`
  determinism (identical inputs replay the stored run).
- **Phase 8 — Scenarios:** locked baselines inheriting run inputs, alternatives with
  parameter merge, versioning, scenario-scoped re-analysis.
- **Phase 9 — Comparison:** baseline-vs-alternative deltas, class transitions,
  new-problem flags, rule-based trade-offs; cost/feasibility honestly Unavailable.
- **Phase 10 — Recommendations:** explainable rule engine (Problem → Cause →
  Intervention → Impact → Evidence → Cost → Feasibility), mandatory evidence
  levels (never `Validated`), idempotent generate-and-store.
- **Phase 11 — Cost estimation:** `Quantity × UnitPrice` core, file-backed price
  catalog, `Unavailable` instead of invented prices, recommendation/scenario links
  feeding the comparison cost dimension.
- **Phase 12 — Reporting:** canonical JSON model + HTML renderer, decision-support
  summary with caveats, versioned + hashed report rows, file download.
- **Phases 13–14 — Testing & docs:** full PRD workflow as one executable test,
  failure-scenario sweep, OpenAPI + Scalar UI, complete `docs/` reference.
- **Review rounds:** fixed file-delete ordering, stack-trace loss, null guards,
  silent classification defaults, casing, race-safe inserts (unique indexes +
  retries), transactional deletes/registration, mixed-currency guard, HTML
  encoding, extension allowlist.
- **PRD v0.2 update:** configurable scenario-parameter catalog (3 approved keys +
  bands + max-count cap, coded errors), `RecommendationRule` evidence registry
  with seeded rules and `NO_EVIDENCE` blocking, traceable quantities
  (`UserProvided`/`DerivedFromGeometry`), backfill migration for pre-v0.2 rows.
- **Latest fix:** cost derivation now computes **geodesic m²** (spherical earth)
  from re-extracted polygon rings instead of relabeling planar deg² — fixing the
  zero-quantity bug on real-world-scale plots; non-EPSG:4326 and zero-rounding
  cases reject instead of storing 0.

### Setup

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
- `docs/prd-v0.2.md` — v0.2 change log and decisions
- `docs/review-fixes.md` — post-review correctness/concurrency fixes + regression tests
