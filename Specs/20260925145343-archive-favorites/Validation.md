# Validation: Shared Archive Favorites

## Table of Contents

- [Acceptance Criteria](#acceptance-criteria)
- [Test Cases](#test-cases)
- [Manual Verification](#manual-verification)
- [Definition of Done](#definition-of-done)
- [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | A single file and folder each show an accessible Favorite action in their actions/context menu; an already favorite item offers Remove from favorites; multi-select menus remain Move-only. |
| FR2 | The favorite item action trigger uses an approved green or blue Bootstrap variant, while non-favorite uses the normal gold variant; text/ARIA describes the state. |
| FR3 | Favoriting in one browser persists an atomic JSON record and is visible after a fresh listing from a second browser/device. |
| FR4 | Favorite requests and responses contain category/opaque IDs only; no physical/root-relative archive path appears in body or error output. |
| FR5 | A normal `GET /api/archive/{category}/items` returns `IsFavorite: true` for persisted, currently resolvable favorites. |
| FR6 | Unknown, malformed, and cross-category IDs cannot change the favorite store and receive safe existing error behavior. |
| FR7 | The initial/default mode places favorites first, with the existing folder/file/name ordering preserved inside favorite and non-favorite groups. |
| FR8 | Explicit Name A–Z, Name Z–A, Size, and Date sorting produces the same relative results whether items are favorite or not. |
| FR9 | Filtering in default mode retains favorite-first ordering among name matches; explicit sorts remain ordinary among matches. |
| FR10 | A successful toggle immediately updates card color/menu state and ordering; a failed request keeps prior rendering and reports the existing accessible error. |
| FR11 | Deleted/unresolvable favorite records do not appear in listings and are safely removed at reconciliation; normal mutation workflows still work. |
| FR12 | `README.md` describes the shared favorites behavior. |

## Test Cases

**Unit tests:**

- `WebApp.Tests/Services/ArchiveFavoritesServiceTests.cs` — adding/removing a resolvable file and folder persists/reloads the shared JSON; rejected IDs do not write; malformed JSON recovers to an empty set; stale records are pruned; concurrent toggles leave valid JSON.
- `WebApp.Tests/Client/ArchiveItemSorterTests.cs` — default mode returns favorites first while keeping the established tie-breakers; Name A–Z, Name Z–A, Size, and Date each ignore favorite status; empty and filtered input behavior remains safe.
- Existing model tests compile with the new `ArchiveItemDto.IsFavorite` optional/default field and no path-bearing property.

**Integration tests:**

- `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs` — with `VideoManagerFactory`, favorite a listed file/folder through the API, fetch a new listing, and verify its `IsFavorite`; recreate a client/factory as appropriate to demonstrate archive-root persisted state.
- `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs` — submit invalid and cross-category IDs and assert not-found/validation status, unchanged listing/store, and no temporary-root path in bodies.
- ⚠️ TODO: Add a component-level browser test if the project later adopts a browser test harness; today the repository uses focused state/model and endpoint xUnit tests plus manual UI validation.

## Manual Verification

1. Create a clean test archive root with at least two folders and two files in an archive category; start the app using `make docker-run`.
2. Open the archive category in one trusted browser. Confirm the new default ordering is active and the current gold actions triggers are visible.
3. Open a file's actions menu, choose Favorite, and confirm its trigger becomes green or blue, menu wording/icon changes, focus remains usable, and the card moves to the default-view favorite group.
4. Repeat for a folder, filter by a matching name, and confirm matching favorites remain first only in the default mode.
5. Select each explicit Name A–Z, Name Z–A, Size, and Date option. Confirm the favorite file/folder follows the ordinary documented order for that option.
6. In a second browser profile/device connected to the same app, load the same folder and confirm favorite state and default ordering match. Remove the favorite there and confirm it disappears after a refresh in the first browser.
7. Attempt a browser/dev-tools request with an unknown or cross-category ID. Confirm it fails without disclosing a host path or changing favorites.
8. Rename, move, and Trash a favorite item. Confirm the ordinary mutation completes and that stale favorite state does not render after the next listing reconciliation.
9. Run `make test` and confirm the isolated Docker Compose suite passes.

## Definition of Done

- Requirements, Plan, and Validation documents are complete in this spec folder.
- JSON persistence uses the documented archive-root ownership, locking, malformed-file handling, and atomic publish behavior.
- `ArchiveItemDto`, endpoints, and the Razor component preserve the opaque-ID/path privacy boundary.
- Default and every explicit sort mode have focused test coverage; server persistence and API behavior have service/endpoint coverage.
- The actions menu and trigger satisfy design-guide Bootstrap/Icon, responsive, dark/light, visible-focus, accessible-name, and non-color-state requirements.
- `README.md`'s Current Supported Features table is updated.
- `make test` passes in the documented Docker Compose workflow.
- Vendor-specific evidence remains marked pending until Microsoft Learn verification is available.

## Rollback Plan

- Revert the favorite endpoint mappings in `ArchiveEndpoints.cs`, the singleton registration in `Program.cs`, the favorite service/model files, and the `ArchiveBrowser.razor` UI/sorter changes as one feature slice.
- The archive-root JSON file is additive state only; leaving `Dashboard/pereneArchiveFavorites.json` in place is harmless after rollback, or an operator may retain it for a later re-enable. No archive content migration or destructive cleanup is required.
