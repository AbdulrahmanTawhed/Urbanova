# Architecture (Phase 1 — locked)

Clean Architecture, dependency rule: `Api → Application → Domain ← Infrastructure`.

```text
HTTP Request
  → Controller (DTO validation only)
  → Application service / use case
  → Domain logic + business rules
  → Infrastructure (EF Core / storage / engines)
  → DTO + ProblemDetails response
```

- Controllers never contain business logic and never return EF entities (DTOs only, from Phase 4).
- Errors: `ExceptionHandlingMiddleware` → RFC 7807 `ProblemDetails { code, traceId }`.
  Domain codes (`UNSUPPORTED_FORMAT`, `GEOMETRY_EXTRACTION_FAILED`, `ANALYSIS_FAILED`,
  `NO_EVIDENCE`, `COST_UNAVAILABLE`) are mapped here from Phase 5+.
- Config: everything unresolved lives in `IOptions<T>` (`HeatAnalysis`, `Classification`,
  `FileStorage`, `PriceCatalog`, `Reporting`, `Jwt`) — see `assumptions.md`.
- Auth (Phase 3): JWT + ASP.NET Identity, `MustOwnProject` resource policy on all nested routes.
- Persistence (Phase 2): SQL Server, Guid PKs, `CreatedAt/UpdatedAt/RowVersion/CreatedBy`,
  JSON columns only for genuinely flexible payloads (`ParametersJson`, snapshots, values).
