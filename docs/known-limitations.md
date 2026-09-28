# Known Limitations (Phase 14 + PRD v0.2)

## By design (tracked in `assumptions.md`, awaiting validation)

- GeoJSON-only file support; single heat proxy metric in °C; temporary 30/35 bands.
- Configurable 3-parameter scenario catalog (keys, bands, cap all temporary);
  canonical key names still open; no recommendation confidence model.
- Placeholder price catalog (not market data); no regional/dynamic pricing.
- No currency conversion: project reports refuse a combined total across multiple
  currencies (Unavailable + reason, null total/currency); scenario comparison
  retains its existing mixed-currency refusal.
- JSON + HTML reports only (no PDF); owner-only auth (no roles).
- Planar CRS-unit areas (deg²), not square meters; all values estimated, none validated.
- Evidence-rule references are MVP placeholders; quantity derivation covers m²
  area-based interventions only.
- Recommendation feasibility is heuristic (intervention magnitude only — not cost,
  constructability, or validation); comparison feasibility is not calculated.
  Cost, price availability, or a lower total never imply higher feasibility;
  the final assessment remains the engineer's responsibility.
- Derived quantities use spherical-earth areas (~0.3% off the WGS84 ellipsoid);
  non-EPSG:4326 geometries and sub-centimeter rounded areas are rejected, not stored.

## Engineering gaps (future work, not PRD blockers)

- Synchronous file validation/analysis (no background jobs, no progress polling).
- No rate limiting, no file antivirus scan, Serilog console sink only.
- No refresh-token reuse detection beyond rotation (replay returns 401; no theft alarm).
- RefreshTokens and stored files are never garbage-collected (no retention job).
- Integration tests require LocalDB (`MSSQLLocalDB`) + ~40 s runtime; no Testcontainers profile yet.
- Single analysis engine registered (no multi-engine dispatch); single-level scenario inheritance.
- Comparison requires identical polygon counts; reports always use the latest run.
- `PUT /api/projects/{id}` replaces site fields wholesale when `site` is provided.
- Transitive NU1903 advisory (`System.Security.Cryptography.Xml 9.0.0` via EF Design) — build-only tooling, not shipped.
