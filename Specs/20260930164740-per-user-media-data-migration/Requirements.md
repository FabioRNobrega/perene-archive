# Requirements: Per-User Media Data Migration

## Problem Statement

Existing note, highlight, progress, favorite, reader-theme, and storage-view services write shared JSON/text files keyed by unstable snapshot metadata. P3 moves them to P1's SQLite store, scopes them to the P2-authorized user and media identity. The application is not in production, so the old shared JSON/text files are simply dropped: there is no importer and no legacy-data handling.

## User Stories

- Given two users read the same EPUB or comic, when each saves annotations or progress, then each sees only their own data.
- Given a book is renamed, when the archive is scanned, then stable media identity preserves its user data; uncertain replacements are reviewed rather than silently attached.
- Given a book is moved or renamed outside the app, when an Admin runs the media reconciliation scan, then the book keeps its identity and a vanished file's data is retained (marked Missing).

## Functional Requirements

1. FR1 — Add `MEDIA_ITEM` records with stable ID, `FolderId` (a foreign key to P2's seeded root `Folder` row, which is never removed; the item's `RelativePath` is root-relative and its folder `FolderLocation` is the root key plus the path's directory, resolved server-side), server-only location, fingerprint, identity key, content revision, and status; enforce an Active-only unique `(FolderId, RelativePath)` index.
2. FR2 — Classify same-path changes as confirmed update, identity-changing replacement, or uncertain; only approved transition-table changes occur and all active-path lookups use one repository method.
3. FR3 — Reconcile scans by fingerprint, retaining user data for Missing/review candidates and never automatically deleting it.
4. FR4 — Replace delimiter-based EPUB notes with per-user SQLite rows that preserve arbitrary multiline content.
5. FR5 — Replace highlights, reading/comic progress, favorites, and custom storage views with per-user SQLite rows, correct unique indexes, nullable global rows where required, and content revisions for reading state. **Reader themes are the exception: they are shared.** Every signed-in user sees every saved theme and can apply it; only the creator can edit or delete it (no Admin override); theme names need not be unique. The user's currently active reader settings and selected theme stay personal, so applying a shared theme never changes another user's reader. A theme outlives its creator (the creator link is cleared when the account is deleted, after which nobody can edit it), and if a theme is deleted while another user has it selected, that user's selection simply falls back to none.
6. FR6 — For per-user data (everything except shared reader themes), require both row ownership and folder Read permission (via P2's `IFolderAccessService.CanReadAsync`) for all per-user data reads/writes. The user identity comes from the authenticated server principal, never from a request field. Anonymous is 401 and an unreadable item is 404, matching P2. Admins do not gain a UI to read other users' annotations. Losing Read hides but never deletes a user's rows; restoring Read restores access.
7. FR7 — Provide an Admin-only reconciliation scan (`POST /api/admin/media/reconcile`, Admin group plus antiforgery) that relinks moved/renamed files by fingerprint, sends ambiguous duplicates to review, marks vanished files Missing, and reclassifies reappearing ones, reporting counts only.
8. FR8 — Remove the legacy JSON/text services from DI and all write paths without migrating their files (none are in production); browser-local theme/sort preferences remain unchanged.

9. FR9 — Service lifetime and signatures: legacy services are singletons without a user parameter, while `AppDbContext` is scoped. The SQLite-backed services are therefore registered scoped and resolve the current user from the server principal. Method signatures that take the unstable `sizeBytes`/`lastWriteTimeUtc` snapshot metadata (reading and comic progress) change to the opaque archive item ID, which the server maps to a `MEDIA_ITEM`; opaque browser IDs are otherwise unchanged. Affected endpoints and components are limited to those callers.

## Non-Functional Requirements

- No paths, fingerprints, or DB identifiers appear in browser responses or normal logs.
- Use real SQLite integration tests for constraints and transition ordering; retain existing service interfaces to minimize endpoint/component change.

## Out of Scope

- Folder ACL rules and permission editing (P2, implemented), durable jobs/naming counters (P4), and filesystem journaling/recovery (P5).

## Open Questions

- None; content fingerprinting and EPUB/CBZ identity rules remain those fixed in the umbrella spec.
