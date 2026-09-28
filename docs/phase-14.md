# Phase 14 — API Documentation + Finalization ✅

## Built

- `docs/api.md`: full reference (all routes, bodies, pagination/concurrency conventions,
  ownership semantics, error-code catalog).
- `docs/known-limitations.md`: MVP limits + engineering gaps (jobs, rate limiting,
  retention, Testcontainers, single-engine dispatch).
- README rewritten as setup guide (prereqs, secrets, migrations, catalog, run, 60-second
  workflow) with final status.
- `ApiDocsTests`: `/openapi/v1.json` reachable and contains routes (annotation guard).

## Verified

```text
dotnet build Urbanova.slnx  → 0 errors
dotnet test Urbanova.slnx   → Unit 106/106, Integration 84/84 (190 total, 0 failed)
dotnet ef migrations list   → InitialCreate, AuthRefreshTokens, RecommendationPolygonIndex
```

## PRD §24 deliverables — all present

Source, schema + 3 migrations, domain/application/infrastructure layers, REST APIs
(`docs/api.md` + `/openapi/v1.json`), auth (Identity + JWT + rotation + MustOwnProject),
file-processing abstraction, geometry module, analysis module, scenarios, comparison,
recommendations, cost estimation, reporting, unit + integration tests, README, setup +
env-var docs, architecture docs, limitations, open validations. Temporary assumptions
enumerated in `docs/assumptions.md`; nothing unresolved is hard-coded as final.
