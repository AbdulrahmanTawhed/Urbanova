# Phase 10 — Recommendations ✅

## Built

- Domain `Recommendations`: full explainable structure (Problem → Cause → Intervention →
  Expected Impact → Evidence → Cost → Feasibility), `IRecommendationEngine` port.
- Infrastructure: `RuleRecommendationEngine` (problem areas → vegetation intervention sized
  by HeatV01 sensitivities, Calculated; moderate → preventive advice, Estimated; acceptable
  → none; confidence always null; heuristic feasibility bands) + `RecommendationService`
  (explicit runId or latest succeeded run; atomic idempotent refresh of stored rows;
  cost honestly Unavailable).
- Api: `GET /api/projects/{id}/recommendations[?runId=]` (200; 400/403/404).
- Schema: `Recommendations.PolygonIndex` (`RecommendationPolygonIndex` migration).

## Decisions / MVP limits (documented assumptions, not final)

- Nothing is ever Validated; confidence model deferred (null, not zero).
- Vegetation sizing targets strict band exit (floor + 1); admits insufficiency beyond 100%.
- Cost linkage deferred to Phase 11 (FK ready, DTO carries Unavailable).

## Verified

```text
dotnet test Urbanova.slnx → Unit 96/96, Integration 65/65 (161 total, 0 failed)
```

New tests: `RecommendationEngineTests` (5: sizing math, preventive, silence on acceptable,
never-Validated, insufficiency admission) + `RecommendationsTests` (3 HTTP: moderate E2E
+ content-idempotency, seeded problem-area path, 400/404/403/401 guards). The suite caught
a band-edge bug mid-phase (35.0 predicted as Moderate) — fixed with strict-exit sizing.

## Next (Phase 11 — Cost Estimation)

`IPriceCatalog` (file-backed MVP), Qty × Price service, `POST /api/projects/{id}/cost-estimates`,
Unavailable instead of invented prices, links to recommendations/scenarios.
