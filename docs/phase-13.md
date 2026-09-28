# Phase 13 — Testing (Workflow + Failure Sweep) ✅

## Built

- `WorkflowTests.FullWorkflow_ProjectToReport`: the PRD §23 Definition of Done as one
  executable chain (project+site → upload → validate → geometry → analysis → baseline →
  alternative → modify/version → recalculate → compare → recommend → cost → report),
  asserting values (areas 4.0, recalc 31.0, Δ −1.0), evidence grading, cost totals,
  report sections, and final row counts per aggregate.
- `FailureTests` (6, PRD §19): unsupported file (415 + explanation + nothing stored),
  invalid file (422 upload + analysis blocked), geometry failure (422, zero runs stored),
  failed runs never read as valid (500 with code), missing cost (Unavailable, zeros —
  never invented), file deletion keeps runs readable (SET NULL, values intact).

## Verified

```text
dotnet test Urbanova.slnx → Unit 106/106, Integration 83/83 (189 total, 0 failed)
```

Mid-phase catches: expression-tree assertion fix; Acceptable latest runs correctly yield
zero recommendations (fixtures adjusted, engine untouched — twice, in workflow + reports).

## Next (Phase 14 — API Documentation)

OpenAPI annotations pass, `docs/api.md` endpoint reference, README quickstart,
known limitations + setup finalization.
