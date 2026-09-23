# Validation: Archive Multi-Select Move to Trash

## Table of Contents

- [Validation: Archive Multi-Select Move to Trash](#validation-archive-multi-select-move-to-trash)
  - [Acceptance Criteria](#acceptance-criteria)
  - [Test Cases](#test-cases)
  - [Manual Verification](#manual-verification)
  - [Definition of Done](#definition-of-done)
  - [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | With 1+ items selected in a non-Trash category, the toolbar shows an outline-danger "Move to Trash" button with a trash icon between Move and Clear selection. |
| FR2 | In the Trash category, selecting items shows the toolbar without a "Move to Trash" button. |
| FR3 | While a mutation job is running the button is disabled. |
| FR4 | Clicking opens a modal naming the count; Cancel closes it, moves nothing, and keeps the selection. |
| FR5 | Confirming issues one `POST /api/archive/{category}/items/trash` with the selected IDs. |
| FR6 | Empty list → 400; `trash` category → 403; an unknown ID or a category root → error and no file is moved. |
| FR7 | Each item lands directly under the Trash root; name collisions (with existing Trash content or within the batch) get unique suffixed names; nothing is overwritten. |
| FR8 | The job has kind `BatchMoveToTrash`, `TotalItems` = files across all entries, and label "N items". |
| FR9 | The worker runs the job through `BatchMoveAsync`, and `GET /api/archive/jobs` shows increasing `ProcessedItems`. |
| FR10 | A forced IO failure yields a Failed job whose diagnostic mentions the processed/total counts and no rollback; earlier items remain in Trash. |
| FR11 | The popup heading reads "Moving to Trash: N items". |
| FR12 | After acceptance the selection clears, the modal closes, and the listing refreshes once the job completes. |
| FR13 | A rejected request shows the error alert and keeps the selection. |
| FR14 | Single-item Delete, single Move, and batch Move behave exactly as before. |
| FR15 | The README `Archive management` row mentions multi-select Move to Trash. |

## Test Cases

**Unit tests** (xUnit, existing patterns):
- `WebApp.Tests/Services/ArchiveServiceTests.cs`: `BatchMoveToTrash` builds one job with an entry per item and the correct `TotalItems` for a mix of files and folders; throws for empty list, Trash category, unknown ID, and category root; produces unique destinations when the Trash already holds a same-named item.
- `WebApp.Tests/Services/ArchiveMutationExecutorTests.cs`: a `BatchMoveToTrash` job moves files and folders into a temp Trash root, reports progress, and returns the trash-specific failure message when a source is locked/missing mid-run.

**Integration tests:**
- `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs` (`WebApplicationFactory`): `POST /api/archive/{category}/items/trash` returns 202 with a job for valid IDs, 400 for an empty list, 403 for the trash category, 404/400 for unknown IDs, and the completed job leaves the items in the Trash listing.
- ⚠️ TODO: UI markup for the toolbar/modal has no bUnit-style harness in this repo; covered by manual verification (the existing `WebApp.Tests/Client/` tests cover only C# state models).

## Manual Verification

1. `make docker-run`, open the app, and go to a category with several files and a folder.
2. Ctrl+Click two files and Shift+Click to extend to a folder; confirm the "N selected" toolbar appears.
3. Confirm "Move to Trash" is present, outline-danger, and keyboard-focusable; click it and verify the modal shows the count.
4. Click Cancel: nothing moves, the selection remains.
5. Click Move to Trash again and confirm: the progress popup reads "Moving to Trash: N items" and counts up; the selection clears; the items disappear from the folder.
6. Open Trash and verify all items are present (folders with their contents). Select items there and verify no "Move to Trash" button appears.
7. Repeat with a name that already exists in Trash and verify the second item gets a suffixed name.
8. Verify single-item Delete and batch Move still work.
9. `make test` passes.

## Definition of Done

- Requirements, Plan, and Validation docs are updated in this spec folder.
- All existing tests still pass via `make test`.
- New service, executor, and endpoint tests exist as listed above.
- UI uses Bootstrap components only, with the confirm modal, disabled state, and error state covered; the button has an accessible name and is keyboard operable.
- `README.md` `Current Supported Features` updated (FR15).

## Rollback Plan

- Revert the change; no data migrations or configuration are involved. To disable only the UI, remove the "Move to Trash" button and modal from `ArchiveBrowser.razor`; the `POST .../items/trash` route and `BatchMoveToTrash` kind are inert without callers. Items already trashed remain recoverable in the Trash category.
