# Post-Phase-14 Review Fixes

## Round 2 (full-codebase review, 10 findings)

1. Project delete wrapped in execution-strategy transaction (was 8 bare deletes).
2. Duplicate-upload race now catches `DbUpdateException` → reports the winning row as
   409 `FILE_DUPLICATE` instead of 500.
3. Re-linking an estimated recommendation is rejected (`INVALID_COST`) instead of
   orphaning the prior estimate.
4. Quantity/UnitPrice capped at 1e12 in validation (decimal overflow → 400, not 500).
5. Rotated-token reuse revokes the whole refresh-token family (cycle-guarded walk).
6. `RecommendationResponse.AnalysisRunId` nullable (was `Guid.Empty` after run deletes).
7. Stored `SizeBytes` measured from disk, with post-save empty/oversize guards.
8. Path prefix checks separator-suffixed in both file stores.
9. Zero cooling-rate config fails loudly instead of dividing by zero.
10. `SystemController` phase string updated to `phase-14-complete`.

Caught live during this round: raw transactions are rejected under
`EnableRetryOnFailure`, so both new transactions run inside
`CreateExecutionStrategy()` (broke registration until diagnosed).

## Round 1

An automated code review of the full `src/` tree raised 13 findings; all are
addressed below. Verification: `dotnet test` → Unit 114/114, Integration 86/86
(200 total, 0 failed); `ConcurrencyGuards` migration applied to LocalDB.

## Correctness

1. File delete order — `FileService.DeleteAsync` removed content before the row.
   Now deletes the row first, then the file best-effort.
2. Stack-trace loss — `FilesController.Map` used `throw ex`. Now rethrows via
   `ExceptionDispatchInfo` (switch expression cannot carry a bare `throw;`).
3. `CanProcess` null guard — null/empty names and content types return false.
4. `ComparisonEngine.Rank` — unknown bands throw `ArgumentException` (surfaces as
   INVALID_COMPARISON) instead of silently ranking Moderate.
5. Status casing — validator accepts any casing; service parses case-insensitively.

## Races

6. Recommendation refresh wrapped in a transaction (delete + insert + save).
7. Analysis idempotency backed by a filtered unique index
   `(ProjectId, InputHash)` where Succeeded, with loser-replays-winner retry.
8. Report versions backed by a unique `(ProjectId, Version)` index with Max+1 retry.
9. Both transactions run inside `CreateExecutionStrategy()` — raw
   `BeginTransactionAsync` is rejected under `EnableRetryOnFailure` (this broke
   registration globally until diagnosed via a live 400 probe; existing AuthTests
   now guard the regression).

## Logic / hardening

10. Mixed-currency comparisons return Unavailable with reason instead of mislabeled sums.
11. Alternative creation merges linked-run params (parent → run → request wins).
12. Registration is one transaction (Identity + profile + tokens; no stranded accounts).
13. `HtmlReportGenerator.Table` encodes cells itself (call sites pass raw values);
    `LocalReportFileStore` allowlists extensions (`html`, `json`).

## Regression tests added

`CanProcess` null matrix, unknown-classification throw, case-insensitive statuses,
table no-double-encoding, mixed-currency comparison, parent+run param merge.

> Current state (post-PR #7; history above retained): recommendation persistence is
> insert-only per immutable run — stable identities with preserved cost links now supersede
> the delete+insert refresh described in Round 1 item 6; `NO_EVIDENCE` (422) guards the
> evidence registry and mixed-currency totals are refused in both comparison and reports.
> Current suite: 265/265 (145 unit + 120 integration, 0 failed, 0 skipped).
