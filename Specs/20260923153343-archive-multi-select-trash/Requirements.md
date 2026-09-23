# Requirements: Archive Multi-Select Move to Trash

## Table of Contents

- [Requirements: Archive Multi-Select Move to Trash](#requirements-archive-multi-select-move-to-trash)
  - [Problem Statement](#problem-statement)
  - [User Stories](#user-stories)
  - [Functional Requirements](#functional-requirements)
  - [Non-Functional Requirements](#non-functional-requirements)
  - [Out of Scope](#out-of-scope)
  - [Open Questions](#open-questions)

## Problem Statement

`Specs/20260922132005-archive-multi-select-move/` added Ctrl+Click/Shift+Click multi-selection to `ArchiveBrowser.razor` (`_selectedForMoveIds`) and a "Selected items" toolbar (`WebApp/WebApp.Client/Components/ArchiveBrowser.razor`, `role="toolbar"`) that offers only **Move** and **Clear selection**. Sending items to Trash is still one at a time: each card's actions menu calls `DeleteAsync` → `DELETE /api/archive/{category}/items/{id}` → `ArchiveService.MoveToTrash`, which moves a single item into the Trash category. Clearing out many files or folders therefore means repeating open-actions → Delete per item. The user wants a "Move to Trash" button in the same selection toolbar so a whole selection is trashed in one operation.

## User Stories

- Given several items are multi-selected in a non-Trash category, when the user clicks "Move to Trash" in the selection toolbar and confirms, then all selected files and folders are moved into Trash as one background job with a single progress popup.
- Given items are selected, when the confirmation dialog appears and the user clicks Cancel, then nothing is moved and the selection is kept.
- Given the user is viewing the Trash category with items selected, when the toolbar is shown, then no "Move to Trash" button is present.
- Given a selected item can no longer be resolved (e.g. it was deleted externally), when the user confirms, then the request is rejected, nothing is moved, and an error is shown.
- Given a batch trash job stops partway through, when the user reads the progress popup, then it states how many items were moved and that no rollback occurred.

## Functional Requirements

1. FR1 — The "Selected items" toolbar in `ArchiveBrowser.razor` shows a "Move to Trash" button (`btn btn-outline-danger btn-sm`, `bi-trash3` icon, placed after Move and before Clear selection) whenever one or more items are selected and `Category` is not `trash`.
2. FR2 — The button is not rendered when `Category == "trash"`.
3. FR3 — The button is disabled while a mutation job is active (`_activeJob is not null`), as Move is today.
4. FR4 — Clicking the button opens a Bootstrap confirmation modal (same pattern as the Empty Trash modal) titled "Move to Trash" stating the selected count; Cancel closes it without changes and keeps the selection.
5. FR5 — Confirming sends one request containing the selected opaque item IDs, via a new endpoint `POST /api/archive/{category}/items/trash` with a `BatchMoveToTrashArchiveItemsRequest(IReadOnlyList<string> ItemIds)` body.
6. FR6 — `IArchiveService.BatchMoveToTrash(categoryKey, itemIds)` rejects an empty list with `ArchiveValidationException`, rejects the `trash` category with `ArchiveForbiddenException`, and resolves every ID (rejecting unknown IDs and category roots) before any file is touched, so an invalid ID fails the entire request.
7. FR7 — Each item's destination is a contained path directly under the Trash root, using the existing `GetUniqueTrashPath` collision naming, and every destination in the batch is unique.
8. FR8 — The batch runs as a single `ArchiveMutationJob` of a new kind `ArchiveMutationKind.BatchMoveToTrash`, carrying `BatchEntries`, with `TotalItems` equal to the sum of file counts (a folder counts its files, a file counts 1) and `Label` of the form "N item(s)".
9. FR9 — `ArchiveMutationBackgroundWorker` dispatches `BatchMoveToTrash` to the existing file-by-file batch executor path so `ProcessedItems`/`TotalItems` progress is reported live.
10. FR10 — On a filesystem failure mid-batch, the job fails with a diagnostic naming processed vs total items and stating that no rollback was performed; already-trashed items stay in Trash.
11. FR11 — The progress popup shows "Moving to Trash:" with the batch label for `BatchMoveToTrash` jobs (`JobActionLabel`).
12. FR12 — After the request is accepted, the selection is cleared and the confirmation modal closes; on completion the listing refreshes through the existing job-polling flow.
13. FR13 — Server errors (validation/forbidden/not found/conflict) surface through the existing `EnqueueMutationAsync` error handling and leave the selection intact.
14. FR14 — The single-item Delete action and the existing Move/BatchMove behavior are unchanged.
15. FR15 — `README.md`'s `Archive management` row in `## Current Supported Features` is updated to mention multi-select send-to-Trash.

## Non-Functional Requirements

- No physical or root-relative path is exposed to the browser or written to logs; only category-scoped opaque item IDs are sent.
- Path handling stays in `ArchiveService`/`ArchiveMutationExecutor` (server-only), following the existing `BatchMove` split of validation (service) and filesystem work (executor).
- UI uses Bootstrap components/utilities and Bootstrap Icons only (per the design-system contract); no new custom CSS or JavaScript. The button has an accessible name, visible focus, and meets the existing toolbar button size conventions.
- No new packages, services, or infrastructure; no new FFmpeg use.
- Validated with xUnit tests run via `make test`.

## Out of Scope

- Permanent deletion or restoring items from Trash (Trash's Empty Trash flow is unchanged).
- Multi-select inside the Trash category.
- Selecting items across folders/pages, or a select-all shortcut.
- Keyboard shortcuts (e.g. the Delete key) for trashing.
- Per-item partial success reporting or rollback.
- Undo toast.

## Open Questions

- None. Resolved: the confirmation modal shows only the selected count, not item names.
