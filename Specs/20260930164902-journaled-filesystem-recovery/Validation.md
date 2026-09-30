# Validation: Journaled Filesystem Recovery

## Acceptance Criteria

- Planned rows exist before disk work; Busy folders reject concurrent operations.
- Recovery tables cover forward/back/stranger/both/neither cases for all operation types and converge only to committed, aborted, or review.
- Trash purge touches only recorded verified objects; reconciliation retains permissions/owners byte-identically.
- Admin review actions are audited and never expose paths.
- Cross-volume crashes during copy/verify preserve source and safely remove verified staging only; crashes after publish/commit finish source trashing.

## Automated Tests

- `FsOperationJournalTests`, operation-specific `FsRecoveryServiceTests`, `CrashRecoveryTests`, `FolderReconciliationServiceTests`, and `FsIdentityTests`.
- `CrossVolumeMoveTests` and `CrossVolumeRecoveryTests`, including full destination and injectable volume probe.
- Review endpoints, path-leak, and P5-disable guard tests; run `make test`.

## Manual Verification

1. Kill the app during a large same-volume move and inspect safe recovery after restart.
2. Move across two filesystems and kill during copy, verify, and publish; confirm no source loss.
3. Review an external NAS rename and a Private subtree in the Admin queue.

## Definition of Done

- All recovery and crash tests pass; G3 same/cross-volume release evidence is recorded.
- No unresolved journal row is bypassed or silently discarded.
