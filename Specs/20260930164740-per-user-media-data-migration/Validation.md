# Validation: Per-User Media Data Migration

## Acceptance Criteria

- Real SQLite tests prove partial uniqueness, approved media transitions, missing/review retention, and moved-file relinking.
- Notes preserve delimiters and multiline content; all other user-data types are isolated by user and enforce ownership plus folder Read.
- The reconcile scan relinks moved files, retains data for vanished files, and sends ambiguous duplicates to review without guessing.
- The legacy JSON/text services are gone from DI and no code writes their files; localStorage behavior remains unchanged.

## Automated Tests

- `MediaIdentityClassifierTests`, `MediaItemRepositoryTests`, and `MediaReconciliationServiceTests`.
- `Sqlite*ServiceTests` for notes, highlights, progress, favorites, and views (per-user isolation) and for reader themes (shared library, creator-only edit/delete, duplicate names allowed, personal active settings, creator deletion).
- Endpoint authorization regression tests (anonymous 401, unreadable 404, cross-user isolation, Admin-only reconcile) on the real-Identity host.
- Run `make test`.

## Manual Verification

1. Move and rename media outside the app, run the reconcile scan, and confirm user data follows the files.
2. Use two accounts on one EPUB and CBZ, including a note containing `==========`; confirm isolation. Create a reader theme as one account and confirm the other can apply it, cannot edit or delete it, and keeps their own active settings.
3. Rename and replace media externally, scan, and verify confirmed vs review behavior.

## Definition of Done

- All requirements and tests pass; sources and paths remain private.
- P4/P5 behavior is not introduced by this spec.
