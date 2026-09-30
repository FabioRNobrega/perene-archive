# Requirements: Per-User Media Data Migration

## Problem Statement

Existing note, highlight, progress, favorite, reader-theme, and storage-view services write shared JSON/text files keyed by unstable snapshot metadata. P3 moves them to P1's SQLite store, scopes them to the P2-authorized user and media identity, and preserves legacy data through a verified one-time import.

## User Stories

- Given two users read the same EPUB or comic, when each saves annotations or progress, then each sees only their own data.
- Given a book is renamed, when the archive is scanned, then stable media identity preserves its user data; uncertain replacements are reviewed rather than silently attached.
- Given legacy files exist, when the first Admin imports them, then original files remain intact and re-running imports no duplicates.

## Functional Requirements

1. FR1 — Add `MEDIA_ITEM` records with stable ID, folder, server-only location, fingerprint, identity key, content revision, and status; enforce an Active-only unique `(FolderId, RelativePath)` index.
2. FR2 — Classify same-path changes as confirmed update, identity-changing replacement, or uncertain; only approved transition-table changes occur and all active-path lookups use one repository method.
3. FR3 — Reconcile scans by fingerprint, retaining user data for Missing/review candidates and never automatically deleting it.
4. FR4 — Replace delimiter-based EPUB notes with per-user SQLite rows that preserve arbitrary multiline content.
5. FR5 — Replace highlights, reading/comic progress, favorites, reader themes, and custom storage views with per-user SQLite rows, correct unique indexes, nullable Admin/global presets where required, and content revisions for reading state.
6. FR6 — Require both row ownership and folder Read permission for all per-user data reads/writes. Admins do not gain a UI to read other users' annotations.
7. FR7 — Add an idempotent importer that resolves legacy records against current media into the first Admin, reports imported/skipped totals, and leaves sources untouched.
8. FR8 — After a verified import and verified pre-migration backup, remove legacy JSON/text services from DI and all write paths; browser-local theme/sort preferences remain unchanged.

## Non-Functional Requirements

- No paths, fingerprints, or DB identifiers appear in browser responses or normal logs.
- Use real SQLite integration tests for constraints and transition ordering; retain existing service interfaces to minimize endpoint/component change.

## Out of Scope

- Folder ACLs (P2), durable jobs/naming counters (P4), and filesystem journaling/recovery (P5).

## Open Questions

- None; content fingerprinting and EPUB/CBZ identity rules remain those fixed in the umbrella spec.
