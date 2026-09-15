# Validation: Upload Progress Preview UI

## Table of Contents

- [Acceptance Criteria](#acceptance-criteria)
- [Test Cases](#test-cases)
- [Manual Verification](#manual-verification)
- [Definition of Done](#definition-of-done)
- [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | With one or more local upload states, the Archive Browser shows one distinct progress-preview section containing the upload cards. |
| FR2 | With enough cards to exceed 30% of viewport height, cards scroll vertically inside the preview region while archive content remains reachable and there is no horizontal page overflow. |
| FR3 | The preview header visibly reads “Uploaded X of Y”, where X equals the number of `Done` states and Y equals `_uploads.Count`. |
| FR4 | A mixed batch displays textual counts for all represented non-success statuses; zero-count status labels may be absent. |
| FR5 | When one or more items are `Done`, Dismiss completed is available and removes every `Done` card; it is unavailable when no `Done` item exists. |
| FR6 | After either individual or bulk dismissal, no removed upload state remains in `_uploadFileRefs`. |
| FR7 | Existing individual-card progress, status, rate/ETA, error, Resume, Cancel, and Dismiss behavior continues to work. |
| FR8 | At desktop and phone widths, the header wraps without overlap, the action remains operable with a 40px-or-larger target, and the cards region remains vertically scrollable. |
| FR9 | The status summary communicates counts as text, the preview has an accessible label, and no new batch-level live announcement causes repeated upload-progress announcements. |

## Test Cases

**Unit tests (xUnit):**

- `WebApp.Tests/Client/ArchiveUploadStateTests.cs` — following the existing pure `ArchiveUploadItemState` test style, verify a focused aggregate helper reports total, successful-completion, and every non-success status count for a mixed collection.
- `WebApp.Tests/Client/ArchiveUploadStateTests.cs` — verify a completed-only dismissal operation removes all and only `Done` items from a mixed set, preserves pending/uploading/completing/interrupted/error entries, and cleans matching upload-file-reference keys.
- `WebApp.Tests/Client/ArchiveUploadStateTests.cs` — verify zero completed states suppress/disable the bulk-dismiss availability and zero non-success counts do not produce incorrect totals.

**Integration tests:**

- No server integration test is required: this feature changes only client-local render state and does not alter upload endpoints, DTOs, or persistence.
- Run the existing archive upload endpoint/service test suites through `make test` to ensure unchanged upload lifecycle behavior continues to pass.

## Manual Verification

1. Start the application with `make docker-run` and open a writable Archive Browser category such as Photos or Documents.
2. Select enough small supported files, or upload a folder with enough files, to make the upload-card region exceed 30% of the viewport. Confirm the preview cards scroll internally, the archive grid is still accessible below it, and no horizontal document overflow occurs.
3. Confirm the header reports “Uploaded 0 of N” at start and displays the active/pending status counts. During transfer, confirm uploading/completing counts change in step with the cards.
4. Let several uploads succeed. Confirm the successful count becomes “Uploaded X of N” and Dismiss completed appears.
5. Create or restore an interrupted/error upload if practical. Select Dismiss completed and verify completed cards disappear while interrupted/error cards remain visible with Resume/Cancel actions; verify successful files remain in the archive listing.
6. Complete a new upload and use its individual Dismiss control. Confirm it disappears and later selecting/uploads do not surface a stale card or file-reference error.
7. Repeat at a narrow phone-sized viewport. Confirm header text/actions wrap cleanly, controls are touch-operable, and the progress preview remains scrollable.
8. Use keyboard navigation and a screen reader or browser accessibility tree to verify the section label, textual summary, labeled bulk button, and existing per-card progress semantics.
9. Run `make test` and confirm the isolated Docker Compose test stack passes.

## Definition of Done

- The three documents in this spec are implemented.
- `ArchiveBrowser.razor` renders a `30vh` bounded upload-preview panel with textual aggregate status and completed-only bulk dismissal.
- `ArchiveBrowser.razor.css` contains only the nonstandard scroll geometry needed for the panel; ordinary layout uses Bootstrap utilities and existing design tokens.
- Completed-item cleanup removes corresponding `_uploadFileRefs` entries in both individual and bulk paths.
- Client-state tests cover aggregate counts and completed-only cleanup; all tests pass through `make test`.
- Responsive, keyboard, accessible-label, and live-region behavior are manually verified.
- `README.md`'s Current Supported Features table describes the new user-visible upload-preview behavior.
- No API, server service, upload-session, path-exposure, dependency, or infrastructure change is introduced.

## Rollback Plan

- Revert the upload-preview markup/helpers in `WebApp/WebApp.Client/Components/ArchiveBrowser.razor`, its local scroll-geometry rule in `ArchiveBrowser.razor.css`, the related client-state tests/helpers, and the README feature-table wording as one change set.
- The server upload protocol, persisted sessions, and already completed archive files are unaffected, because this feature has no server-side migration or persistence changes.
