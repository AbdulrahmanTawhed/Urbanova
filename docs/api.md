# URBANOVA API Reference (Phase 14)

Base URL local: `http://localhost:5202`. Interactive docs (Development):
`/scalar/v1`, machine-readable `/openapi/v1.json` (covered by `ApiDocsTests`).

Auth: JWT Bearer (`Authorization: Bearer <accessToken>`). Fallback policy requires
authentication on every endpoint except: `POST /api/auth/*`, `/health/*`,
`/api/system/info`, `/openapi/*`, `/scalar/*`, `/`. Errors are RFC 7807
`ProblemDetails` with `code` + `traceId` extensions.

## Conventions

- Request/response bodies are DTOs (EF entities never exposed).
- Ownership: id-scoped reads return 404 missing / 403 not-owned; lists are owner-scoped.
- Pagination: `?page=1&pageSize=20` (max 100) → `{items, page, pageSize, totalCount, totalPages}`.
- Concurrency: PUT bodies carry `rowVersion` (base64) from a prior GET; stale → 409.

## Auth

| Method | Route | Auth | Success | Errors |
|---|---|---|---|---|
| POST | `/api/auth/register` `{email, password≥8, displayName?}` | anon | 201 AuthResponse | 400 validation, 409 EMAIL_TAKEN |
| POST | `/api/auth/login` `{email, password}` | anon | 200 AuthResponse | 401 INVALID_CREDENTIALS |
| POST | `/api/auth/refresh` `{refreshToken}` | anon | 200 AuthResponse (rotated; replay → 401) | 401 INVALID_REFRESH_TOKEN |
| GET | `/api/auth/me` | JWT | 200 CurrentUser | 401 |

`AuthResponse = {accessToken, refreshToken, expiresAtUtc, userId, email}`.

## Projects + Sites

| Method | Route | Success | Errors |
|---|---|---|---|
| POST | `/api/projects` `{name, description?, site?{address, latitude −90..90, longitude −180..180, boundaryGeoJson?, crs?, areaM2>0}}` | 201 + Location | 400, 401 |
| GET | `/api/projects?page=&pageSize=` | 200 PagedResult | 401 |
| GET | `/api/projects/{id}` | 200 ProjectResponse | 401/403/404 |
| PUT | `/api/projects/{id}` `{name, description?, status? Draft\|Active\|Archived, site?, rowVersion!}` | 200 | 400/401/403/404/409 |
| DELETE | `/api/projects/{id}` (explicit child delete order) | 204 | 401/403/404 |

## Engineering Files + Geometry

| Method | Route | Success | Errors |
|---|---|---|---|
| POST | `/api/projects/{id}/files` multipart `file` (≤50 MB) | 201 FileResponse (Valid) | 400 empty, 401/403/404, 409 FILE_DUPLICATE, 413, 415 UNSUPPORTED_FORMAT, 422 INVALID_FILE (stored as Invalid) |
| GET | `/api/projects/{id}/files` | 200 FileResponse[] | 401/403/404 |
| GET | `/api/files/{id}` | 200 FileResponse | 401/403/404 |
| POST | `/api/files/{id}/validate` (re-run) | 200 FileResponse | 401/403/404, 422, 415 |
| POST | `/api/files/{id}/extract-geometry` | 200 GeometryResponse `{crs, areaUnit, totalArea, polygons[{rings, area, bbox}]}` | 401/403/404, 422 (Invalid file or GEOMETRY_EXTRACTION_FAILED) |
| DELETE | `/api/files/{id}` | 204 | 401/403/404 |

Areas are planar CRS units (`deg²` for EPSG:4326) — not m².

## Analysis

| Method | Route | Success | Errors |
|---|---|---|---|
| POST | `/api/projects/{id}/analysis` `{fileId, parameters{vegetationCoverPct 0–100, albedo 0–1, shadingPct 0–100}}` | 201 fresh / 200 replay (same inputs+config) | 400 unknown params, 401/403/404, 422 invalid file/geometry, 500 ANALYSIS_FAILED |
| GET | `/api/analysis/{runId}` | 200 AnalysisRunResponse | 401/403/404, 500 failed run |

`AnalysisRunResponse` carries engine/method/config versions, `inputHash`, per-area
`{polygonIndex, area, value, classification}`, summary counts, `isEstimated: true`.

## Scenarios

| Method | Route | Success | Errors |
|---|---|---|---|
| POST | `/api/projects/{id}/scenarios` `{name, baseAnalysisRunId?, parentScenarioId?, parameters?}` (no parent → locked Baseline; parent → Alternative inheriting baseline; parameters validated against the `ScenarioParameters` catalog, max 3) | 201 | 400 incl. UNSUPPORTED_/INVALID_/TOO_MANY_SCENARIO_PARAMETER, 401/403/404 |
| GET | `/api/projects/{id}/scenarios` | 200 ScenarioResponse[] | 401/403/404 |
| GET | `/api/scenarios/{id}` | 200 | 401/403/404 |
| PUT | `/api/scenarios/{id}` `{name?, parameters?, rowVersion!}` (alternatives only; Version++; catalog-validated) | 200 | 400 incl. parameter codes, 401/403/404, 409 SCENARIO_LOCKED / stale |
| POST | `/api/scenarios/{id}/analyze` `{fileId?}` (baseline-run file default) | 201 fresh / 200 replay | 401/403/404, 422 no-source/geometry |

