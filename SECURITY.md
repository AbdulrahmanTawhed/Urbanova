# Security Policy

## Supported Versions

Only the latest commit on `main` is supported. This is an MVP codebase under active
development toward engineering validation (see `docs/assumptions.md`).

## Reporting a Vulnerability

Open a **private security advisory** on GitHub
(`Security` → `Advisories` → `New draft advisory`) rather than a public issue.
Include steps to reproduce, affected endpoints, and any relevant logs with secrets
redacted. Expect an initial response within 7 days.

## Known MVP Caveats (not vulnerabilities, but read before deploying)

- `src/Urbanova.Api/AppData/pricing/mvp-prices.json` contains **placeholder prices**,
  not market data. Missing codes yield `Unavailable`, never invented prices.
- Heat thresholds, classification bands, and scenario parameters are temporary MVP
  defaults pending engineering validation — see `docs/assumptions.md`.
- No rate limiting, no upload antivirus scanning, console-only logging.
- JWT signing keys must be provided via user-secrets or environment variables
  (`Jwt__Key`, 256-bit+). Never commit secrets; the committed config ships an empty key.
