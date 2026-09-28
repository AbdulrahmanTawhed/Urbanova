# Phase 4 — Project Management ✅

## Built

- Application `Projects` slice: `ProjectDtos` (create/update/response/site/paged — entities never
  leave the service), `ProjectException` (NOT_FOUND→404, FORBIDDEN→403, CONCURRENCY_CONFLICT→409),
  `IProjectService` (all methods owner-scoped), `Common/ICurrentUserService`, FluentValidation
  validators (name/description/status/site bounds incl. lat/lon ranges, positive area).
- Infrastructure `Projects/ProjectService`: owner-scoped CRUD, `MaxPageSize = 100`,
  `CreatedAt`-ordered paging, `RowVersion` optimistic concurrency (stale → 409),
  blank CRS defaults to `EPSG:4326`, explicit delete order
  (reports → recommendations → cost estimates → alternative scenarios → baselines →
  analysis runs → files → site → project) honoring RESTRICT FKs.
- Api: `ProjectsController` (`POST/GET/GET{id}/PUT/DELETE /api/projects`, 201 + `Location`),
  404-before-403 on id-scoped reads, `MustOwnProject` policy on mutating paths with
  service re-checks (defense in depth), `HttpContextCurrentUserService` (JWT sub claim),
  validator + `HttpContextAccessor` wiring.

## Verified

```text
dotnet test Urbanova.slnx → Unit 38/38, Integration 30/30 (68 total, 0 failed)
```

New tests: `ProjectValidatorsTests` (10) + `ProjectsTests` (11 HTTP: create+site/400/401,
owner-only list + paging, 200/403/404 reads, update + CRS default, stale-write 409,
cross-owner update 403, delete cascades children explicitly + 403/404 guards).

## Next (Phase 5 — Engineering File Processing)

`IEngineeringFileProcessor` registry, `IFileStorage` (local disk), upload → detect →
validate → metadata endpoints, size/type/hash-dedupe guards, `UNSUPPORTED_FORMAT`/`INVALID_FILE` errors.
