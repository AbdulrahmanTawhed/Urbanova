# Phase 2 — Domain Entities + Database ✅

## Built

- Domain: `Common/EntityBase` (Guid PK, audit, `RowVersion`), `Enums.cs` (7 enums),
  10 entities (`User`, `Project`, `Site`, `EngineeringFile`, `AnalysisRun`,
  `AnalysisResult`, `Scenario`, `Recommendation`, `CostEstimate`, `Report`),
  `ValueObjects` (`Money`, `HeatValue`), `Interfaces/IUnitOfWork`,
  `BusinessRules` (`ProjectRules`, `ScenarioRules`, `CostRules`).
- Domain stays Identity/EF-free: ownership is `OwnerId Guid`; credentials live in
  `Infrastructure.UrbanovaIdentityUser`. `ArchitectureGuardTests` updated to anchor
  on `Project` (Domain `Placeholder` removed).
- Infrastructure: `AppDbContext` (+ auto audit stamps), per-entity configurations,
  `DependencyInjection.AddUrbanovaPersistence` (returns CS or null when unconfigured),
  `AppDbContextFactory` (design-time), `Migrations/InitialCreate`.
- Api: conditional persistence wiring + SQL Server readiness probe (`urbanova-db`).
- Design fix: multiple-cascade-paths rejected by SQL Server → only `Site` cascades
  from `Project`; all other children `RESTRICT` (explicit delete order, PRD §19).

## Verified

```text
dotnet ef database update  → Applied InitialCreate to LocalDB Urbanova_Dev
dotnet test Urbanova.slnx  → Unit 14/14, Integration 7/7 (21 total, 0 failed)
```

New tests: `BusinessRulesTests` (12: names, cost math, baseline lock/inherit),
`PersistenceTests` (3: project+site, hash uniqueness, full graph + restrict-delete).

Integration tests require `MSSQLLocalDB` running and use `Urbanova_Test`.

## Next (Phase 3 — Authentication)

Identity + JWT Bearer (`register/login/refresh`), domain `User` sync, `MustOwnProject`
policy, `[Authorize]` by default, 401/403 tests. No roles yet (owner-only per assumptions).
