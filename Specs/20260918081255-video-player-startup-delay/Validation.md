# Validation: Video Player Startup Delay

## Table of Contents

- [Validation: Video Player Startup Delay](#validation-video-player-startup-delay)
  - [Acceptance Criteria](#acceptance-criteria)
  - [Test Cases](#test-cases)
  - [Manual Verification](#manual-verification)
  - [Definition of Done](#definition-of-done)
  - [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | `GET /api/videos/{id}` returns `200` with a `VideoItemDto` matching the entry's current thumbnail/hover-preview/subtitle/metadata state when the ID is resolvable, and `404` when it is not. |
| FR2 | Given a snapshot that already contains the requested ID, resolving it performs no filesystem walk (`Discover`/`ScanAsync` is not invoked). Given a snapshot that does not contain the ID, resolving it performs exactly one `ScanAsync` and then resolves (or 404s if still not found after that scan). |
| FR3 | Clicking a video under the `videos` category in the Archive Browser calls `GET /api/videos/{id}` (not `POST /api/videos/scan`) and selects the returned video; the existing `_error` messages still appear for a non-success response and for a missing/unresolvable item. |
| FR4 | `GET /api/videos` (collection) and `POST /api/videos/scan` continue to behave exactly as before this change (verified via existing tests in `WebApp.Tests/Endpoints/VideoEndpointsTests.cs` still passing unmodified for those cases). |
| FR5 | For a fresh video selection, `setVolume`/`setMuted`/`setPlaybackRate`/`setLoop`/`setSubtitlesEnabled` are each invoked exactly once (not twice) per selection. |
| FR6 | Resuming a previously-playing selection still seeks to `PlayerState.LastKnownTime` and resumes playback if `PlayerState.WasPlaying` was true, unchanged from current behavior. |
| FR7 | `_playbackError` is still reset on `HandleLoadedMetadataAsync` and still set on a media `onerror` event, unchanged from current behavior. |

## Test Cases

**Unit / integration tests (xUnit, `WebApp.Tests`, run only via `make test`):**

- `WebApp.Tests/Services/VideoLibraryServiceTests.cs`:
  - `ResolveAsync` returns the entry directly from an already-populated snapshot without triggering a rescan (assert via a scan-count spy/counter or by asserting no new files are discovered between two resolutions of the same known ID).
  - `ResolveAsync` triggers exactly one scan and resolves successfully when called with an ID that only exists after that scan (e.g. a file added to the temp test root before `ResolveAsync` is called for the first time on a service instance with an empty snapshot).
  - `ResolveAsync` returns `null` for a genuinely nonexistent ID after the scan-on-miss fallback runs once (does not loop or rescan repeatedly).
- `WebApp.Tests/Endpoints/VideoEndpointsTests.cs` (using the existing `WebApplicationFactory`-based pattern already in this file):
  - `GET /api/videos/{id}` returns `200` and a body matching the shape/fields of `GET /api/videos`'s list entries for a known ID.
  - `GET /api/videos/{id}` returns `404` for an unresolvable/malformed ID.
  - Existing `GET /api/videos` and `POST /api/videos/scan` test cases in this file continue to pass unmodified.
- `WebApp.Tests/Client/` (if a `Player`-related or `PersistentPlayerState`-related test file exists, extend it; otherwise add ⚠️ TODO): assert `PersistentPlayerState.SelectVideo` behavior is unaffected by the DTO now coming from a single-item fetch (the DTO shape is unchanged, so no new test should be strictly required beyond confirming existing tests still pass).

**Manual verification (Player.razor interop dedup — not practically unit-testable without a browser, since it depends on JS interop call counts):**
- ⚠️ TODO: no existing automated harness in this repo drives real `<video>` element JS interop; covered instead by the Manual Verification steps below.

## Manual Verification

Starting from a clean state using this repo's documented Docker workflow (`AGENTS.md` Execution Environment):

1. `make docker-run-bg` to start the stack, then `make get-url` to get the LAN URL and open it in a browser.
2. Navigate to the Video Library (`videos` category) with a reasonably sized library (multiple files, ideally on the configured `PERENE_ARCHIVE_ROOT` mount).
3. Click a video for the first time after a fresh container start. Confirm it plays (this exercises the scan-on-miss fallback in FR2) and note the load time.
4. Return to the library and click a **different** video. Confirm it opens noticeably faster than before the fix, and open browser DevTools → Network to confirm the request is `GET /api/videos/{id}` (not `POST /api/videos/scan`).
5. Click the same video a third time; confirm it still opens fast and correctly.
6. With DevTools open on the `Console`, temporarily add a `console.trace()` (or use a debugger breakpoint) inside `setVolume`/`setSubtitlesEnabled` in `WebApp/WebApp.Client/wwwroot/js/videoEditor.js` to confirm each fires exactly once per video selection instead of twice; remove the temporary instrumentation afterward.
7. Set a non-default volume, mute state, playback rate, and (for a video with subtitles) enable subtitles. Select a different video, then select back — confirm all four preferences are still correctly applied to the new element.
8. Start playing a video, seek partway through, and either navigate away and back to it or use a playlist's "next"/"previous" control to leave and return to it — confirm playback resumes at the correct time and (if it was playing) automatically resumes playing.
9. Trigger the A/B loop → Save Cut flow on a video selected via the Video Library to confirm `PlayerState.CanSaveCut` and `POST /api/videos/{id}/cuts` still work with IDs returned by the new `GET /api/videos/{id}` endpoint.
10. Select a video in a **non-`videos`** archive category (e.g. a general folder with a video file) to confirm `SelectArchiveVideo`'s unrelated code path is unaffected.
11. `make test` to run the full automated suite in the isolated Docker Compose stack and confirm it passes.

## Definition of Done

- Requirements, Plan, and Validation docs in this spec folder are complete and consistent with the implementation.
- All existing tests in `WebApp.Tests` still pass via `make test`.
- New tests exist for `VideoLibraryService.ResolveAsync` and `GET /api/videos/{id}` per the Test Cases above.
- Manual verification steps 1–11 above have been performed and confirmed.
- No physical or root-relative filesystem path is exposed to the browser or logged by the new endpoint (spot-checked in the `GET /api/videos/{id}` response body and server logs during manual verification).
- `README.md`'s `## Current Supported Features` table is reviewed; since this spec is a performance/internal-correctness fix with no new user-facing feature or supported format, no row addition is expected, but confirm no existing row needs a wording update.

## Rollback Plan

- The change is additive and localized: revert the four modified files (`IVideoLibraryService.cs`, `VideoLibraryService.cs`, `VideoEndpoints.cs`, `ArchiveBrowser.razor`, `Player.razor`) via `git revert` of the implementing commit(s).
- No configuration flag, migration, or persisted data is introduced, so rollback requires no data cleanup — the new `GET /api/videos/{id}` route simply stops being mapped, and `ArchiveBrowser.SelectVideoAsync` reverts to the previous `POST /api/videos/scan` + client-side search behavior.
