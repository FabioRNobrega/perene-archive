# Requirements: Archive Move — Cross-Category Column Picker

## Table of Contents

- [Requirements: Archive Move — Cross-Category Column Picker](#requirements-archive-move--cross-category-column-picker)
  - [Problem Statement](#problem-statement)
  - [User Stories](#user-stories)
  - [Functional Requirements](#functional-requirements)
  - [Non-Functional Requirements](#non-functional-requirements)
  - [Out of Scope](#out-of-scope)
  - [Open Questions](#open-questions)

## Problem Statement

`ArchiveBrowser.razor` renders one browser per archive category ("page") — Videos, Photos, Music, Documents, Books, Downloads, Shared, Family, History, Trash — each backed by its own root directory (`ArchiveService.GetCategoryRoot`, `WebApp/WebApp/Models/ArchiveCategory.cs:10-22`) under a single shared `ArchiveRootOptions.Path`. The current "Move" action (`ArchiveBrowser.razor:297-306`, modal at `ArchiveBrowser.razor:524-551`) is a plain `<select>` populated from `_folderChoices`, which is always just the subfolders of the *currently displayed* folder in the *currently displayed* category (`ArchiveBrowser.razor:620,707,1289,1506`). Two problems follow directly from this:

1. The Move menu item's `disabled` state is driven only by `_folderChoices.Count == 0` (`ArchiveBrowser.razor:300`), not by the item's kind — so whenever the current folder happens to contain no subfolders, Move is disabled for every item in it, files and folders alike. Users experience this as "Move only works for folders," because folders are more likely to sit in folder-rich locations while a page's file-only leaf folders end up with Move permanently greyed out.
2. Because the destination list is built solely from the current listing, the UI has no way to target any folder outside the current folder/category, and `IArchiveService.Move(string categoryKey, string itemId, string? destinationFolderId)` (`WebApp/WebApp/Services/IArchiveService.cs:31`, implemented at `WebApp/WebApp/Services/ArchiveService.cs:174-198`) only ever resolves `destinationFolderId` inside the *same* `categoryKey`'s root — there is no code path that moves an item from one category root to another (e.g. Downloads → Videos).

This spec replaces the single-level `<select>` with a Finder-style, multi-column drill-down folder picker (per the HTML/CSS/JS design reference the user supplied, reinterpreted in Bootstrap + Blazor WebAssembly, not copied verbatim) that can browse into any move-eligible category and any folder depth within it, and extends the move capability end to end (client request, endpoint, `IArchiveService`) to carry an explicit destination category alongside the destination folder.

## User Stories

- Given a file or folder in any archive category, when the user opens its "Move" action, then a column-picker dialog opens showing folders they can drill into, and the Move control is enabled regardless of whether the item is a file or a folder, and regardless of whether the current folder has subfolders.
- Given the Move dialog is open, when the user clicks a folder in a column, then a new column appears to its right showing that folder's subfolders, mirroring macOS Finder's column view.
- Given the Move dialog is open on an item from "Downloads", when the user selects the "Videos" category in the first column and drills into one of its folders, then confirming the move relocates the item into that folder under the Videos category root, and the Downloads listing (still on screen) no longer shows the item.
- Given the user is moving a folder, when they drill into that same folder (or any of its descendants) inside the picker, then that folder is not offered as a valid destination and confirming is blocked, consistent with the existing "a folder cannot be moved into itself" rule.
- Given the user opens Move on an item that already lives three folders deep in "Photos", when the dialog opens, then the picker is pre-drilled to that item's current category and folder path so moving to a nearby sibling folder takes minimal clicks.

## Functional Requirements

1. FR1 — The "Move" dropdown action (`ArchiveBrowser.razor:297-306`) is enabled for both `ArchiveItemKind.File` and `ArchiveItemKind.Folder` items whenever `_operationPending` is false; its enabled state no longer depends on whether the current folder contains subfolders.
2. FR2 — Opening Move renders a Bootstrap modal containing a horizontally scrollable row of columns (Bootstrap `d-flex overflow-auto` + one `list-group` per column, per the supplied design's column layout), each column listing only folder-kind items (never files) of the folder/category selected in the column to its left.
3. FR3 — The leftmost column lists the archive categories eligible as move destinations: every `ArchiveCategory` with `CanCreateFolder == true` (Videos, Photos, Music, Documents, Books, Downloads, Shared, Family), sourced from the same static category list the client already uses for navigation (`Sidebar.razor:69-78`). History and Trash are excluded from this column.
4. FR4 — Clicking a category or folder entry in a column selects it (visually marked, mirroring the reference design's `.selected` state) and appends/replaces the columns to its right with that entry's folder-kind children, fetched via the existing `GET /api/archive/{category}/items?folderId=` endpoint and filtered client-side to `Kind == Folder`.
5. FR5 — When the Move dialog opens for an item, the picker is pre-initialized with a column path that already drills into the item's current category and current folder (using the existing `Breadcrumbs`/`CurrentFolderId` the browser already tracks), so the deepest selected column reflects where the item lives today.
6. FR6 — The dialog's confirm control ("Move here") is enabled only when a valid destination is selected (a category or folder column is selected) and disabled while `_operationPending` is true; confirming issues one HTTP request that moves the item to the deepest-selected folder in the picker (or to the selected category's root when no subfolder was drilled into).
7. FR7 — `MoveArchiveItemRequest` (`WebApp/WebApp.Client/Models/MoveArchiveItemRequest.cs`) gains a required destination category alongside the existing nullable destination folder id, so a single move request fully identifies the target location independent of the source category in the URL.
8. FR8 — `IArchiveService.Move`/`ArchiveService.Move` (`WebApp/WebApp/Services/IArchiveService.cs:31`, `ArchiveService.cs:174-198`) accepts a destination category key distinct from the source category key, resolves the destination folder within the destination category's own root, and performs the physical move across category roots (both are subdirectories of the same `ArchiveRootOptions.Path`, so `Directory.Move`/`File.Move` already work across them without a copy+delete fallback).
9. FR9 — Moving a folder into itself or into any of its own descendant folders — now checked across category boundaries, not just within one category — remains rejected with the existing `ArchiveValidationException("A folder cannot be moved into itself.")`, and the picker itself prevents selecting the moving folder or its descendants as a destination column.
10. FR10 — After a successful cross-category move, the response returned to the client reflects the *source* category/folder listing (as today, `ArchiveService.Move` returns `BuildListing(category, destinationFolder)` for the same category currently — this must now return the *source* category's listing with the moved item removed, since the user stays on the source page), and `PATCH /api/archive/{category}/items/{id}/location` keeps its current route shape with `{category}` still meaning the source category.
11. FR11 — Existing same-category, same-folder-depth moves (the only case supported today) continue to work unchanged through the new dialog and request shape.

## Non-Functional Requirements

- The picker UI uses only Bootstrap components/utilities (`modal`, `list-group`, `d-flex`/`overflow-auto` for the column strip) and existing Bootstrap Icons; no new custom CSS or JavaScript is introduced, consistent with `AGENTS.md`'s design-system constraints (`Specs/20260827194328-perene-tech-design-system-refactor/`).
- All column state (selected path, loaded children per column, loading/error per column) is owned by C# component state in `ArchiveBrowser.razor` (or a new focused Razor component it composes), not JavaScript interop.
- The picker must remain usable at phone width: columns scroll horizontally within the modal body rather than causing the whole page to overflow, matching the reference design's `overflow-x:auto` column strip translated into Bootstrap's `overflow-auto` utility.
- `ArchiveService.Move` must keep enforcing the existing path-containment invariant (`ContainedPath`/`IsWithinOrSame`) independently for both the source and destination category roots — a destination category/folder id can never resolve outside its own category root.
- No physical or root-relative filesystem path is ever sent to the browser; the picker only ever receives opaque item ids and category keys, same as the rest of the archive browser.

## Out of Scope

- Multi-select / bulk move of several items at once.
- Drag-and-drop moving (the picker remains a click-through dialog).
- Making History or Trash valid move destinations.
- Changing how "Move to Trash" or "Empty Trash" work.
- Any change to upload, rename, create-folder, or create-file flows beyond what's needed to keep them compiling against the updated `MoveArchiveItemRequest`/`IArchiveService.Move` signatures.

## Open Questions

- ⚠️ TODO: none outstanding — category scope (CanCreateFolder-only) and picker pre-drill behavior were confirmed with the user during spec discovery.
