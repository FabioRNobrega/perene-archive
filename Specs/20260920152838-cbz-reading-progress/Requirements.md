# Requirements: CBZ Reading Progress

## Problem Statement

`ComicViewer.razor` always opens page one. Readers need their last viewed CBZ page restored automatically, using the same server-owned JSON persistence pattern already used by EPUB progress.

## User Stories

- Given a comic I previously read, when I reopen it, then it opens on my last viewed valid page.
- Given I move to another comic page, when navigation completes, then my position is saved automatically.
- Given a comic changed, has fewer pages, or its progress data is missing/corrupt, when I open it, then it safely opens page one.

## Functional Requirements

1. FR1 — The server must load/save a browser-safe CBZ page index through opaque category-scoped comic IDs only.
2. FR2 — Progress must be privately keyed by category, opaque item ID, file size, and last-write timestamp so changed comics do not reuse stale positions.
3. FR3 — `ComicViewer.razor` must restore a valid saved page after metadata loads; absent, invalid, or out-of-range progress starts at page one.
4. FR4 — Every successful previous/next, keyboard, tap-zone, or swipe page change must save the current page automatically on a best-effort basis.
5. FR5 — JSON reads/writes must be serialized and writes must use temp-file-then-atomic-move; malformed data must be treated as no saved progress.
6. FR6 — Progress endpoints and JSON must never expose archive paths or CBZ ZIP entry names.

## Non-Functional Requirements

- Reuse the archive-owned `Books/Notes` JSON persistence convention; no database, browser storage, package, or archive extraction.
- Keep progress storage server-owned and testable with temporary archive roots.
- UI failures to save progress must not interrupt reading.

## Out of Scope

- Bookmarks, named positions, reading history, sync across installations, user accounts, and progress reset UI.

## Open Questions

- ⚠️ TODO: Decide whether a future release needs a visible reset-reading-position action.
