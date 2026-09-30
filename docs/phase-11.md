# Phase 11 — Cost Estimation ✅

## Built

- Domain `Costing`: `IPriceCatalog` port (miss = normal outcome, never invented).
- Application `Costing` slice: DTOs, coded `CostException` (404/403/400), validators,
  owner-scoped `ICostService`.
- Infrastructure: `FilePriceCatalog` (JSON items, case-insensitive codes, missing/malformed
  file → miss), `CostService` (direct price → Calculated via `CostRules`; catalog hit →
  Calculated with catalog price + source; miss → stored Unavailable row with Total 0;
  neither → 400; recommendation/scenario links validated + back-filled).
- Api: `CostEstimatesController` — `POST/GET /api/projects/{id}/cost-estimates`,
  `GET /api/cost-estimates/{id}`.
- Comparison cost dimension now fills from scenario-linked Calculated estimates
  (`Calculated` + totals + delta, else Unavailable with reason); `CostDeltaDto` added.
- Catalog seed `src/Urbanova.Api/AppData/pricing/mvp-prices.json` (copy-to-output,
  explicitly marked placeholders).

## Decisions / MVP limits (documented assumptions, not final)

- Unknown codes answer 201 Unavailable (tracked, honest) rather than 422.
- No regional/dynamic pricing; single currency per estimate (default USD).
- Catalog read per lookup (tiny file, always fresh); caching deferred with a real feed.

## Verified

```text
dotnet test Urbanova.slnx → Unit 103/103, Integration 72/72 (175 total, 0 failed)
```

New tests: `PriceCatalogTests` (7: hit/case/miss/missing/malformed/validators) +
`CostTests` (7 HTTP: direct total, catalog total + source, Unavailable-not-invented,
400, recommendation back-fill, comparison cost totals 100/60/−40, guards).

## Next (Phase 12 — Reporting)

`IReportGenerator` (JSON canonical + HTML), decision-support summary,
`POST /api/projects/{id}/reports` + `GET /api/reports/{id}`.

> Current state (post-PR #7, historical counts above retained): quantities are traceable
> (`UserProvided`/`DerivedFromGeometry` from analyzed polygon geodesic m² for area units);
> second estimates on an already-linked recommendation are rejected; cross-project references
> return 404; pair-specific comparison totals and project-wide report aggregates both refuse
> mixed-currency sums (`Unavailable` + reason, never a cross-currency total or `0 USD`).
> Current suite: 265/265 (145 unit + 120 integration).
