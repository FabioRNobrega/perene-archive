# Requirements: Shared Archive Favorites

## Table of Contents

- [Problem Statement](#problem-statement)
- [User Stories](#user-stories)
- [Functional Requirements](#functional-requirements)
- [Non-Functional Requirements](#non-functional-requirements)
- [Out of Scope](#out-of-scope)
- [Open Questions](#open-questions)

## Problem Statement

`ArchiveBrowser.razor` can currently filter and sort the current folder, but it has no durable way to mark a useful file or folder for quick access. The existing sort preference is intentionally browser-local, which cannot meet the requested shared behavior across browsers and trusted LAN devices. Users need a server-owned favorite marker, stored in the archive, that promotes selected items only in the default archive view while leaving explicit sort modes unchanged.

## User Stories

- Given an archive item card, when I use its actions menu and choose Favorite, then it is marked with a bookmark and the change is available from another browser/device.
- Given a favorite item in a folder, when I use the default ordering, then it appears before non-favorite items in that folder.
- Given I explicitly sort by Name A–Z, Name Z–A, Size, or Date, when the listing updates, then favorites participate in the selected ordering exactly like every other item.
- Given I filter a folder in the default view, when matching favorite and non-favorite items remain, then matching favorites still appear first.
- Given an item is already a favorite, when I choose the same menu action, then it is removed from favorites and its menu button returns to the normal color.

## Functional Requirements

1. FR1 — `ArchiveBrowser.razor` exposes a single-item `Favorite` / `Remove from favorites` action, with `<i class="bi bi-bookmark">` / the corresponding filled state, in the existing per-card actions/context menu for both archive files and folders; multi-selection menus remain unchanged.
2. FR2 — The per-card actions button changes from its normal gold primary presentation to an approved green (`btn-secondary`) or blue (`btn-info`) Bootstrap presentation while its item is a favorite, providing a non-color accessible action label that identifies the current favorite state.
3. FR3 — A new server-owned favorite service persists the shared favorite set as atomically published JSON beneath the configured archive root, using the same locking and malformed-file resilience pattern as `CustomStorageViewService`; it is readable and writable by every trusted client through the web application, not browser local storage.
4. FR4 — Favorite requests accept and return only the current archive category and opaque item IDs. Physical paths and root-relative paths must not be included in browser DTOs, client requests, API errors, or routine logs.
5. FR5 — The archive listing endpoint enriches each `ArchiveItemDto` with an `IsFavorite` value resolved server-side from the favorite store, so a fresh listing from any browser shows the shared state without a separate client-owned cache.
6. FR6 — A toggle endpoint resolves the submitted opaque ID through `IArchiveService` in its stated category before changing persistence; unknown, malformed, or cross-category IDs receive the existing safe not-found/validation behavior and do not create a favorite record.
7. FR7 — The Archive Browser adds a distinct default sort mode and uses it initially. In that mode, `ArchiveItemSorter` places favorite items before non-favorites, preserving the existing folder/file and case-insensitive name ordering within each favorite group.
8. FR8 — Any explicit sort selection—Name A–Z, Name Z–A, Size, or Date—uses the existing sorting rules and does not give favorites priority. The selected explicit mode remains browser-local through the existing sort-preference storage behavior.
9. FR9 — Name filtering composes with ordering: in default mode it shows only name matches with favorites first; in explicit sort modes it shows only name matches in the ordinary selected order.
10. FR10 — A successful favorite toggle updates the currently rendered card/list ordering immediately without requiring navigation or a full page reload. Failed requests leave the known favorite state and ordering unchanged and use the component's existing accessible error region.
11. FR11 — The JSON store removes records that can no longer be resolved when it next reconciles an archive listing; removal, rename, move, and Trash behavior must not expose stale favorites to clients or prevent the existing archive mutation flows.
12. FR12 — `README.md`'s Current Supported Features table documents shared archive favorites and their default-versus-explicit-sort behavior.

## Non-Functional Requirements

- Extend the existing server/client split: server-only services own filesystem paths and JSON; `WebApp.Client/Models` contains browser-safe DTO/request contracts; `ArchiveBrowser.razor` owns rendering and interaction.
- Reuse Bootstrap 5.3 components/utilities and Bootstrap Icons. The favorite actions icon is a minimum 40×40 CSS-pixel control with an accessible name, visible focus treatment, and a programmatic pressed/state cue; do not hand-author SVG or add a frontend package.
- The favorite action must follow the design guide: gold remains the principal primary action color; a favorited card action uses the existing green `btn-secondary` or blue `btn-info` semantic-safe brand variant, with text/ARIA state so color is not the sole cue. Both dark and Kindle-paper themes must retain WCAG AA normal-text contrast.
- JSON writes are serialized in-process and use a temporary file followed by atomic replacement. A missing, malformed, inaccessible, or transiently unreadable favorites file must not prevent archive browsing; it starts/continues with an empty safe set and records no path-bearing error.
- New service and sorting behavior are covered by focused xUnit tests; endpoint tests verify shared state, rejected IDs, and absence of the archive root in responses. All tests run through `make test`.

## Out of Scope

- A separate Favorites page, sidebar destination, cross-folder aggregate search, tags, folders of favorites, or user accounts/permissions.
- Browser-local-only favorites, cloud synchronization, and changes to the private trusted-LAN/no-authentication model.
- Bulk favorite/unfavorite operations, drag-and-drop reordering, or a card-level second bookmark control.
- Changing how explicit Name, Size, Date, or descending sorts rank normal files and folders.
- Automatically preserving a favorite through rename, move, cross-category move, or Trash/restore in this first slice.

## Open Questions

- ⚠️ TODO: Favorites will initially be keyed by the current category plus its opaque, path-derived item ID and stale entries will be reconciled away. Decide in a later spec whether a favorite must survive rename/move/Trash; that requires mutation-aware identity migration rather than the current opaque-ID model.
