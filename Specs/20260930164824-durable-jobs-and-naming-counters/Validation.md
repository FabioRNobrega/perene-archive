# Validation: Durable Jobs and Naming Counters

## Acceptance Criteria

- Existing job endpoints and polling DTOs remain compatible but survive restart.
- Payload inspection proves stable identities only; identity/access changes fail jobs generically before execution.
- All four naming paths retain their formats and unique consecutive allocation under concurrency.

## Automated Tests

- `SqliteJobStoreTests`, `JobEnqueueServiceTests`, `JobExecutionRecheckTests`, and restart tests.
- `NamingCounterServiceTests`, extending cut/composition/image-crop/conversion naming tests with parallel allocation.
- Owner-filtered job lists (member sees own, Admin sees all, deleted-account job Admin-only) and a deleted-account job failing at execution.
- Startup interruption marks Pending/Processing as Failed and does not replay.
- Retry: Failed/Stopped only (409 otherwise), owner or Admin only (404 for others), payload without a selection or a missing/changed source is refused, a successful retry reuses the same job ID and row (no second row), resets it to Pending with the same selection, and a job that is no longer Failed/Stopped cannot be restarted, and the payload still contains no paths. The jobs list includes the starter's display name (and "removed" after account deletion).
- Activity list: includes composition, move/trash and cut jobs with titles and starter names, hides other members' jobs from members, shows everything to Admins, excludes conversions, is newest first, and leaks no paths. `CutBackgroundWorker` records Processing/Completed/Failed (access refused, generator failure, exception) through the recorder.
- `JobListRulesTests`: type labels/icons/badges, running-first then newest ordering, progress vs total time vs failure reason, total-time rule, filters and counts, paging with the 5/10/25/50 page sizes, clamping, fallback for unsupported sizes and the page window, and which actions apply.
- Path-leak and endpoint regression tests; run `make test`.

## Manual Verification

1. Queue a conversion, stop the container, restart, and confirm interrupted durable status.
2. Restart mid-conversion, open Jobs, confirm "Started by" shows your name and Retry on the interrupted job restarts that same job card with the same profile.
3. Open Jobs after saving a cut, creating a composition and moving a file: each appears with the right type, and the filter narrows the list.
4. With more than 10 jobs, confirm one fixed-height line per job, the list scrolls with the title and filter pinned, pagination works, running jobs stay on top, a Failed job shows its reason on the line, and opening a details panel survives the 2-second refresh.
5. Run parallel cuts of one source and verify collision-free consecutive names.

## Definition of Done

- All requirements and Docker test suite pass without exposing paths or snapshot IDs in persisted payloads.
- P5 journaled filesystem operations remain out of scope.
