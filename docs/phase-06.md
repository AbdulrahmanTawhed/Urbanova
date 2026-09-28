# Phase 6 — Geometry Abstraction ✅

## Built

- Domain: `NormalizedGeometry` (+ `NormalizedPoint/Polygon`, `GeometryExtractionResult`)
  — the ONLY geometry type analysis may consume (PRD §7); `IEngineeringFileProcessor`
  extended with `ExtractGeometryAsync` (raw, source CRS) + `NormalizeGeometryAsync`
  (closed rings, defaulted CRS, recomputed areas/bboxes/totals, pure function).
- Infrastructure: GeoJSON Polygon/MultiPolygon extraction (Z ignored, finite-number guards,
  malformed coordinates → explained failure, not throw), shoelace areas with hole
  subtraction, non-polygonal features counted as skipped; `GeometryService`
  (Valid-file gate → processor → normalize; failures → `GEOMETRY_EXTRACTION_FAILED`),
  `IGeometryService` DI registration. No migration (stateless — Phase 7 persists snapshots).
- Api: `POST /api/files/{id}/extract-geometry` → 200 `GeometryResponse` (full rings +
  per-polygon areas/bboxes); `GEOMETRY_EXTRACTION_FAILED` → 422.

## Decisions / MVP limits (documented assumptions, not final)

- Areas are planar CRS units (`deg²` for EPSG:4326) — NOT square meters; geodesic/projected
  area computation awaits the validated engineering method (affects Phase 7 heat math).
- Polygons only; points/lines skip (counted, visible in response).
- Single invalid geometry fails the whole extraction (stop-analysis semantics, PRD §19);
  partial-success mode deferred.

## Verified

```text
dotnet test Urbanova.slnx → Unit 63/63, Integration 44/44 (107 total, 0 failed)
```

New tests: `GeometryExtractionTests` (8: square/triangle/hole/multipolygon areas,
ring closure, Z, points-only failure, CRS preservation) + `GeometryTests` (4 HTTP:
200 + areas, invalid-file 422, points-only 422 with code, 403/404/401).

## Next (Phase 7 — Environmental Analysis)

`IEnvironmentalAnalysisEngine` (HeatV01, config-driven), classification service,
`POST /api/projects/{id}/analysis` + `GET /api/analysis/{runId}`, AnalysisRun snapshots
+ InputHash determinism, estimated-vs-validated flags.
