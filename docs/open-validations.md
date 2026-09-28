# Open Validation Requirements (PRD §2 — unchanged by Phase 1)

1. Supported engineering file format
2. Heat analysis metric
3. Heat analysis units
4. Heat thresholds
5. Environmental classification rules
6. Scenario parameters (modifiable set — v0.2: configurable catalog with 3 approved keys + bands + max count; canonical key names still open)
7. Recommendation confidence model
8. Cost data source
9. Report format
10. User roles
11. Analysis validation method
12. Feasibility methodology (approved definition; whether technical feasibility,
    affordability, and implementation readiness are separate dimensions; inputs,
    threshold validation, aggregation across polygons/recommendations,
    missing-cost and mixed-currency behavior; engineering sign-off required
    before comparison-level feasibility is computed)

Each maps to an abstraction + configurable MVP default in `assumptions.md`.
Nothing in `src/` treats these defaults as final.
