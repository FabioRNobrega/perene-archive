# Requirements: Archive Context Menu and Drag-and-Drop Move

## Table of Contents

- [Requirements: Archive Context Menu and Drag-and-Drop Move](#requirements-archive-context-menu-and-drag-and-drop-move)
  - [Problem Statement](#problem-statement)
  - [User Stories](#user-stories)
  - [Functional Requirements](#functional-requirements)
  - [Non-Functional Requirements](#non-functional-requirements)
  - [Out of Scope](#out-of-scope)
  - [Open Questions](#open-questions)

## Problem Statement

In the Archive Browser (`WebApp/WebApp.Client/Components/ArchiveBrowser.razor`) a user can reach an item's actions only through a small `bi-arrow-bar-down` button on each card that slides open the `archive-actions-panel` (`ToggleActions`, `StartRenameFromActions`, `StartMoveFromActions`, `DeleteFromActionsAsync`, ...). Moving anything requires opening that panel (or the multi-select toolbar) and then picking the destination in `ArchiveMovePicker`. Desktop file managers (Dolphin, Explorer, Finder) instead open the same action list on right-click and let the user drag files/folders onto a folder to move them. The browser's native context menu currently appears on right-click, which is useless here. Multi-select (Ctrl/Shift+Click) and a batch-move endpoint (`PATCH /api/archive/{category}/items/location`, `BatchMoveArchiveItemsRequest`) already exist from `Specs/20260922132005-archive-multi-select-move/`, so this spec adds two new input paths on top of that existing pipeline with no new server behavior.

## User Stories

- Given the user is viewing a folder in the Archive Browser, when they right-click a card, then the browser's native context menu is suppressed and an in-app menu opens at the pointer listing the same actions as that card's actions panel.
- Given two or more cards are selected, when the user right-clicks one of the selected cards, then the menu acts on the whole selection and offers only Move and Move to Trash; when they right-click an unselected card, the selection is replaced by that card alone before the menu opens.
- Given the user drags a card (or a multi-selection) onto a folder card, when they drop it, then the items are moved into that folder immediately through the existing batch-move job and progress popup.
- Given the user drags items over a breadcrumb ancestor of the current folder, when they drop, then the items move up into that ancestor folder (or the category root).
- Given a drop would be invalid (onto a dragged item itself, onto a non-folder, or the destination has a name conflict), when the user drops or hovers, then no move starts and the user sees the same error the existing move flow already shows.

## Functional Requirements

1. FR1 — Each item card in `ArchiveBrowser.razor` handles `contextmenu` with `@oncontextmenu:preventDefault` so the native browser menu never appears over an item card.
2. FR2 — Right-click on an item that is not in `_selectedForMoveIds` clears the selection and selects that item alone (setting `_selectionAnchorId`); right-click on an item already in the selection preserves the selection.
3. FR3 — The context menu opens at the pointer position (`MouseEventArgs.ClientX/ClientY`), is clamped to stay fully inside the viewport, and closes on Escape, an outside click/scroll, choosing an item, navigating, or starting another mutation.
4. FR4 — For a single target, the menu lists exactly the actions the actions panel shows for that item today, under the same conditions (`Play as playlist`, `Convert / Compress to MP4`, folder thumbnail set/remove, `Rename`, `Move`, `Move to Trash`; trash category conditions unchanged) and calls the same handlers (`PlayAsPlaylistFromActions`, `OpenConversionPlanFromActionsAsync`, `StartRenameFromActions`, `StartMoveFromActions`, `DeleteFromActionsAsync`, ...).
5. FR5 — For a target selection of 2+ items, the menu lists only `Move` (calls `StartBatchMove`) and `Move to Trash` (calls `StartBatchTrash`), with a header showing the selected count; `Move to Trash` is omitted in the trash category, as in the selection toolbar.
6. FR6 — Menu items are disabled while `_operationPending` or an archive mutation job is active (`_activeJob is not null`), matching the existing disabled rules for the panel and toolbar.
7. FR7 — The context menu is keyboard accessible once open: `role="menu"` / `role="menuitem"` semantics, focus moves to the first enabled item, Arrow Up/Down move focus, Escape closes and returns focus to the originating card.
8. FR8 — Item cards are `draggable="true"` unless an operation is pending, a job is active, or the card is a category root. Starting a drag on a card that is in the multi-selection drags the whole selection; starting a drag on an unselected card drags only that card, without changing the selection.
9. FR9 — Folder cards are drop targets. While a valid drag hovers one, it shows a highlighted state (Bootstrap border/background utilities plus a non-color cue such as an icon/label change); invalid targets (a folder that is itself among the dragged items, or any non-folder card) show no highlight and do not accept the drop.
10. FR10 — Dropping on a valid folder card calls the existing batch-move endpoint (`PATCH api/archive/{Category}/items/location` with `BatchMoveArchiveItemsRequest(ids, Category, targetFolderId)`) through `EnqueueMutationAsync`, then clears the selection. A single dragged item also uses the batch endpoint with one ID.
11. FR11 — Breadcrumb ancestor entries (the root entry and every non-current breadcrumb button) are drop targets with the same hover highlight and drop behavior as FR9/FR10; the current-folder breadcrumb is not a target. The root entry targets `DestinationFolderId = null`.
12. FR12 — A drop that cannot be valid client-side (target is one of the dragged items, or the current folder) is ignored without a request. Any other validation failure (name conflict, moving a folder into its own descendant) is reported by the server exactly as today's move flow reports it, and nothing is moved.
13. FR13 — Dropped moves reuse the existing move-progress popup, polling, listing refresh, and no-rollback failure semantics unchanged; no new job kind, endpoint, DTO, or server code is added.
14. FR14 — Dragging and the context menu only carry opaque item IDs already in the current listing; no physical or root-relative path is placed in `dataTransfer`, DOM attributes, or requests.
15. FR15 — Existing interactions are unchanged: left-click, Ctrl/Shift+Click selection, the per-card actions button/panel, the selection toolbar, the Move picker, file upload drag-and-drop, and the right-click behavior everywhere outside item cards (native menu on text fields, breadcrumbs, and empty space).

## Non-Functional Requirements

- No physical or root-relative filesystem path is exposed to the browser or written to logs; only opaque IDs cross the boundary.
- UI uses Bootstrap components/utilities (`dropdown-menu`, `dropdown-item`, `dropdown-header`, border/background utilities) and Bootstrap Icons only, per the design-system spec; custom CSS is limited to behavior Bootstrap cannot express (drag/drop-target state, fixed positioning), scoped in `ArchiveBrowser.razor.css`, and reuses global tokens. Dark and light themes, WCAG AA contrast, non-color state cues, and reduced motion must hold.
- JavaScript stays minimal: DOM-only concerns (viewport clamping, `dataTransfer` setup that Blazor cannot do, focus). Selection, targets, validation, and requests remain in C#.
- The touch/keyboard fallback is the existing actions button, selection toolbar, and Move picker, which stay fully functional.
- Client-side validation is convenience only; the server remains the authority through `ArchiveService.BatchMove`.
- Drag hit-testing must not degrade grid rendering for large folders (no per-item JS listeners; use event delegation or Blazor handlers on existing elements).

## Out of Scope

- Any server, endpoint, DTO, or `ArchiveMutationKind` change.
- Dropping onto Trash, onto sidebar/other category entries, or onto the Move picker; cross-category drag.
- Right-click on empty grid space (New file/New folder/Upload menu), right-click on breadcrumbs, and an "Open" menu entry.
- Long-press context menu on touch, Shift+F10/Menu-key opening, touch drag-and-drop, and keyboard drag-and-drop.
- Dragging items out of the browser window, dropping OS files to upload (untouched), and copy-on-modifier-key (Ctrl+drag) semantics.
- Drag-to-reorder or auto-scroll while dragging near grid edges.
- Custom drag preview thumbnails beyond the browser default.

## Open Questions

- ⚠️ TODO: Should hovering a folder card for ~1s while dragging auto-open it (spring-loaded folders)? Assumed no.
- ⚠️ TODO: Should the browser default drag image be replaced with a count badge for multi-item drags? Assumed no for the first slice.