## Comparison

| Method | Route | Success | Errors |
|---|---|---|---|
| GET | `/api/projects/{id}/comparison?baselineId=&alternativeId=` | 200 ComparisonResponse `{whatChanged, environmental{areas, means, transitions}, cost{status|totals}, feasibility, tradeoffs}` | 400 missing query/unanalyzed/kind-mismatch/geometry-mismatch, 401/403/404 |

Cost dimension fills from scenario-linked Calculated estimates, else Unavailable.

Feasibility dimension stays Unavailable (`No feasibility data linked to these scenarios yet`):
no comparison-feasibility methodology exists, and calculated scenario costs alone
do not create a feasibility verdict.

## Recommendations

| Method | Route | Success | Errors |
|---|---|---|---|
| GET | `/api/projects/{id}/recommendations?runId=` (default: latest succeeded run) | 200 RecommendationResponse[] `{problem, cause, intervention, expectedImpact, ruleCode, evidenceSource, evidenceLevel, feasibility, confidence: null, scientificReferences[], cost: Unavailable}` | 400 no analysis, 401/403/404, 422 NO_EVIDENCE (rule unknown/inactive; nothing stored) |

Evidence is Calculated (problem) / Estimated (preventive) — never Validated.

Feasibility is a heuristic recommendation-level string (High/Medium/Low bands on
intervention magnitude under the current vegetation rule), not a validated
engineering verdict: not technical feasibility, affordability, or constructability.
It is persisted at generation and is independent of any later linked CostEstimate
status; `confidence` remains null and `EvidenceLevel` does not validate it.

## Cost Estimates

| Method | Route | Success | Errors |
|---|---|---|---|
| POST | `/api/projects/{id}/cost-estimates` `{quantity?≥0 (nullable), unit, unitPrice?\|itemCode?, currency?=USD, recommendationId?, scenarioId?}` (quantity omitted + rec + m² → derived from polygon area as `DerivedFromGeometry`, else 400) | 201 (Calculated or Unavailable; response includes `quantitySource`) | 400 neither price nor code / bad links / quantity missing, 401/403/404 |
| GET | `/api/projects/{id}/cost-estimates` | 200 list | 401/403/404 |
| GET | `/api/cost-estimates/{id}` | 200 | 401/403/404 |

## Reports

| Method | Route | Success | Errors |
|---|---|---|---|
| POST | `/api/projects/{id}/reports` `{format?=Json\|Html, baselineScenarioId?, alternativeScenarioId?}` | 201 ReportResponse `{format, version, payloadHash, hasFile, content}` | 400 bad format, 401/403/404 |
| GET | `/api/reports/{id}` | 200 ReportResponse | 401/403/404 |
| GET | `/api/reports/{id}/file` | 200 text/html download (HTML reports; JSON → 404) | 401/403/404 |

Report `costs` is a project-wide aggregate over Calculated estimates:
`{calculatedCount, unavailableCount, status, reason, calculatedTotal?, currency?}`.
`status` is `Calculated` only when every Calculated estimate shares one currency
(total + currency reported); mixed currencies or no Calculated estimates yield
`Unavailable` with `reason` and null total/currency — never a cross-currency sum,
never `0 USD`. No currency conversion exists in the MVP.

## System

| Method | Route | Auth | Notes |
|---|---|---|---|
| GET | `/health/live`, `/health/ready` (incl. `urbanova-db` probe when configured) | anon | 200 |
| GET | `/api/system/info` | anon | build/env metadata |
| GET | `/`, `/scalar/v1`, `/openapi/v1.json` | anon (Development docs) | — |

## Error codes

`EMAIL_TAKEN, INVALID_CREDENTIALS, INVALID_REFRESH_TOKEN, REGISTRATION_INVALID,
NOT_FOUND, FORBIDDEN, CONCURRENCY_CONFLICT, SCENARIO_LOCKED, EMPTY_FILE,
FILE_DUPLICATE, FILE_TOO_LARGE, UNSUPPORTED_FORMAT, INVALID_FILE,
GEOMETRY_EXTRACTION_FAILED, NO_ANALYSIS, NO_EVIDENCE, INVALID_COMPARISON, INVALID_COST,
INVALID_REPORT, INVALID_SCENARIO, UNSUPPORTED_SCENARIO_PARAMETER,
INVALID_SCENARIO_PARAMETER, TOO_MANY_SCENARIO_PARAMETERS,
ANALYSIS_FAILED, FILE_STORAGE_ERROR, INTERNAL_ERROR`
