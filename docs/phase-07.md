# Phase 7 — Environmental Analysis ✅

## Built

- Domain `Analysis`: `AnalysisInput` (NormalizedGeometry + params — never raw files),
  `RawAreaValue`/`AreaValue`, `EngineAnalysisResult` (IsEstimated mandatory),
  `IEnvironmentalAnalysisEngine` + `IEnvironmentalClassificationService` ports.
- Infrastructure `Analysis`: `HeatV01Engine` (pure/deterministic:
  base − veg·kVeg − shade·kShade + (albedoRef − albedo)·kAlbedo, all constants from
  `HeatAnalysisOptions`), `ThresholdClassificationService` (bands only from
  `ClassificationOptions`), `AnalysisService` (ownership → valid file → geometry →
  engine → classification → run + result in ONE SaveChanges; engine exceptions persist
  a Failed run then surface ANALYSIS_FAILED; idempotent replay on (project, InputHash)).
- Api: `AnalysisController` — `POST /api/projects/{id}/analysis` (201 fresh / 200 replay),
  `GET /api/analysis/{runId}`; 404/403/422/500 mapping, never partial-valid.
- Config: `HeatAnalysis` numerics made explicit in appsettings (still temporary).

## Decisions / MVP limits (documented assumptions, not final)

- Closed parameter set {vegetationCoverPct, albedo, shadingPct}; unknown keys → 400.
- Single engine registered; multi-engine registry when the second method arrives.
- Geometry snapshot = file hash + geometry hash + params (rings recomputable from file +
  processor version); full rings live in AnalysisResult.ValuesJson for display.
- All results estimated; classification bands 30/35 temporary.

## Verified

```text
dotnet test Urbanova.slnx → Unit 82/82, Integration 50/50 (132 total, 0 failed)
```

New tests: `HeatEngineTests` (8: defaults/directions/determinism/bands/summary),
`AnalyzeRequestValidatorTests` (5), `AnalysisTests` (6 HTTP: 201 + values + snapshots,
replay 200 + single row, 400, invalid-file 422, 404/403 guards, run get + cross-owner 403).

## Next (Phase 8 — Scenarios)

Baseline locking + alternative inheritance with configurable params,
`POST /api/projects/{id}/scenarios`, `PUT /api/scenarios/{id}`,
`POST /api/scenarios/{id}/analyze` (scenario params feed the engine).
