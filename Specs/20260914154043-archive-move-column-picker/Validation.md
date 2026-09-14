# Validation: Archive Move — Cross-Category Column Picker

## Table of Contents

- [Validation: Archive Move — Cross-Category Column Picker](#validation-archive-move--cross-category-column-picker)
  - [Acceptance Criteria](#acceptance-criteria)
  - [Test Cases](#test-cases)
  - [Manual Verification](#manual-verification)
  - [Definition of Done](#definition-of-done)
  - [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | With a category folder containing only files (no subfolders), the "Move" dropdown item is enabled (not greyed out) for those file items when `_operationPending` is false. |
| FR2 | Opening Move renders a modal with a horizontally scrollable strip of `list-group` columns; no folder's files ever appear as selectable entries in any column. |
| FR3 | The first column lists exactly Videos, Photos, Music, Documents, Books, Downloads, Shared, Family — no History, no Trash. |
| FR4 | Clicking a folder in column *N* appends/replaces column *N+1* with that folder's subfolders only. |
| FR5 | Opening Move on an item nested two folders deep pre-renders three columns (category, folder, sub-folder) with the item's current path already selected. |
| FR6 | "Move here" is disabled until a category or folder is selected, and disabled again while the move request is in flight. |
| FR7 | `MoveArchiveItemRequest` serializes both `DestinationCategory` and `DestinationFolderId`; a request missing `DestinationCategory` fails model binding. |
| FR8 | Moving a file/folder from category A to a folder in category B relocates the physical file/directory from A's root subtree to B's root subtree. |
| FR9 | Attempting to move a folder into itself or a descendant folder (via a request crafted to bypass the UI's own filtering) throws `ArchiveValidationException`, including when the destination category differs textually but the resolved physical path is still inside the source folder (not reachable in practice today since categories are disjoint roots, but the check must not silently pass). |
| FR10 | After a cross-category move, the HTTP response body is the *source* category's listing (its `Category`/`DisplayName` match the source, not the destination), and the moved item is absent from it. |
| FR11 | Moving an item to a different folder within its own current category still succeeds and behaves exactly as before this change. |

## Test Cases

**Unit tests** (`WebApp.Tests/Services/ArchiveServiceTests.cs`, following the existing `CreateArchive()`/`CreateService(root.Path)` fixture pattern already used in that file):

- `Move_relocates_item_within_the_same_category` — existing-behavior regression: create a file in `documents/`, a target subfolder, call `Move("documents", fileId, "documents", targetFolderId)`, assert the file exists under the target folder and the returned listing is the *source* folder's listing without the item.
- `Move_relocates_item_across_categories` — create a file in `downloads/`, a target folder in `videos/`, call `Move("downloads", fileId, "videos", targetFolderId)`, assert the file now exists under `Videos/<target>/` on disk and no longer under `Downloads/`.
- `Move_rejects_moving_a_folder_into_itself` — call `Move(category, folderId, category, folderId)`, assert `ArchiveValidationException`.
- `Move_rejects_moving_a_folder_into_its_own_descendant` — create `Downloads/A/B`, call `Move("downloads", idOfA, "downloads", idOfB)`, assert `ArchiveValidationException`.
- `Move_rejects_when_destination_already_has_an_item_with_the_same_name` — assert `ArchiveConflictException`, matching the existing conflict behavior.
- `Move_throws_not_found_for_an_unknown_destination_category` — call `Move("downloads", fileId, "not-a-real-category", null)`, assert `ArchiveNotFoundException`.
- `Move_returns_source_listing_after_cross_category_move` — assert the returned `ArchiveListing.Category.Key` equals the source category, not the destination.

**Client-side (manual, no existing Blazor component test harness in this repo for interactive UI states):**

- ⚠️ TODO: no automated Blazor component test convention exists yet in `WebApp.Tests/Client/`; add one only if the repo later adopts bUnit or similar — until then this flow is covered by Manual Verification below.

## Manual Verification

Starting from a clean state using this repo's documented workflow (`make docker-run-bg`, then open the reported LAN/local URL — see `AGENTS.md`'s Execution Environment):

1. Navigate to Downloads. Confirm at least one plain file (no subfolders present at this level) shows an enabled "Move" action in its dropdown.
2. Click Move on that file. Confirm the column picker modal opens showing the Downloads category pre-selected as the first column and its contents (folders only) as the second column.
3. Click a different category (e.g. Videos) in the first column. Confirm the second column now shows Videos' folders instead of Downloads'.
4. Drill into a folder two levels deep. Confirm each click appends a new column and the previously selected column stays visibly marked as selected.
5. Click "Move here". Confirm the modal closes, the Downloads listing refreshes without the moved file, and navigating to Videos → that folder shows the file now present there.
6. Repeat steps 2-5 for a folder item instead of a file, moving it to a different category. Confirm the folder and its contents move together.
7. Open Move on a folder that has subfolders, and attempt to drill into that same folder or one of its own subfolders inside the picker. Confirm it does not appear as a selectable/enterable destination.
8. Open Move on any item, then click "Move here" without selecting anything different from the pre-drilled current location (or click Cancel). Confirm no error and no unexpected relocation.
9. Repeat step 5 for an item moved to a different folder within the *same* category (e.g. Documents → Documents/Invoices) and confirm this still works exactly as it did before this change.
10. Resize the browser to phone width (~400px) with the Move modal open. Confirm the column strip scrolls horizontally inside the modal without the page itself gaining horizontal scroll.

## Definition of Done

- Requirements, Plan, and Validation docs in this folder are complete and internally consistent.
- All existing tests pass under `make test`.
- New `ArchiveServiceTests` unit tests listed above are added and pass under `make test`.
- `ArchiveMovePicker.razor` is composed of Bootstrap components/utilities only, with no new `.razor.css` or JavaScript file.
- Move is enabled for files and folders alike, verified per Manual Verification steps 1-2.
- Cross-category move works end to end, verified per Manual Verification steps 3-6.
- `README.md`'s `## Current Supported Features` table reflects the new cross-category move capability.
- `AGENTS.md`'s Repository Map / Spec-Kit Workflow references are left accurate (no update needed unless this spec's folder name or scope changes before implementation).

## Rollback Plan

If the new picker or cross-category move causes a regression:

- Revert the `ArchiveMovePicker.razor` addition and the `ArchiveBrowser.razor` changes that reference it, restoring the prior `<select>`-based modal and `_folderChoices` logic from git history.
- Revert `MoveArchiveItemRequest`, `IArchiveService.Move`, `ArchiveService.Move`, and `ArchiveEndpoints.Move` to their pre-change signatures (single-category move) in the same revert.
- No data migration, schema, or persisted state is introduced by this change — physical files/folders moved via the new picker are ordinary filesystem moves and are unaffected by reverting the code; only the code path that performs future moves is rolled back.
