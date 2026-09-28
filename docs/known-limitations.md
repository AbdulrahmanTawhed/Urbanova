# Known Limitations (Phase 14)

## By design (tracked in `assumptions.md`, awaiting validation)

- GeoJSON-only file support; single heat proxy metric in °C; temporary 30/35 bands.
- Closed 3-parameter scenario set; no recommendation confidence model.
- Placeholder price catalog (not market data); no regional/dynamic pricing.
- JSON + HTML reports only (no PDF); owner-only auth (no roles).
- Planar CRS-unit areas (deg²), not square meters; all values estimated, none validated.

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
