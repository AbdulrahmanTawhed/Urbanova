# Phase 9 — Scenario Comparison ✅

## Built

- Domain `Comparison`: `ComparisonInput` (classified runs + params), `AreaDelta`/
  `EnvironmentalDelta`/`ParameterChange`/`DimensionStatus`/`ScenarioComparison`,
  swappable `IScenarioComparisonService` port.
- Infrastructure: `ComparisonEngine` (index-aligned deltas, heat semantics lower-is-better,
  class-transition/new-problem detection, rule-based tradeoff statements, cost/feasibility
  honestly Unavailable), `ComparisonService` (ownership, Baseline-vs-Alternative kind
  enforcement, latest-succeeded-run with linked-baseline-run fallback, same-geometry guard).
- Api: `GET /api/projects/{id}/comparison?baselineId=&alternativeId=` (200; 400/403/404).

## Decisions / MVP limits (documented assumptions, not final)

- Kinds enforced (baseline must be Baseline, alternative Alternative); swapped roles → 400.
- Same polygon count required; mismatched geometry → 400 with explanation (no guessing).
- Cost/feasibility dimensions return Unavailable until Phases 10–11 link data.
- Comparison computed on demand, never stored.

## Verified

```text
dotnet test Urbanova.slnx → Unit 91/91, Integration 62/62 (153 total, 0 failed)
```

New tests: `ComparisonEngineTests` (3: deltas/transitions/tradeoffs, worsening flags,
mismatch throws) + `ComparisonTests` (4 HTTP: full delta assertions, unanalyzed 400,
kind-mismatch 400, 403 + missing-query 400). The suite caught a real gap mid-phase:
baselines created FROM a run had no scenario-scoped run — fixed via linked-run fallback.

## Next (Phase 10 — Recommendations)

`IRecommendationEngine` (Problem → Cause → Intervention → Impact → Evidence → Cost →
Feasibility), evidence levels, `GET /api/projects/{id}/recommendations`.
