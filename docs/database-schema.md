# Database Schema (Phase 2 — `InitialCreate` on SQL Server)

Migrations: `InitialCreate` (Phase 2) + `AuthRefreshTokens` (Phase 3) +
`RecommendationPolygonIndex` (Phase 10) + `ConcurrencyGuards` (review fixes:
filtered unique `(ProjectId, InputHash)` where Succeeded; unique `(ProjectId, Version)`
on Reports)
(applied to LocalDB `Urbanova_Dev`; tests use `Urbanova_Test`).

## Tables

| Table | PK | FKs | Notes |
|---|---|---|---|
| `AspNetUsers` (+ Roles/Claims/Logins/Tokens/UserRoles) | `Id uniqueidentifier` | — | `UrbanovaIdentityUser : IdentityUser<Guid>` + `DisplayName`, `CreatedAt` |
| `Users` | `Id` (= Identity id) | — | Domain profile mirror; `Email` unique |
| `Projects` | `Id` | — | `OwnerId` idx, `(OwnerId, Name)` idx, `RowVersion`, `Status int` |
| `Sites` | `Id` | `ProjectId unique 1:1 CASCADE` | `BoundaryGeoJson nvarchar(max)`, `Crs` default `EPSG:4326` |
| `EngineeringFiles` | `Id` | `ProjectId RESTRICT` | `(ProjectId, HashSha256) unique` (SHA-256 hex `nchar(64)`), `ValidationStatus int`, JSON cols |
| `AnalysisRuns` | `Id` | `ProjectId RESTRICT`, `EngineeringFileId→SET NULL`, `ScenarioId→SET NULL` | `(ProjectId, InputHash)` idx, `InputHash nchar(64)`, `RowVersion` |
| `AnalysisResults` | `Id` | `AnalysisRunId unique 1:1 CASCADE` | `ValuesJson` + `ClassificationSummaryJson nvarchar(max)`, `IsEstimated` |
| `Scenarios` | `Id` | `ProjectId RESTRICT`, `ParentScenarioId self RESTRICT`, `BaseAnalysisRunId→SET NULL` | `(ProjectId, Kind)` idx, `ParametersJson nvarchar(max)`, `RowVersion` |
| `Recommendations` | `Id` | `ProjectId RESTRICT`, `AnalysisRunId/ScenarioId/CostEstimateId→SET NULL` | `EvidenceLevel int`, `Confidence nullable` |
| `CostEstimates` | `Id` | `ProjectId RESTRICT`, `RecommendationId/ScenarioId→SET NULL` | `Quantity/UnitPrice/Total decimal(18,4)`, `Currency default USD` |
| `Reports` | `Id` | `ProjectId RESTRICT` | `Format int`, `ContentJson nvarchar(max)`, `PayloadHash nchar(64)` |
| `RefreshTokens` | `Id` | — | `UserId` idx, `TokenHash nchar(64) unique` (hash only, never plaintext), `ExpiresAt`, `RevokedAt`, `ReplacedByTokenHash` |

All entities: `CreatedAt/UpdatedAt` (auto-stamped in `SaveChangesAsync`), `CreatedBy Guid?`.
Enums stored as `int`. Flexible payloads (`*Json`) are `nvarchar(max)` — everything
queryable (ownership, hashes, kinds, statuses) stays relational.

## Delete policy

Only `Project→Site` and `AnalysisRun→Result` cascade. Every other Project-child FK is
`RESTRICT`: deletes must proceed explicitly in dependency order, so a failed operation
can never silently destroy project data (PRD §19). Verified by
`PersistenceTests.CanPersistFullAnalysisGraph`.
