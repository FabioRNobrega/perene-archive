# Validation: Durable Jobs and Naming Counters

## Acceptance Criteria

- Existing job endpoints and polling DTOs remain compatible but survive restart.
- Payload inspection proves stable identities only; identity/access changes fail jobs generically before execution.
- All four naming paths retain their formats and unique consecutive allocation under concurrency.

## Automated Tests

- `SqliteJobStoreTests`, `JobEnqueueServiceTests`, `JobExecutionRecheckTests`, and restart tests.
- `NamingCounterServiceTests`, extending cut/composition/image-crop/conversion naming tests with parallel allocation.
- Path-leak and endpoint regression tests; run `make test`.

## Manual Verification

1. Queue a conversion, stop the container, restart, and confirm interrupted durable status.
2. Run parallel cuts of one source and verify collision-free consecutive names.

## Definition of Done

- All requirements and Docker test suite pass without exposing paths or snapshot IDs in persisted payloads.
- P5 journaled filesystem operations remain out of scope.
