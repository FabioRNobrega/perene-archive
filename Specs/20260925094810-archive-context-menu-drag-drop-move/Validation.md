# Validation: Archive Context Menu and Drag-and-Drop Move

## Table of Contents

- [Validation: Archive Context Menu and Drag-and-Drop Move](#validation-archive-context-menu-and-drag-and-drop-move)
  - [Acceptance Criteria](#acceptance-criteria)
  - [Test Cases](#test-cases)
  - [Manual Verification](#manual-verification)
  - [Definition of Done](#definition-of-done)
  - [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | Right-clicking any item card shows the in-app menu and never the browser's native menu. |
| FR2 | Right-click on an unselected card leaves only that card selected; right-click inside a selection keeps all selected items. |
| FR3 | Menu appears at the pointer, stays fully inside the viewport near right/bottom edges, and closes on Escape, outside click, scroll, item choice, and navigation. |
| FR4 | With one target, the menu shows the same entries as that card's actions panel (file vs folder vs media vs trash variants) and each opens the same dialog/action. |
| FR5 | With 2+ selected, menu shows a count header plus Move and Move to Trash only (no Trash entry in the trash category). |
| FR6 | While an operation or mutation job is active, all menu entries are disabled. |
| FR7 | Menu has menu/menuitem roles, focuses the first enabled item, Arrow keys move focus, Escape closes and refocuses the card. |
| FR8 | Cards drag; dragging a selected card carries the whole selection; dragging an unselected card carries only itself and leaves selection unchanged; nothing drags while busy. |
| FR9 | A valid folder target shows a highlight with a non-color cue; the dragged folder itself and files show none and reject the drop. |
| FR10 | Dropping on a folder card starts one batch-move job for all dragged IDs, the progress popup appears, and selection clears. |
| FR11 | Dropping on the root or an ancestor breadcrumb moves items to that folder; the current-folder breadcrumb is not a target. |
| FR12 | Dropping on itself/current folder sends no request; a name conflict or self-descendant move shows the existing error and moves nothing. |
| FR13 | Moved items appear in the destination after job completion; failure reports counts like the existing move; no new endpoint/job kind exists. |
| FR14 | Network requests and DOM contain only opaque IDs; no path appears in `dataTransfer`, attributes, or requests. |
| FR15 | Left-click, Ctrl/Shift+Click, actions panel button, selection toolbar, Move picker, file upload drop, and right-click on inputs/empty space behave as before. |

## Test Cases

**Unit tests** (xUnit, `WebApp.Tests/Client/ArchiveDragDropRulesTests.cs`, matching `ArchiveItemSorterTests.cs` style):

- `ResolveTargets` returns the whole selection when the context item is selected, and only the item when it is not.
- `CanDrop` is false for a target among dragged IDs, false for the current folder, and true for a different folder.
- Empty/duplicate ID inputs return empty/de-duplicated sets.

**Integration tests:**

- Existing batch-move endpoint tests under `WebApp.Tests/Endpoints/` must still pass unchanged (confirms no server regression).
- ⚠️ TODO: Browser-level (Playwright-style) drag/context-menu automation does not exist in this repo; covered by manual verification below.

## Manual Verification

1. `make docker-run` and open the app; open Archive Browser on a category with several folders and files.
2. Right-click a file: native menu does not appear; the in-app menu shows the same entries as the actions panel. Try each of Rename, Move, Move to Trash, and Escape.
3. Right-click near the bottom-right of the window: the menu stays fully visible.
4. Ctrl+Click three items, right-click one of them: header says 3 selected, only Move and Move to Trash are shown. Right-click an unselected item: selection collapses to that item.
5. Drag a single file onto a folder card: highlight appears, drop starts the progress popup, item appears inside that folder afterward.
6. Select several items, drag one of them onto a folder: all move in one job.
7. Drag a folder over itself and over a file card: no highlight, drop does nothing.
8. Create a name conflict in the destination and drop: the existing error shows and nothing moves.
9. Open a nested folder and drop items on an ancestor breadcrumb and on the root entry: they move up.
10. Repeat in dark and light themes, with reduced motion enabled, and at a narrow window width.
11. Confirm the actions button, toolbar Move, picker, and file-upload drag-and-drop still work, and right-click in text inputs still shows the native menu.
12. In browser dev tools, confirm no filesystem path in the requests or in DOM attributes.
13. `make test` passes.

## Definition of Done

- Requirements, Plan, and Validation are current in this spec folder.
- All existing tests pass via `make test`; new rules tests pass.
- Right-click menu and drag-and-drop are verified in Chromium and Firefox, in both themes.
- Responsive, disabled, and accessibility states verified per the manual steps.
- `README.md` `## Current Supported Features` row and `AGENTS.md` updated.
- Vendor evidence in `Plan.md` remains accurate.

## Rollback Plan

- The change is client-only. Revert the `oncontextmenu`/drag attributes, context-menu markup, and `archiveContextMenu.js` import in `ArchiveBrowser.razor`; the actions panel, selection toolbar, Move picker, and the batch-move endpoint continue to work unchanged.
