# Requirements: Archive Browser Sort Filter

## Table of Contents

- [Archive Browser Sort Filter](#requirements-archive-browser-sort-filter)
  - [Problem Statement](#problem-statement)
  - [User Stories](#user-stories)
  - [Functional Requirements](#functional-requirements)
  - [Non-Functional Requirements](#non-functional-requirements)
  - [Out of Scope](#out-of-scope)
  - [Open Questions](#open-questions)

## Problem Statement

`ArchiveBrowser.razor` (`WebApp/WebApp.Client/Components/ArchiveBrowser.razor:51-55`) already lets a user filter the current folder's items by typing into a `FolderSearchBar`, and `_filteredItems` (`ArchiveBrowser.razor:843-846`) narrows `_listing.Items` by name substring. The underlying order those items render in, however, is fixed at whatever `GET /api/archive/{category}/items` returns — folders first, then files, each group alphabetical (`WebApp/WebApp/Services/ArchiveService.cs:638,903`). There is no way for the user to view a folder's contents sorted by file size (e.g. to find large files worth cleaning up) or by modification date (e.g. to find recently added files) without leaving the app. This spec adds a client-side sort control next to the existing name filter so the user can re-order the currently filtered items by Name (A-Z or Z-A), Size, or Date, without changing server behavior or the opaque-ID/read-only boundaries documented in `AGENTS.md`.

## User Stories

- Given a folder with many files, when I open the sort dropdown and choose "Size", then the currently visible items re-order largest-first, with folders still listed before files.
- Given a folder with many files, when I open the sort dropdown and choose "Date", then the currently visible items re-order newest-first, with folders still listed before files.
- Given I have chosen "Size" or "Date" sort, when I open the sort dropdown and choose "Name (A-Z)", then items return to A-Z order (the default), with folders still listed before files.
- Given I have not touched the sort dropdown, when I open any Archive Browser category (or Trash) for the first time, then items render sorted by Name A-Z, exactly as they do today.
- Given I picked a non-default sort option, when I reload the page or navigate to a different Archive category, then the previously chosen sort option is still applied (persisted client-side).
- Given I have typed a name filter and also chosen a non-default sort, when the filter text changes, then the filtered subset is re-sorted using the currently selected sort option (filter and sort compose, they do not reset each other).

## Functional Requirements

1. FR1 — `ArchiveBrowser.razor` renders a new icon-only dropdown button using the `bi-filter` Bootstrap icon, positioned in the `card-header` alongside the existing `FolderSearchBar` (`ArchiveBrowser.razor:49-55`), visible under the same condition the search bar uses (`_listing is not null && _listing.Items.Count > 0`).
2. FR2 — The dropdown offers exactly four mutually exclusive options: "Name (A-Z)", "Name (Z-A)", "Size", and "Date", following the same Bootstrap `dropdown`/`dropdown-menu`/`dropdown-item` markup pattern already used for the "Create" dropdown in the same component (`ArchiveBrowser.razor:58-103`).
3. FR3 — The currently active sort option is visually indicated in the dropdown (e.g. an active/checked state on the matching `dropdown-item`) and exposed with `aria-pressed` or equivalent so assistive technology can determine the current selection.
4. FR4 — Selecting "Name (Z-A)" sorts the current `_filteredItems` result descending by `ArchiveItemDto.Name` using `StringComparer.OrdinalIgnoreCase`, with folders still grouped first and also ordered Z-A. Selecting "Name (A-Z)" sorts the current `_filteredItems` result ascending by `ArchiveItemDto.Name` using `StringComparer.OrdinalIgnoreCase`, matching today's default order.
5. FR5 — Selecting "Size" sorts the current `_filteredItems` result descending by `ArchiveItemDto.SizeBytes`, treating a `null` `SizeBytes` (folders always have `null` today) as `0`.
6. FR6 — Selecting "Date" sorts the current `_filteredItems` result descending by `ArchiveItemDto.LastWriteTimeUtc` (newest first).
7. FR7 — Regardless of the selected sort option, items where `Kind == ArchiveItemKind.Folder` are always grouped before items where `Kind == ArchiveItemKind.File`; the selected sort key only orders within each group. Folders themselves are ordered by name within their group (A-Z, or Z-A under "Name (Z-A)") (folders carry no `SizeBytes`, so Size/Date sorting has no meaningful folder-level key).
8. FR8 — The chosen sort option is applied on top of the existing name-filter result (`_filteredItems`) without changing `FolderSearchBar`'s filter behavior or `_filterText` state.
9. FR9 — The chosen sort option is persisted in the browser's `localStorage` (via `IJSRuntime`, no new server call) under a dedicated key, is read back once when `ArchiveBrowser` first initializes, and defaults to "Name" when no stored value exists or the stored value is unrecognized/storage is unavailable.
10. FR10 — Changing the sort option does not trigger a server request, a listing reload (`LoadAsync`), or reset `_filterText`, `_selectedForMoveIds`, or any open action panel state.
11. FR11 — The sort control and its options work identically across every `ArchiveBrowser` usage/category, including the `trash` category, since the component is category-agnostic (`ArchiveBrowser.razor:791` `Category` parameter).

## Non-Functional Requirements

- The new sort logic must be a small, pure, unit-testable class (following the existing `WebApp/WebApp.Client/Models/VideoFrameState.cs` / `FillTabState.cs` pattern of self-contained state/logic classes with dedicated xUnit tests) rather than inline LINQ duplicated across the computed property, so folder-grouping and tie-breaking rules are verified independently of the Razor markup.
- No new runtime package, JS library, or server endpoint is introduced; persistence uses the existing direct `IJSRuntime` → `localStorage` call pattern already used by `WebApp/WebApp.Client/wwwroot/js/theme.js` (or an equivalent minimal `IJSRuntime.InvokeAsync` call), not a new JS module, since the operation is a single get/set of a string value.
- Icon-only dropdown trigger must meet the design system's accessibility bar already documented in `AGENTS.md` (accessible name, visible focus, minimum 40×40 CSS-pixel target, Bootstrap tooltip or equivalent), matching the existing 44×44 `btn-primary`/`btn-secondary` circular icon buttons in the same header (`ArchiveBrowser.razor:60-119`).
- No physical or root-relative path is introduced anywhere in this feature; only fields already present on the browser-safe `ArchiveItemDto` (`Name`, `SizeBytes`, `LastWriteTimeUtc`, `Kind`) are used.

## Out of Scope

- Server-side sorting, pagination, or changes to `GET /api/archive/{category}/items` ordering — sorting stays entirely client-side over the already-fetched listing.
- Computing an aggregate size for folders (folders continue to have no `SizeBytes`; they are not reordered by size relative to each other).
- An ascending/descending toggle UI for Size/Date (Size defaults largest-first, Date defaults newest-first, per the approved defaults; no user-facing direction switch).
- Multi-column or multi-key custom sorting, saved sort presets per folder, or a "sort by type" option.
- Any change to the existing name-filter (`FolderSearchBar`) behavior, matching algorithm, or placement.

## Open Questions

None — sort defaults, folder-grouping behavior, folder-size handling, and persistence were confirmed during discovery.
