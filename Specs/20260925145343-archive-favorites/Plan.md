# Plan: Shared Archive Favorites

## Table of Contents

- [Summary](#summary)
- [Technical Approach](#technical-approach)
- [Component Breakdown](#component-breakdown)
- [Dependencies](#dependencies)
- [External / Vendor Documentation Evidence](#external--vendor-documentation-evidence)
- [Flow](#flow)
- [Risk Assessment](#risk-assessment)

## Summary

Add a server-owned JSON-backed favorites set to the Archive Browser. It extends the existing archive endpoint/service pattern and `CustomStorageViewService`'s atomic JSON persistence pattern, while extending `ArchiveItemSorter` with a favorite-first default mode that is deliberately bypassed by every explicit sort choice.

## Technical Approach

Create a focused `IArchiveFavoritesService` / `ArchiveFavoritesService` in `WebApp/WebApp/Services/`, registered as a singleton in `Program.cs`. It receives `IArchiveService` and `IOptions<ArchiveRootOptions>`, validates an opaque `(category, itemId)` through the archive service before persisting, and stores records in an archive-root-owned file such as `Dashboard/pereneArchiveFavorites.json`. The service—not a Razor component—owns file paths, a `SemaphoreSlim` lock, JSON deserialization, stale-record reconciliation, and temporary-file/atomic-move publication. It follows the existing `CustomStorageViewService` read/write failure model so browsing remains available if the optional state file cannot be read.

Use a browser-safe `ArchiveFavoriteRequest` model in `WebApp.Client/Models/` and extend the existing `ArchiveItemDto` with `IsFavorite`. `ArchiveEndpoints.List` obtains the server-side favorite IDs for its already-resolved listing and passes the flag into `ToDtoAsync` / `ToDto`; it must not serialize stored paths. Add a `PUT`/`DELETE` (or one idempotent toggle) favorite endpoint under the existing `/api/archive/{category}/items/{id}` route family. Endpoint code resolves the supplied ID through `IArchiveService`, maps existing archive exceptions through `ExecuteAsync`, and returns the updated browser-safe item/list state.

Add `ArchiveSortOption.Default`, make it the initial option in `ArchiveBrowser.razor`, and present it as a distinct dropdown choice. `ArchiveItemSorter.Sort` owns pure ordering: default groups favorites first, then applies the established folders-first/name ordering within each group. Name ascending/descending, Size, and Date retain their current behavior and ignore `IsFavorite`. The existing name filter remains before this sorter, naturally composing with the mode. The currently persisted legacy `Name` value should remain a valid explicit mode, avoiding a breaking local-storage parse path.

In `ArchiveBrowser.razor`, add the bookmark menu item to the single-item context menu, reuse its close-menu/error conventions, call the endpoint with only the opaque category/id, then replace/update the current listing and let `_filteredItems` recompute. The existing menu trigger is the sole visible card control: when `item.IsFavorite`, change its Bootstrap class from `btn-primary` (gold) to `btn-secondary` (green) or `btn-info` (blue), retaining 40px geometry and an explicit `aria-label` such as “Actions for X, favorite.” The menu label and icon change to make the state understandable without color. The selection toolbar and multi-item context menu do not gain favorite controls.

This keeps responsibilities narrow and testable: the persistence service can be direct-tested with temporary roots and a real `ArchiveService`; the pure sorter remains unit-tested; minimal APIs use `WebApplicationFactory`; the component performs HTTP/UI state only. No new package, background service, or infrastructure is required.

## Component Breakdown

**Existing files to modify:**

- `WebApp/WebApp/Program.cs` — register the favorite service singleton.
- `WebApp/WebApp/Endpoints/ArchiveEndpoints.cs` — map favorite mutation route(s), validate opaque IDs, and enrich list DTOs with favorite state.
- `WebApp/WebApp.Client/Models/ArchiveItemDto.cs` — add browser-safe `IsFavorite`.
- `WebApp/WebApp.Client/Models/ArchiveSortOption.cs` — add a distinct default sort option.
- `WebApp/WebApp.Client/Models/ArchiveItemSorter.cs` — implement favorite-first ordering only for the default option.
- `WebApp/WebApp.Client/Components/ArchiveBrowser.razor` — offer the single-item bookmark action, update the action-trigger color/accessible state, choose default ordering, and refresh state after mutation.
- `WebApp.Tests/Client/ArchiveItemSorterTests.cs` — test favorite-first default and normal explicit-sort results.
- `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs` — cover favorite endpoint/list contracts, rejected IDs, cross-category safety, and no path disclosure.
- `README.md` — add the supported favorites behavior to Archive management or Archive sort.

**New files to create:**

- `WebApp/WebApp/Services/IArchiveFavoritesService.cs` — narrow retrieval/toggle/reconciliation contract.
- `WebApp/WebApp/Services/ArchiveFavoritesService.cs` — archive-root JSON persistence and safe opaque-ID validation.
- `WebApp/WebApp.Client/Models/ArchiveFavoriteRequest.cs` — browser-safe request contract if the selected endpoint design needs a body.
- `WebApp.Tests/Services/ArchiveFavoritesServiceTests.cs` — persistence, concurrency, stale-data, and invalid-ID coverage using the existing temporary-archive test pattern.

## Dependencies

- The configured writable `ArchiveRoot:Path` and its existing `Dashboard` folder convention.
- Existing `IArchiveService` category/opaque-ID resolution and archive exception handling.
- The trusted private-LAN deployment model; all clients share the one host-mounted archive-root JSON file.
- Existing Docker Compose test workflow: `make test`.

## External / Vendor Documentation Evidence

Microsoft Learn MCP is unavailable in this session, so vendor-specific verification is pending. No new Microsoft API or package decision is proposed: the implementation reuses the repository's established ASP.NET Core minimal API, options, `System.Text.Json`, and Blazor HTTP patterns. The implementation must verify current .NET guidance before code changes if it changes those patterns.

The local [design guide](../20260827194328-perene-tech-design-system-refactor/design-guide-en.html) is the visual authority: it maps `btn-primary` to gold, `btn-secondary` to green, and `btn-info` to blue, and specifies Bootstrap dropdown/icon-control conventions. The favorite state will use one of the approved non-gold variants, never a bespoke color.

## Flow

```mermaid
sequenceDiagram
    participant User
    participant Browser as ArchiveBrowser.razor
    participant Endpoint as ArchiveEndpoints
    participant Favorites as ArchiveFavoritesService
    participant Archive as IArchiveService
    participant Store as pereneArchiveFavorites.json

    User->>Browser: Open actions menu; choose Favorite
    Browser->>Endpoint: Toggle with category + opaque item ID
    Endpoint->>Archive: Resolve current ID in category
    Archive-->>Endpoint: Resolved item or safe archive error
    Endpoint->>Favorites: Persist favorite state
    Favorites->>Store: Lock, write temp JSON, atomic move
    Favorites-->>Endpoint: Updated state
    Endpoint-->>Browser: Browser-safe favorite result/listing
    Browser->>Browser: Update item and recompute filtered/sorted view
```

## Risk Assessment

| Risk | Evidence | Mitigation |
| --- | --- | --- |
| Paths leak through favorite persistence/API | Archive uses opaque IDs; `ArchiveItemDto` is browser-safe and `ArchiveService` owns physical paths. | Persist server-only state, accept only category/opaque ID, assert endpoint responses do not contain the archive root. |
| Concurrent clients corrupt shared JSON | Multiple trusted devices can toggle a favorite at once. | Singleton service lock plus temp-file-and-atomic-replace, matching `CustomStorageViewService`. |
| Renames/moves invalidate path-derived opaque IDs | `ArchiveService.ComputeItemId` derives IDs from archive paths. | Reconcile unresolved records out of the store; explicitly defer identity migration to a later spec. |
| Favorite priority accidentally affects requested sorts | Existing sorter always groups folders before files and has no default/explicit distinction. | Add a separate `Default` option and unit-test every explicit sort ignores `IsFavorite`. |
| Color alone communicates favorite state | The design contract requires non-color cues. | Toggle menu text/icon, ARIA label/state, tooltip/title as appropriate, and preserve focusable 40px action control. |
