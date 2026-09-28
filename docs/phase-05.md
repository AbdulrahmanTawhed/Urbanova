# Phase 5 — Engineering File Processing ✅

## Built

- Domain: `IEngineeringFileProcessor` (FormatName, extension/content-type detection,
  Validate + GetMetadata; geometry methods extend it in Phase 6),
  `FileValidationResult` + `EngineeringFileMetadata` value objects.
- Application `EngineeringFiles` slice: `FileResponse` DTO, coded `FileException`
  (404/403/409/413/415/422/500), owner-scoped `IFileService`, `IFileStorage` +
  `IProcessorRegistry` ports.
- Infrastructure `FileProcessing`: `GeoJsonFileProcessor` (FeatureCollection + Feature,
  typed geometries, coordinate-array checks, counts/types/CRS/bbox metadata),
  first-match `ProcessorRegistry` (new formats = register another processor, zero changes),
  `LocalFileStorage` (`{Root}/{projectId}/{fileId}_{sanitized}`, traversal-safe, best-effort delete),
  `FileService` (size guards, SHA-256 dedupe → 409, eager validation + metadata, camelCase
  stored JSON, explicit file delete). No migration (no schema change).
- Api: `FilesController` — `POST /api/projects/{id}/files` (multipart, 55 MB endpoint cap
  so the app returns 413 not the server), `GET` list + details, `POST /files/{id}/validate`,
  `DELETE /files/{id}`; invalid content persists as `Invalid` for traceability but answers 422.

## Decisions / MVP limits (documented assumptions, not final)

- Registry miss = generic-stub rejection naming every supported format (no silent accept).
- Null geometries rejected (analysis needs geometry); ring-closure + deep CRS validation → Phase 6.
- Upload validates synchronously (background jobs deferred); `extract-geometry` endpoint → Phase 6.
- Duplicate content → 409 (no silent overwrite); storage failures → centralized 500 (no leak).

## Verified

```text
dotnet test Urbanova.slnx → Unit 55/55, Integration 40/40 (95 total, 0 failed)
```

New tests: `GeoJsonProcessorTests` (17: valid collection/feature + metadata, 9 invalid cases,
detection matrix, registry) + `FilesTests` (10 HTTP: 201 + metadata/hash/row, content-type
detection, 422 + stored-Invalid, 415 + nothing stored, 409, re-validate, 403/404 + scoping,
delete, 401) + service-level oversize guard.

## Next (Phase 6 — Geometry Abstraction)

`NormalizedGeometry` VO, Extract/Normalize on the processor interface, GeoJSON polygon
extraction + area computation, `POST /api/files/{id}/extract-geometry`.
