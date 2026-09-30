# Phase 12 — Reporting ✅

## Built

- Domain `Reporting`: canonical `ReportModel` (project, site, analysis, problem areas,
  scenarios, comparison, recommendations, costs, decision summary, caveats) +
  `IReportGenerator` port (new formats = one implementation, zero model changes).
- Infrastructure: `JsonReportGenerator` (canonical source of truth),
  `HtmlReportGenerator` (escaped tables, unavailable-section reasons, no PDF dep),
  `LocalReportFileStore`, `ReportService` (owner-scoped assembly from project, latest
  run, scenarios, optional comparison reuse, idempotent recommendation resolution,
  costs; canonical JSON always stored + hashed; HTML additionally filed; per-project
  versioning). No migration (Report entity unchanged).
- Api: `ReportsController` — `POST /api/projects/{id}/reports` (201),
  `GET /api/reports/{id}`, `GET /api/reports/{id}/file` (HTML download; 404 for JSON).

## Decisions / MVP limits (documented assumptions, not final)

- Reports generate on empty projects with Unavailable sections (snapshot, not 400).
- Recommendation resolution reuses the idempotent service (benign refresh side effect).
- Unknown formats → 400; PDF deferred.
- Reports carry reference-only spatial identity (`spatialReferences`: displayed
  analysis, recommendation context, separate Baseline/Alternative sides with run,
  file, and hash references plus retrieval metadata). No coordinates, geometry,
  or maps are embedded; Analysis and Comparison may describe different runs by
  design (project-latest vs requested pair).

## Verified

```text
dotnet test Urbanova.slnx → Unit 106/106, Integration 76/76 (182 total, 0 failed)
```

New tests: `ReportGeneratorTests` (3: canonical shape, XSS escaping, unavailable rendering)
+ `ReportsTests` (4 HTTP: full report incl. comparison/costs/decisions + versioning,
HTML + download, empty-project snapshot, 400/404/403/401). Mid-phase catch: reports
silently omitted recs without a prior endpoint call — fixed via service reuse; second
catch: Acceptable latest runs correctly yield zero recs (fixture adjusted, engine untouched).

## Next (Phase 13 — Testing)

Full-workflow integration test (Project → Report per PRD §23) + failure-scenario sweep.

> Current state (post-PR #7, historical counts above retained): reports carry full
> recommendation traceability with real linked costs, mixed-currency-safe aggregates, and
> reference-only spatial identity (`polygonIndexBase: 0`; `sourceEngineeringFileId` survives
> deletion while `liveEngineeringFileId` nulls; evaluated-empty runs keep context with count 0;
> displayed analysis and comparison sides are separate references; no embedded geometry or maps).
> Current suite: 265/265 (145 unit + 120 integration).
