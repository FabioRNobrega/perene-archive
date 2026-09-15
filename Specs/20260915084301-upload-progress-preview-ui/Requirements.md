# Requirements: Upload Progress Preview UI

## Table of Contents

- [Problem Statement](#problem-statement)
- [User Stories](#user-stories)
- [Functional Requirements](#functional-requirements)
- [Non-Functional Requirements](#non-functional-requirements)
- [Out of Scope](#out-of-scope)
- [Open Questions](#open-questions)

## Problem Statement

`WebApp/WebApp.Client/Components/ArchiveBrowser.razor` currently renders each in-memory `ArchiveUploadItemState` as an unbounded Bootstrap list-group directly below the archive toolbar. A large batch pushes the archive contents far down the page, provides no aggregate understanding of batch state, and requires users to dismiss completed uploads one at a time. Users need a bounded, independently scrollable upload-progress preview with an accessible batch summary and one action to clear completed cards.

## User Stories

- Given I select a large group of files or a folder for upload, when upload cards are visible, then they remain in a fixed, scrollable preview area and do not grow indefinitely into the archive listing.
- Given uploads are pending, active, completed, interrupted, or failed, when I view the progress-preview header, then I can see the total number and a clear count for each status, including how many completed successfully.
- Given several uploads have completed successfully, when I choose to dismiss all completed uploads, then every completed card disappears while non-completed uploads and their recovery actions remain visible.
- Given no upload cards remain after dismissing completed items, when I view the archive browser, then the progress-preview area is absent and the normal archive content follows the toolbar.

## Functional Requirements

1. FR1 — When `_uploads.Count > 0`, `ArchiveBrowser.razor` shall render all upload cards inside one distinct upload-progress preview container instead of directly as an unbounded list below the archive toolbar.
2. FR2 — The container shall have a maximum height of `30vh` and vertically scroll its cards when their content exceeds that height; it shall not make the document or archive-content grid horizontally overflow.
3. FR3 — The container shall have a visible, persistently available header that reports the aggregate upload count and a completed-success count in the form “Uploaded {completed} of {total}”.
4. FR4 — The header shall also expose separate counts for every non-success upload state represented by `ArchiveUploadItemStatus`: pending, uploading, completing, interrupted, and error. A state with a zero count may be omitted to keep the summary concise.
5. FR5 — The header shall provide one “Dismiss completed” action only when at least one item is in `ArchiveUploadItemStatus.Done`; activating it shall remove all and only `Done` states from `_uploads`.
6. FR6 — Bulk dismissal shall also remove the associated entries from `_uploadFileRefs` so client-side state does not retain completed browser file references after their cards disappear.
7. FR7 — Existing per-card status badges, acknowledged-byte progress, rate/ETA, error text, Resume, Cancel, and individual Dismiss behavior shall remain functionally unchanged.
8. FR8 — The progress container, aggregate information, and dismissal control shall remain usable on narrow screens: the header content may wrap, the scroll region remains vertically scrollable, and controls retain the project’s minimum 40×40 CSS-pixel interactive target requirement.
9. FR9 — The aggregate status must be conveyed in text and not color alone; the container retains an accessible label, while live-region behavior remains scoped to the individual upload-card updates to avoid noisy whole-batch announcements.

## Non-Functional Requirements

- Follow the existing Blazor WebAssembly component-state pattern: aggregate values are derived from the local `_uploads` collection in `ArchiveBrowser.razor`; no endpoint, persistence format, server service, or JavaScript interop changes are introduced.
- Use Bootstrap 5.3.8 components and responsive utilities for the header, badges, button, spacing, and wrapping. Add only narrowly scoped CSS in `ArchiveBrowser.razor.css` for the nonstandard `30vh` scroll-region geometry, consistent with `AGENTS.md`.
- Preserve the existing opaque-ID and archive-path boundary. This presentation-only feature must not expose a physical or root-relative path or change upload-session behavior.
- Keep aggregate-count calculation small and deterministic so it is straightforward to unit test without browser or network dependencies.

## Out of Scope

- Cancelling, retrying, or dismissing active, pending, completing, interrupted, or failed uploads in bulk.
- Persisting the visible upload-card list or a user’s dismissal choice across navigation, refresh, or browser restart.
- Changing resumable upload chunking, server acknowledgement, session recovery, API contracts, or upload file-selection workflows.
- Redesigning individual upload-card content, status naming, or progress/rate/ETA calculations.

## Open Questions

- None. The user confirmed a viewport-relative `30vh` cap, a multi-status header, and bulk dismissal limited to successfully completed uploads.
