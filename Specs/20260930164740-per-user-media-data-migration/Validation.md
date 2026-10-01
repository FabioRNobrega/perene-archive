# Validation: Per-User Media Data Migration

## Acceptance Criteria

- Real SQLite tests prove partial uniqueness, approved media transitions, missing/review retention, and moved-file relinking.
- Notes preserve delimiters and multiline content; all other user-data types are isolated by user and enforce ownership plus folder Read.
- Import is idempotent, reports results, and never modifies legacy source files.
- Legacy JSON/text services leave DI only after a verified importer run and backup; localStorage behavior remains unchanged.

## Automated Tests

- `MediaIdentityClassifierTests`, `MediaItemRepositoryTests`, and `MediaReconciliationServiceTests`.
- `Sqlite*ServiceTests` for notes, highlights, progress, themes, favorites, and views.
- `LegacyDataImporterTests` (including the Admin-only route, backup gate, and audit rows) and endpoint authorization regression tests (anonymous 401, unreadable 404, cross-user isolation) on the real-Identity host.
- Run `make test`.

## Manual Verification

1. Import representative legacy data after a verified backup and inspect report totals.
2. Use two accounts on one EPUB and CBZ, including a note containing `==========`; confirm isolation.
3. Rename and replace media externally, scan, and verify confirmed vs review behavior.

## Definition of Done

- All requirements and tests pass; sources and paths remain private.
- P4/P5 behavior is not introduced by this spec.
