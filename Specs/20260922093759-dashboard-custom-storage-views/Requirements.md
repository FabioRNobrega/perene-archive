# Requirements: Dashboard Custom Storage Views

## Table of Contents

- [Requirements: Dashboard Custom Storage Views](#requirements-dashboard-custom-storage-views)
  - [Problem Statement](#problem-statement)
  - [User Stories](#user-stories)
  - [Functional Requirements](#functional-requirements)
  - [Non-Functional Requirements](#non-functional-requirements)
  - [Out of Scope](#out-of-scope)
  - [Open Questions](#open-questions)

## Problem Statement

The Storage card (`WebApp/WebApp.Client/Components/Dashboard/DashboardStorageCard.razor`) only ever shows one "Capacity" progress bar, backed by `IStorageUsageService.GetUsage()` (`WebApp/WebApp/Services/StorageUsageService.cs`), which reports total/used bytes for the whole filesystem behind `ArchiveRootOptions.Path`. There is no way to see how much of that capacity a specific archive folder (e.g. one category, or a subfolder inside one) is consuming against a size the user cares about for that folder specifically. The user wants to pick any folder under the archive root through the existing opaque-ID archive browsing model, assign it a display-only "max size" of their own choosing, and see a second (and third, and so on) progress row appended below "Capacity" that tracks that folder's actual on-disk size against that user-chosen ceiling — persisted so it survives a page reload, and manageable (add/remove/edit) from the Storage card itself.

## User Stories

- Given the Storage card is open, when the user clicks the small "+" button next to "Capacity", then a modal opens letting them drill into archive categories and folders (mirroring the existing Move column-picker pattern) to pick one folder.
- Given the user has picked a folder in the modal, when they enter a max size in GB and confirm, then a new row appears below "Capacity" showing that folder's name as a title, its current on-disk size, and the user-chosen max, rendered with the same progress-bar styling and threshold coloring as "Capacity".
- Given one or more custom storage views already exist, when the user reloads the Dashboard page (or reopens the browser), then the same custom rows reappear with up-to-date current sizes, because they were saved server-side.
- Given a custom storage row is visible, when the user wants to stop tracking that folder, then they can remove it directly from the row and it disappears from the card (and from what's persisted) without affecting "Capacity" or any other custom row.
- Given a custom storage row is visible, when the user wants to change only the max-size ceiling for that folder, then they can edit that value in place without having to remove and re-pick the folder.
- Given the folder backing a saved custom storage view has since been deleted, moved, or renamed, when the Storage card refreshes, then that row shows an unavailable/error state instead of a stale or crashing size read, and the user can still remove it.

## Functional Requirements

1. FR1 — `DashboardStorageCard.razor` renders a small icon-only "+" button next to the "Capacity" label (Bootstrap Icons, `currentColor`, 40×40 minimum target, accessible name, tooltip), which opens a folder-picker modal.
2. FR2 — The folder-picker modal reuses the same category → folder drill-down pattern as the archive Move dialog (`Specs/20260914154043-archive-move-column-picker/`): a leftmost column lists move-eligible-style archive categories (`ArchiveCategory.Defaults`, at minimum every category with `CanCreateFolder == true`), and selecting a category or folder loads its folder-kind children via the existing `GET /api/archive/{category}/items?folderId=` listing filtered to `Kind == Folder`. The archive root itself (all categories combined) may also be offered as a selectable "whole archive" option.
3. FR3 — After selecting a folder (or the whole archive) in the modal, the user enters a positive numeric max size in GB before confirming; confirming with no folder selected or a non-positive/blank size is prevented client-side with inline validation feedback.
4. FR4 — Confirming the modal calls a new server endpoint that resolves the selected category + opaque folder id to a physical path the same way `IArchiveService`/`ArchiveService.ResolveFolder` already does, computes a stable identifier for it, and persists a new custom storage view record (category key, opaque folder id or whole-archive marker, display title, and the user's max-size bytes) via a new server-side service, then returns the current list of custom storage views (each with a freshly computed current size) so the client can render immediately without a second round trip.
5. FR5 — `DashboardStorageCard.razor` renders one additional progress row per persisted custom storage view below "Capacity", each with: the folder's display title, current-size-of-max text, and a `progress`/`progress-bar` element using the same `ThresholdColor` helper and Bootstrap markup pattern as the existing Capacity row (`DashboardStorageCard.razor:20-29`).
6. FR6 — Each custom storage row includes a small remove control that calls a new server endpoint to delete that persisted view by its identifier, then removes the row from the rendered list.
7. FR7 — Each custom storage row includes an edit control that lets the user change only the max-size value (not the folder) for that view, saving the updated value server-side and reflecting it immediately in the row's progress bar and text.
8. FR8 — A new server-side service computes a custom storage view's current size as the recursive sum of file sizes under its resolved folder (or the whole archive root when that option was chosen), following the same recursive-enumeration, symlink/reparse-point-skipping, and non-fatal-`IOException`/`UnauthorizedAccessException`-handling approach already used by `ArchiveMetricsService.CountFiles()` (`WebApp/WebApp/Services/ArchiveMetricsService.cs`); this size is (re)computed synchronously whenever the Storage card is refreshed (initial load and the dashboard's existing manual refresh button), not cached across requests.
9. FR9 — Custom storage views are persisted as a JSON file written under `ArchiveRootOptions.Path` using the same temp-file-then-atomic-move pattern as `EpubProgressService` (`WebApp/WebApp/Services/EpubProgressService.cs:84-97`), so the list survives process restarts and is shared across any browser/device pointed at this instance.
10. FR10 — If a persisted custom storage view's folder can no longer be resolved (deleted, moved outside its category root, or the category itself became invalid), the server marks that view's `IsAvailable` as `false` in the response instead of throwing, and `DashboardStorageCard.razor` renders that row in an unavailable state (matching the existing "Storage metrics unavailable" treatment) while still offering the remove control.
11. FR11 — The endpoints backing FR4/FR6/FR7 only ever accept/return opaque category keys and folder ids (the same shape already used by `GET /api/archive/{category}/items`); no physical or root-relative filesystem path is ever sent to or received from the browser.

## Non-Functional Requirements

- No physical or root-relative filesystem path is ever exposed to the browser or written to normal application logs, consistent with the existing archive opaque-ID boundary.
- Folder resolution and size computation stay server-side in a new focused service (not inline in the endpoint or in `ArchiveService`'s existing responsibilities), following the existing single-responsibility service pattern (`IStorageUsageService`, `IArchiveMetricsService`, `IEpubProgressService`).
- The new endpoints follow the existing minimal-API grouping convention (`MapDashboardEndpoints` in `WebApp/WebApp/Endpoints/DashboardEndpoints.cs`, or a new `MapCustomStorageEndpoints` alongside it) rather than being added ad hoc elsewhere.
- All new UI is built from Bootstrap components/utilities and Bootstrap Icons per `AGENTS.md`'s design-system constraints; no hand-authored SVGs or bespoke color CSS.
- The folder-picker modal must remain usable at phone width, matching the existing column-picker's horizontal-scroll behavior.
- Persistence writes must not corrupt the JSON file under concurrent add/remove/edit calls (use the same single-writer lock pattern as `EpubProgressService`).
- Recursive size scans must not block other dashboard cards' refreshes indefinitely; large archive folders may be slow, and this is an accepted tradeoff of the "recompute on every refresh" approach (see Open Questions for a future caching alternative).

## Out of Scope

- Enforcing the user-chosen max size as an actual disk quota — it is visual/display-only, exactly as the user specified.
- Automatically discovering or suggesting folders to track; the user must always pick explicitly via the modal.
- Background/periodic recomputation of custom storage view sizes independent of a dashboard refresh.
- Any change to `IStorageUsageService`'s existing whole-filesystem "Capacity" row or its throughput/health fields.
- Multi-user or per-browser scoping of which custom storage views are visible — persistence is instance-wide, matching how the rest of the app has no auth/user isolation layer.

## Open Questions

- ⚠️ TODO: Confirm an acceptable soft limit (if any) on how many custom storage views a user may add, given each one triggers a recursive filesystem scan on every dashboard refresh. Default assumption for the plan: no hard limit, but the UI should not visually break with several rows.
- ⚠️ TODO: Confirm the exact display title used per row when "whole archive" is selected as the tracked folder (proposed default: "Archive").
