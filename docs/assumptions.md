# Temporary MVP Assumptions (must be replaced after validation)

Every item below is a **configurable placeholder**, never hard-coded in domain logic.
Each is bound via `IOptions<T>` and stamped `IsEstimated` / `EvidenceLevel.Assumption`
where it surfaces in analysis output.

| # | PRD unresolved | MVP default (`appsettings.json`) | Replace with |
|---|---|---|---|
| 1 | File format | `GeoJsonFileProcessor` + generic reject-all-else stub | Validated CAD/BIM parser |
| 2 | Heat metric | `LandSurfaceTempProxy` | Validated metric |
| 3 | Heat units | `Celsius` | Validated units |
| 4 | Heat thresholds | Acceptable &lt;30, Moderate 30–35, Problem &gt;35 | Engineered thresholds |
| 5 | Classification rules | 3-class rule in `Classification:` section | Validated rule set |
| 6 | Scenario params | `ScenarioParameters` catalog: `vegetationCoverPct` (% 0–100), `albedo` (0–1), `shadingPct` (% 0–100), `MaxParameters: 3`; PRD snake_case names (`building_orientation`, `green_area`, `shading`) are aliases only | Final param schema + canonical key names |
| 7 | Recommendation confidence | `null` unless `Validated/Calculated`; else warning | Defined confidence model |
| 8 | Cost source | `AppData/pricing/mvp-prices.json`; missing → `Unavailable` | Reliable price feed |
| 12 | Evidence references | `RecommendationRules` seed cites MVP placeholder references | Real engineering rules + scientific sources |
| 13 | Cost quantity | Omitted quantity derived from analyzed polygon area (m²) when backed by geometry | Validated quantity-takeoff rules |
| 9 | Report format | JSON canonical + simple HTML renderer; no PDF | Defined format |
| 10 | User roles | Owner-only; `Role` claim parsed but unenforced | Final RBAC |
| 11 | Analysis validation | Deterministic re-run (`InputHash` match); external = `PendingValidation` | Validation protocol |
