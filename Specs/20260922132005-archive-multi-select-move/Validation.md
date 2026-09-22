# Validation: Archive Multi-Select Move

## Table of Contents

- [Validation: Archive Multi-Select Move](#validation-archive-multi-select-move)
  - [Acceptance Criteria](#acceptance-criteria)
  - [Test Cases](#test-cases)
  - [Manual Verification](#manual-verification)
  - [Definition of Done](#definition-of-done)
  - [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | Ctrl+Click and Shift+Click on a card never trigger `ActivateAsync` (no folder navigation, no playback, no viewer opening); a plain click still does. |
| FR2 | Ctrl+Click on an unselected card adds its ID to `_selectedForMoveIds` and sets it as the anchor; Ctrl+Click on an already-selected card removes it. |
| FR3 | Shift+Click after an anchor exists selects every item between the anchor and the clicked item (inclusive) in the current `_filteredItems` order, replacing the prior selection; Shift+Click with no prior anchor selects just the clicked item and sets the anchor. |
| FR4 | A category-root folder never appears as a selectable card (it is the browsed folder, not a listed item), so it can never enter `_selectedForMoveIds`. |
| FR5 | A selected card shows a green (`border-success`) card border, visually distinct from and non-conflicting with the existing "currently playing" badge and its blue (`border-primary`) border. |
| FR6 | The selection toolbar appears exactly when `_selectedForMoveIds.Count > 0`, shows the correct live count, and its "Clear selection" button empties the selection and hides the toolbar; its "Move" button is disabled whenever `_activeJob is not null` (another move/delete job's progress popup is showing) and re-enables once that job reaches a terminal state and is dismissed/cleared. |
| FR7 | Opening "Move" from the toolbar shows `ArchiveMovePicker` with the title `"Move {N} items"` for N ≥ 2 selected items; single-item Move (from a card's own actions panel) still shows `"Move {item.Name}"` unchanged. |
| FR8 | `PATCH /api/archive/{category}/items/location` exists, accepts `{ ItemIds, DestinationCategory, DestinationFolderId }`, and is distinct from the existing `PATCH /api/archive/{category}/items/{id}/location`. |
| FR9 | A batch request containing one item that would conflict, self-move, or target a category root is rejected in full (HTTP 400/403/409 as appropriate) with zero items enqueued or moved; a fully valid batch enqueues successfully. |
| FR10 | A successfully enqueued batch job's `TotalItems` equals the sum of each selected item's own file count (folders counted recursively, files counted as 1). |
| FR11 | During a batch job's execution, polled `ProcessedItems` increases monotonically across the whole batch (not reset between items) up to `TotalItems` on success; on an injected mid-batch failure, the job's `Diagnostic` names how many of the total files were moved before the stop, and files already moved by earlier entries remain moved. |
| FR12 | `ArchiveMutationBackgroundWorker` correctly dispatches a `BatchMove`-kind job to `ArchiveMutationExecutor.BatchMoveAsync` (verified via the existing worker/executor test doubles). |
| FR13 | The existing `EnqueueMutationAsync`/`_activeJob`/polling flow drives the batch job's progress popup with no client code path unique to batch beyond enqueue-time request construction and the `BatchMove` label. |
| FR14 | Navigating to a new folder/category, or a listing reload after `LoadAsync`, clears `_selectedForMoveIds` and `_selectionAnchorId`; the toolbar disappears accordingly. |
| FR15 | No response body from the new endpoint or job-status payloads (`ArchiveMutationJobDto`, batch accept response) contains a physical or root-relative path — only opaque IDs, category keys, folder IDs, labels, and counts. |

## Test Cases

**Unit tests (`WebApp.Tests/Services`):**

- `ArchiveServiceTests.cs`: extend with cases for the new `BatchMove` method — happy path (2+ files across folders, correct `TotalItems`/`BatchEntries`), name-conflict rejection (no enqueue), self-move/descendant-folder rejection, category-root rejection, and confirm existing single-item `Move`/`Rename`/`MoveToTrash` tests still pass unmodified after the validation-helper extraction.
- `ArchiveMutationExecutorTests.cs`: extend with `BatchMoveAsync` cases — running total progress across multiple entries, stop-on-first-failure behavior with a diagnostic naming the partial count, and confirm existing `MoveAsync`/`MoveToTrashAsync`/`EmptyTrashAsync` tests are unaffected.
- `ArchiveMutationJobQueueTests.cs` / `ArchiveMutationJobStatusStoreTests.cs`: confirm a `BatchMove`-kind job flows through unchanged (these stores/queues are kind-agnostic already, so this is a regression check, not new logic).

**Endpoint tests (`WebApp.Tests/Endpoints`):**

- `ArchiveEndpointsTests.cs`: extend with cases for `PATCH /api/archive/{category}/items/location` — successful batch enqueue returns 202-style payload with a job id, invalid batch returns the correct error status with nothing enqueued, and the existing single-item `PATCH .../items/{id}/location` test cases still pass.

**Client tests (`WebApp.Tests/Client`):**

- ⚠️ TODO: `WebApp.Tests/Client` currently covers browser-safe state objects (e.g. `ArchiveUploadStateTests.cs`) rather than full Razor component interaction; if `ArchiveBrowser.razor`'s selection logic (range computation, anchor handling) is extracted into a small pure/testable helper (e.g. a static `ArchiveSelectionRange.Compute(...)` method), add a matching unit test file following the existing `*StateTests.cs` convention. If it stays inline in the component, this is a manual-verification-only concern (matching how `HandleCardKeyDown`/`ToggleActions` are validated today — no existing component-render test harness in this repo).

## Manual Verification

Starting from a clean state using `make docker-run` (per `AGENTS.md`'s documented Docker Compose workflow — no native `dotnet run`):

1. Open the Archive Browser, navigate into a category folder with at least 5 files/folders.
2. Ctrl+Click one file — confirm it shows selected (green card border + no navigation/playback occurred) and the selection toolbar appears with "1 selected".
3. Ctrl+Click two more distinct items — confirm all three are selected and the count updates to "3 selected"; Ctrl+Click one of them again — confirm it deselects and the count drops to "2 selected".
4. Ctrl+Click a fresh item to reset the anchor, then Shift+Click an item several positions later — confirm every item in between becomes selected and the count matches.
5. With 3+ items selected (mixing files and at least one folder), click "Move" in the toolbar — confirm the picker opens titled "Move 3 items" (adjust N), pick a different destination folder/category, and confirm.
6. Confirm the existing move-progress popup appears, shows increasing combined progress across all files in all selected items, and completes; confirm the listing refreshes and every selected item is now in the destination.
7. Repeat with a batch that includes a name conflict at the destination (pre-create a same-named file there) — confirm the whole batch is rejected up front with a clear error and nothing moved.
8. Navigate to a different folder after selecting items but before moving — confirm the selection and toolbar clear.
9. Confirm the pre-existing single-item "Move" action (per-card actions panel) still works unchanged for one item at a time.
10. While a move job's progress popup is still showing (start a large batch move to keep it visible), select another set of items — confirm the toolbar's "Move" button is disabled and cannot start a second job until the first one finishes and its popup is closed.

## Definition of Done

- Requirements, Plan, and Validation docs in this spec folder reflect the implemented behavior.
- `make test` passes, including the new/extended `ArchiveServiceTests`, `ArchiveMutationExecutorTests`, and `ArchiveEndpointsTests` cases.
- New UI (green selected-card border, toolbar, batch-aware picker title) is built entirely from Bootstrap components/utilities and Bootstrap Icons per `AGENTS.md`; no new scoped CSS was needed.
- No physical or root-relative filesystem path appears in any new client-facing payload (spot-checked in the batch endpoint response and `GET /api/archive/jobs`).
- Manual verification steps above pass in a running `make docker-run` session.
- `README.md`'s `## Current Supported Features` table gains/adjusts a row describing multi-select batch move, per `AGENTS.md`'s standing instruction to update it whenever a spec changes a user-facing feature.

## Rollback Plan

- The new client-side selection UI is additive (new fields, a new toolbar block, a new click handler) and gated entirely behind the presence of Ctrl/Shift-modified clicks or the new toolbar controls; reverting the `ArchiveBrowser.razor`/`ArchiveMovePicker.razor` changes alone restores today's single-item-only Move UI with no server-side dependency.
- The new server route (`PATCH /api/archive/{category}/items/location`), `ArchiveService.BatchMove`, the `BatchMove` job kind, and `ArchiveMutationExecutor.BatchMoveAsync` are additive to the existing pipeline (`Specs/20260922122650-archive-move-delete-progress`) — removing the new route registration in `MapArchiveEndpoints` disables the feature server-side while leaving every existing single-item Move/MoveToTrash/EmptyTrash endpoint, job kind, and executor path untouched.
- No data migration, schema, or persisted-state change is introduced (job status remains in-memory only, matching the existing pipeline), so rollback requires no cleanup step beyond reverting the code changes.
