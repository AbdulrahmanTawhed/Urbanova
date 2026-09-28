# Phase 8 — Scenarios ✅

## Built

- Application `Scenarios` slice: DTOs, coded `ScenarioException` (404/403/409/400),
  validators (closed MVP parameter set), owner-scoped `IScenarioService`.
- Infrastructure `ScenarioService`: baseline creation (locked at birth, params inherited
  from linked analysis run's preserved inputs, request merge wins), alternative creation
  via domain `ScenarioRules.CreateAlternative` (baseline-only parents, param merge),
  mutable alternatives with Version++ and RowVersion concurrency, analyze delegating to
  the shared pipeline.
- Analysis extension: `RunForScenarioAsync` (scenario params + baseline-run file or
  explicit fileId; InputHash scoped with scenario id; direct-run hashes frozen),
  `ScenarioId` added to `AnalysisRunResponse`. Shared `ExecuteAsync` pipeline.
- Api: `ScenariosController` — `POST/GET /api/projects/{id}/scenarios`,
  `GET/PUT /api/scenarios/{id}`, `POST /api/scenarios/{id}/analyze`
  (201 fresh / 200 replay; both ScenarioException and AnalysisException mapped).

## Decisions / MVP limits (documented assumptions, not final)

- Alternatives inherit baselines only (one level); deeper nesting rejected.
- Scenario analyze takes no ad-hoc params — modify the scenario first (traceability).
- No scenario delete endpoint (deferred; alternatives accumulate, documented).

## Verified

```text
dotnet test Urbanova.slnx → Unit 88/88, Integration 58/58 (146 total, 0 failed)
```

New tests: `ScenarioValidatorTests` (6) + `ScenariosTests` (8 HTTP: baseline inheritance
+ lock, alternative merge, baseline-409, versioning + stale-409, scenario analyze with
params + idempotent replay, no-source 422, 403/404 guards, scoping).

## Next (Phase 9 — Comparison)

`IScenarioComparisonService`: baseline vs alternative on environment/cost/feasibility,
`GET /api/projects/{id}/comparison?baselineId=&alternativeId=`.
