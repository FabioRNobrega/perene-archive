# Requirements: Video Player Startup Delay

## Table of Contents

- [Requirements: Video Player Startup Delay](#requirements-video-player-startup-delay)
  - [Problem Statement](#problem-statement)
  - [User Stories](#user-stories)
  - [Functional Requirements](#functional-requirements)
  - [Non-Functional Requirements](#non-functional-requirements)
  - [Out of Scope](#out-of-scope)
  - [Open Questions](#open-questions)

## Problem Statement

Clicking a video in the Video Library (Archive Browser with `Category == "videos"`) currently feels slow to open the player. Two independent root causes were diagnosed:

1. `ArchiveBrowser.SelectVideoAsync` (`WebApp/WebApp.Client/Components/ArchiveBrowser.razor`) awaits `POST /api/videos/scan` before it can select the clicked item. That endpoint (`VideoEndpoints.ScanAsync` → `VideoLibraryService.ScanAsync` → `Discover`) recursively walks the **entire** configured video root, opens every discovered file once to confirm readability, and rebuilds a `VideoItemDto` (thumbnail/hover-preview/subtitle/metadata state) for **every** file in the library — not just the one the user clicked. The browser then does a client-side linear search through that full list to find the clicked item by ID. This is O(library size) work on the critical path of a single click, and it is currently the *only* code path that ever populates `VideoLibraryService`'s in-memory snapshot for the `videos` category — there is no lighter-weight way today to resolve one video without paying for a full scan.
2. `Player.razor` calls `ApplyPlayerPreferencesAsync` (5 sequential JS interop round-trips: `setVolume`, `setMuted`, `setPlaybackRate`, `setLoop`, `setSubtitlesEnabled`, plus a `SynchronizeMediaStateAsync` snapshot read) twice per video selection — once eagerly in `OnAfterRenderAsync` right after the new `<video>`/`<audio>` element mounts, and again in `HandleLoadedMetadataAsync` when the browser fires the native `loadedmetadata` event. The second call is redundant: it repeats the same preference-application interop work that already ran, coupling "apply the user's persisted playback preferences" together with "resume the last known position and play state" into a single method that both call sites depend on.

## User Stories

- Given a Video Library with many files, when a user clicks a video to play it, then the player opens without waiting for a full library rescan, and the click cost does not grow with library size (after the library has been scanned at least once).
- Given a freshly started application (or a library whose snapshot has never been populated), when a user clicks a video for the first time, then the video still resolves and plays correctly (a one-time scan is acceptable here, but not on every subsequent click).
- Given a user has previously set volume/mute/playback-rate/loop/subtitle preferences, when they select a new video, then those preferences are still applied to the new element exactly once, with no duplicated JS interop round-trips.
- Given a user was previously watching a video and resumes it (e.g. selecting the same item again, or a playlist "next/previous" transition), when the new element's metadata loads, then playback still resumes at the last known time and play state, unchanged from current behavior.

## Functional Requirements

1. FR1 — Add `GET /api/videos/{id}` to `VideoEndpoints` (`WebApp/WebApp/Endpoints/VideoEndpoints.cs`), returning a single `VideoItemDto` for an opaque video ID resolved against `IVideoLibraryService`'s current snapshot, reusing the existing `BuildDto` helper. Return 404 when the ID does not resolve (mirroring the existing `TryResolve`-based pattern used by `GET /api/videos/{id}/thumbnail|preview|subtitle` and `GET /api/videos/{id}/stream`).
2. FR2 — `IVideoLibraryService`/`VideoLibraryService` resolves a requested ID against the current in-memory snapshot first; only if the ID is not found does it perform one on-demand `ScanAsync` refresh and retry the resolve before reporting not-found. This keeps the full recursive filesystem walk out of the common case (library already scanned) while still self-healing on cold start or when a file was added after the last scan, without requiring a separate "warm the cache" trigger elsewhere in the app.
3. FR3 — `ArchiveBrowser.SelectVideoAsync` (`WebApp/WebApp.Client/Components/ArchiveBrowser.razor`) calls `GET /api/videos/{id}` instead of `POST /api/videos/scan`, removing the client-side linear search through the full video list. On a 404 response it keeps showing the existing "The selected file is not available to the video player yet." error.
4. FR4 — The existing `GET /api/videos` (collection) and `POST /api/videos/scan` (explicit rescan) endpoints are unchanged in behavior and continue to serve their current callers/use cases.
5. FR5 — `Player.razor` applies persisted playback preferences (`setVolume`, `setMuted`, `setPlaybackRate`, `setLoop`, `setSubtitlesEnabled`) exactly once per video selection, from the pending-media-initialization branch of `OnAfterRenderAsync`.
6. FR6 — `Player.razor` keeps the resume-last-position/play-state behavior (seeking to `PlayerState.LastKnownTime` and resuming playback if it was playing) driven from `HandleLoadedMetadataAsync`, decoupled from preference application — `HandleLoadedMetadataAsync` no longer re-invokes the preference-application step.
7. FR7 — Playback error handling (`ShowPlaybackError`, `_playbackError` reset on `HandleLoadedMetadataAsync`) is preserved unchanged.

## Non-Functional Requirements

- Performance: selecting an already-known video (the common case, library scanned at least once) must not perform a recursive filesystem walk or per-file readability probe of the whole library; it must be a single opaque-ID snapshot lookup.
- No physical or root-relative filesystem path may be exposed to the browser or logged, consistent with the existing opaque-ID boundary (`AGENTS.md` Constraints).
- `VideoLibraryService` remains the sole owner of scanning and snapshot-resolution logic (single responsibility); `VideoEndpoints.GetVideoById` stays a thin HTTP adapter that reuses the existing `BuildDto` DTO-construction logic rather than duplicating it.
- No behavior change to the A/B loop / Save Cut feature, which depends on `PlayerState.CanSaveCut` requiring `StreamBasePath == VideoStreamBasePath` — the new endpoint must keep returning IDs resolvable by `POST /api/videos/{id}/cuts`.
- No new runtime dependency, background service, or JS interop function is introduced.

## Out of Scope

- Changing how `VideoLibraryService.Discover` walks the filesystem or how thumbnails/hover-previews/subtitles are generated.
- Removing or changing `POST /api/videos/scan` (still used for explicit rescans) or `GET /api/videos` (collection listing).
- Any change to the Archive category flow for non-`videos` categories (`SelectArchiveVideo`), which already resolves without a scan.
- Server-side or transport-level video transcoding/streaming changes (range processing, buffering, container/codec compatibility such as `moov` atom placement).
- Changing where/when the very first scan of a cold `VideoLibraryService` snapshot happens beyond the self-healing fallback in FR2.

## Open Questions

- ⚠️ TODO: none outstanding — scope and approach were confirmed with the user (single-item REST endpoint per SOLID/REST conventions; decouple preference-application from resume behavior while keeping resume working).
