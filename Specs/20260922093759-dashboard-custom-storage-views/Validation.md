# Validation: Dashboard Custom Storage Views

## Table of Contents

- [Validation: Dashboard Custom Storage Views](#validation-dashboard-custom-storage-views)
  - [Acceptance Criteria](#acceptance-criteria)
  - [Test Cases](#test-cases)
  - [Manual Verification](#manual-verification)
  - [Definition of Done](#definition-of-done)
  - [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | The Storage card shows a "+" icon button next to "Capacity" with an accessible name, tooltip, and a 40×40 CSS-pixel minimum target; activating it opens the folder-picker modal. |
| FR2 | The modal's leftmost column lists `ArchiveCategory.Defaults` categories (plus a "Whole Archive" option); selecting a category or folder loads its folder-kind children via `GET /api/archive/{category}/items?folderId=`, matching the Move dialog's column behavior. |
| FR3 | Confirming with no folder selected, or with a blank/zero/negative max-size value, is blocked client-side with visible inline feedback; the request is not sent. |
| FR4 | Confirming a valid selection calls `POST /api/dashboard/storage/custom`, which persists the view and returns the current list of views (each with a freshly computed size), rendered without a page reload. |
| FR5 | Below "Capacity", one additional progress row per persisted view appears, using the same `progress`/`progress-bar`/`ThresholdColor` markup pattern as the Capacity row, with the folder's title and "used of max" text. |
| FR6 | Clicking a row's remove control calls `DELETE /api/dashboard/storage/custom/{viewId}` and the row disappears from the card and from the persisted JSON file. |
| FR7 | Editing a row's max size in place calls `PATCH /api/dashboard/storage/custom/{viewId}` and the row's progress bar/text update to the new max without changing the tracked folder. |
| FR8 | A view's `UsedBytes` equals the recursive sum of file sizes under its resolved folder (or the whole archive root), recomputed on every Storage card refresh, using the same symlink-skipping/non-fatal-exception approach as `ArchiveMetricsService.CountFiles()`. |
| FR9 | The persisted list survives an application restart: values written via `CustomStorageViewService` are read back correctly from the JSON file under `ArchiveRootOptions.Path` after a fresh process start, using the same temp-file-then-atomic-move pattern as `EpubProgressService`. |
| FR10 | When a persisted view's folder can no longer be resolved, `GET /api/dashboard/storage/custom` returns that view with `IsAvailable = false` (not an error), and the card renders it in an unavailable state while its remove control still works. |
| FR11 | Inspecting requests/responses for all four `/api/dashboard/storage/custom*` endpoints shows only opaque category keys/folder ids/view ids — no physical or root-relative path appears in any request body, response body, or application log. |

## Test Cases

**Unit tests:**

- `WebApp.Tests/Services/CustomStorageViewServiceTests.cs`:
  - Add → persisted JSON contains the new record with a generated view id, opaque `categoryKey`/`folderId` (or `IsWholeArchive`), and the given max bytes.
  - Add with an unresolvable `(categoryKey, folderId)` throws/returns a validation failure and nothing is persisted.
  - GetAll computes `UsedBytes` as the recursive file-size sum for a folder with nested subfolders and a symlink (symlink excluded), following the `ArchiveMetricsService.CountFiles()` fixture style already in `WebApp.Tests/Services/ArchiveMetricsServiceTests.cs`.
  - GetAll marks a view `IsAvailable = false` when its folder id no longer resolves, without throwing for the remaining views.
  - Remove deletes only the targeted view id; other persisted views are untouched.
  - UpdateMaxSize changes only `MaxBytes` for the targeted view; `UsedBytes`/folder reference are unchanged.
  - Concurrent Add/Remove/UpdateMaxSize calls (simulated via parallel `Task.WhenAll`) never corrupt the JSON file — the final read back is valid JSON with all expected mutations applied, matching the concurrency guarantee `EpubProgressService` already relies on.

**Integration tests:**

- `WebApp.Tests/Endpoints/DashboardEndpointsTests.cs` (extended): using `WebApplicationFactory` against a temp `ArchiveRootOptions.Path` fixture (same setup style already used for the existing storage/archive endpoint tests), verify:
  - `POST /api/dashboard/storage/custom` with a valid folder selection returns 200 and includes the new view.
  - `POST /api/dashboard/storage/custom` with an invalid `folderId` returns 400/404 without writing a file.
  - `DELETE /api/dashboard/storage/custom/{viewId}` for an unknown id returns 404.
  - `PATCH /api/dashboard/storage/custom/{viewId}` updates and returns the new max size.
  - `GET /api/dashboard/storage/custom` after deleting the backing folder on disk returns `IsAvailable = false` for that view instead of a 500.

## Manual Verification

1. `make docker-run-bg` to start the stack against a populated `PERENE_ARCHIVE_ROOT`.
2. Open the Dashboard, confirm the Storage card still shows "Capacity" as before.
3. Click the new "+" button; confirm the modal opens with categories in the leftmost column.
4. Drill into a category, select a folder with known contents, enter a max size (e.g. 50 GB), confirm.
5. Confirm a new row appears below "Capacity" with the folder's name, a size close to its actual on-disk usage, and a progress bar filled proportionally to the entered max.
6. Reload the browser tab; confirm the same row reappears with a freshly computed size.
7. Click the row's edit control, change the max size, confirm the bar/text update without changing the tracked folder.
8. Click the row's remove control; confirm the row disappears and does not reappear after another reload.
9. Add a view for a folder, then delete/rename that folder on the host filesystem outside the app; refresh the Dashboard and confirm the row shows an unavailable state rather than an error, and can still be removed.
10. `make test` to confirm the full suite (including the new/extended test files above) passes in the isolated Docker Compose test stack.

## Definition of Done

- Requirements, Plan, and Validation docs in this spec folder are complete and internally consistent.
- All existing tests still pass under `make test`.
- New behavior has unit coverage (`CustomStorageViewServiceTests`) and endpoint coverage (extended `DashboardEndpointsTests`) matching this repo's existing xUnit/`WebApplicationFactory` conventions.
- `DashboardStorageCard.razor` and the new `CustomStorageFolderPicker.razor` use Bootstrap components/utilities and Bootstrap Icons only, with responsive, empty/loading/unavailable, and accessibility states covered as described above.
- No vendor-specific API decisions requiring fresh Microsoft Learn verification were introduced (see Plan.md § External / Vendor Documentation Evidence).
- `README.md`'s `## Current Supported Features` table is updated with a row for custom storage views, per `AGENTS.md`'s requirement to update it whenever a spec adds a user-facing feature.

## Rollback Plan

- The feature is additive: removing the new `/api/dashboard/storage/custom*` routes from `MapDashboardEndpoints` and the new UI from `DashboardStorageCard.razor` fully reverts visible behavior; the existing "Capacity" row and all other dashboard cards are untouched by this spec.
- If a bad persisted JSON file ever caused a startup or read issue, `CustomStorageViewService`'s read path degrades the same way `EpubProgressService.ReadAllUnlockedAsync` does — catching `JsonException`/`IOException`/`UnauthorizedAccessException`/`FormatException` and returning an empty list — so deleting or renaming the JSON file under the archive root (e.g. `Dashboard/pereneArchiveCustomStorageViews.json`) is sufficient to reset to zero custom views without any code change or redeploy.
