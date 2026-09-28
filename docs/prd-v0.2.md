# PRD v0.2 Implementation Notes

Incremental update on top of the Phase 1–14 backend. Architecture, routes, and
all previously working behavior are preserved; only the deltas below were applied.

## Status legend

- **Approved Requirement** — implemented as specified.
- **MVP Assumption** — configurable placeholder, marked as temporary.
- **Pending Validation** — awaiting engineering sign-off.
- **Future Feature** — explicitly out of scope.

## Changes

### Scenario parameters — Approved Requirement (configurable)
- `ScenarioParameters` config section: `MaxParameters: 3` plus per-parameter
  `{Key, Name, Unit, Min, Max, Allowed}` (`appsettings.json`).
- Approved MVP keys (camelCase preserved from v0.1 stored data):
  `vegetationCoverPct` (% 0–100), `albedo` (fraction 0–1), `shadingPct` (% 0–100).
- PRD snake_case names (`building_orientation`, `green_area`, `shading`) are
  documented aliases only — **Pending Validation** which canonical keys the
  finalized schema uses.
- Enforcement with coded errors (no silent ignores, merged sets validated):
  `UNSUPPORTED_SCENARIO_PARAMETER`, `INVALID_SCENARIO_PARAMETER`,
  `TOO_MANY_SCENARIO_PARAMETERS` → 400. Baseline locking, inheritance, versioning,
  deterministic recalculation: unchanged.

### Recommendation evidence — Approved Requirement
- New `RecommendationRules` table (`Code` unique; Title, Description,
  ScientificReferencesJson, ImpactBasis, EngineName/Version, IsActive), seeded with
  `HEAT-VEG-001` and `HEAT-PREVENT-001` (references are **MVP Assumption**
  placeholders pending real sources).
- Engine cites `RuleCode`; service resolves it and stores `RecommendationRuleId`.
  Unknown/inactive code → generation blocked with `NO_EVIDENCE` → 422, nothing stored.
- Responses expose `ruleCode` + `scientificReferences`. Engine stays rule-based
  (no AI generation); `EvidenceLevel` values and never-`Validated` rule preserved.

### Cost quantity — Approved Requirement (traceable)
- `Quantity` optional on create; when omitted with a `RecommendationId` and unit
  `m2`, it is derived from the recommendation's analyzed polygon area and reported
  as `DerivedFromGeometry`. Otherwise quantity is required (400) — never invented.
- `QuantitySource` (`UserProvided`/`DerivedFromGeometry`) persisted and returned.
- Formula, catalog behavior, and `Unavailable` semantics unchanged.

### Error codes — Approved Requirement
New codes ride the existing RFC7807 mechanism: the three scenario codes → 400,
`NO_EVIDENCE` → 422. `FORBIDDEN` kept (not renamed to `FORBIDDEN_PROJECT`);
`COST_UNAVAILABLE` already covered by `CostStatus.Unavailable`.

### AI Chatbot — Future Feature
Verified absent repo-wide. Recommendation engine remains rule/evidence-based with
no AI dependencies. No code added.

### Unchanged modules
Environmental analysis (`IEnvironmentalAnalysisEngine`, HeatV01), comparison
(already key-agnostic), baseline immutability, reporting (JSON/HTML), auth.

## Migration
`PrdV02_EvidenceAndQuantity`: `RecommendationRules` (+ seed), FK
`Recommendations.RecommendationRuleId` (Restrict), `CostEstimates.QuantitySource`
(default `UserProvided`). No old migration touched.
