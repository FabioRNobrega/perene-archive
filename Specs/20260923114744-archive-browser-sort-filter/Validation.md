# Validation: Archive Browser Sort Filter

## Table of Contents

- [Archive Browser Sort Filter](#validation-archive-browser-sort-filter)
  - [Acceptance Criteria](#acceptance-criteria)
  - [Test Cases](#test-cases)
  - [Manual Verification](#manual-verification)
  - [Definition of Done](#definition-of-done)
  - [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | A `bi-filter` icon dropdown button renders in the Archive Browser card header, next to the name filter, whenever `_listing.Items.Count > 0`, and is absent when the folder is empty. |
| FR2 | Opening the dropdown shows exactly four items labeled "Name (A-Z)", "Name (Z-A)", "Size", and "Date". |
| FR3 | The item matching the current `_sortOption` is visually marked active (e.g. checkmark icon) and carries `aria-pressed="true"`; the other two carry `aria-pressed="false"`. |
| FR4 | Selecting "Name (A-Z)" renders items A-Z by `Name` (case-insensitive), identical to today's default order; selecting "Name (Z-A)" renders folders then files Z-A by `Name`. |
| FR5 | Selecting "Size" renders files largest-`SizeBytes`-first; folders (whose `SizeBytes` is `null`, treated as `0`) never appear ahead of a file by virtue of size. |
| FR6 | Selecting "Date" renders files newest-`LastWriteTimeUtc`-first. |
| FR7 | In every sort mode, every item with `Kind == Folder` appears before every item with `Kind == File`; folders among themselves stay ordered by name (Z-A under "Name (Z-A)"). |
| FR8 | Typing into the name filter while a non-default sort is active narrows the item set and keeps it ordered by the active sort option (not reset to Name). |
| FR9 | Reloading the browser tab (or navigating away and back) after selecting "Size" or "Date" re-applies that same option on the next `ArchiveBrowser` render; clearing `localStorage` or blocking it falls back to "Name". |
| FR10 | Selecting a sort option does not trigger a visible loading spinner, does not clear `_filterText`, and does not clear an in-progress multi-select (`_selectedForMoveIds`). |
| FR11 | The same dropdown, with the same four options and folder-first behavior, works when `Category == "trash"` and in every other Archive Browser category. |

## Test Cases

**Unit tests (`WebApp.Tests`, run via `make test`):**

- `WebApp.Tests/Client/ArchiveItemSorterTests.cs` (new, follows `VideoFrameStateTests.cs`/`ArchiveUploadStateTests.cs` conventions):
  - `Sort_WithNameOption_OrdersFilesAlphabeticallyCaseInsensitive` — mixed-case names sort A-Z.
  - `Sort_WithNameDescendingOption_OrdersFoldersThenFilesReverseAlphabetically` — folders and files each sort Z-A, folders first.
  - `Sort_WithSizeOption_OrdersFilesLargestFirst` — files with distinct `SizeBytes` sort descending.
  - `Sort_WithSizeOption_TreatsNullSizeAsZero` — a file with `SizeBytes = null` sorts last among files under Size.
  - `Sort_WithDateOption_OrdersFilesNewestFirst` — files with distinct `LastWriteTimeUtc` sort descending.
  - `Sort_AlwaysOrdersFoldersBeforeFiles_RegardlessOfOption` — parameterized/looped over all four `ArchiveSortOption` values, asserting folder items precede file items in the output.
  - `Sort_OrdersFoldersByNameWithinGroup` — multiple folders sort A-Z among themselves under every option.
  - `Sort_WithEmptyInput_ReturnsEmptyList` — no-item edge case.

**Integration tests:**

- ⚠️ TODO: no existing `WebApplicationFactory`-based test exercises `ArchiveBrowser.razor` interactively (the existing `Endpoints/` tests cover the minimal API surface, not client Razor components); this feature is client-only state/logic, so it is covered by the unit tests above plus manual verification rather than a new integration test category.

## Manual Verification

1. `make docker-run-bg` (or `make docker-run`) to start the stack per `AGENTS.md` Execution Environment.
2. Open the app, navigate to Archive Browser, select a category (e.g. Videos or Archive root) with several files of varying size and modified date, and confirm items currently render Name A-Z with folders first, unchanged from before this feature.
3. Click the new `bi-filter` icon button; confirm the dropdown opens with "Name (A-Z)" marked active, and "Name (Z-A)"/"Size"/"Date" present.
4. Select "Size"; confirm files re-order largest-first with no page reload/spinner, and any folders remain listed first.
5. Select "Date"; confirm files re-order newest-first with no page reload/spinner.
6. Type a few characters into the existing name filter while "Date" is active; confirm the filtered subset stays sorted newest-first.
7. Reload the browser tab; confirm "Date" (or whichever option was last chosen) is still active and applied without navigating away.
8. Open DevTools, clear/block `localStorage` for the origin, reload; confirm the sort silently falls back to "Name (A-Z)" with no console-breaking error (a caught `JSException` at most).
9. Switch to the Trash category; confirm the same dropdown, options, and folder-first behavior work there.
10. Verify keyboard/focus: tab to the `bi-filter` button, open with Enter/Space, arrow through items, confirm visible focus rings and that the active item's `aria-pressed` state is programmatically readable (e.g. via browser accessibility inspector).

## Definition of Done

- Requirements, Plan, and Validation docs in this spec folder are complete and consistent.
- `make test` passes, including the new `ArchiveItemSorterTests.cs`.
- `ArchiveBrowser.razor`, `ArchiveSortOption.cs`, and `ArchiveItemSorter.cs` are implemented per this plan; no other `ArchiveBrowser`-consuming page needs changes since sorting is internal to the component.
- Manual verification steps above pass in the Docker Compose dev stack across at least one non-trash and the trash category.
- No new CSS file or `.razor.css` rule was needed (Bootstrap dropdown/button utilities sufficed) — confirmed by review of the diff.
- No new physical/root-relative path or server endpoint was introduced; `README.md` "Current Supported Features" table is updated with a row describing the new sort control, per the `AGENTS.md` constraint to update it whenever a spec adds a user-facing feature.

## Rollback Plan

This is a purely additive, client-side-only feature with no server or storage schema change. To roll back: revert the commit(s) touching `ArchiveBrowser.razor`, `ArchiveSortOption.cs`, and `ArchiveItemSorter.cs` (and the corresponding test file). No database migration, environment variable, or server config exists to disable. A stale `localStorage` key left behind by a rolled-back client is harmless and unread by any other feature (it is read only inside `ArchiveBrowser.OnAfterRenderAsync`), so no cleanup step is required.
