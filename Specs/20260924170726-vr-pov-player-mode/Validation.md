# Validation: VR POV Player Mode

## Table of Contents

- [Validation: VR POV Player Mode](#validation-vr-pov-player-mode)
  - [Acceptance Criteria](#acceptance-criteria)
  - [Test Cases](#test-cases)
  - [Manual Verification](#manual-verification)
  - [Definition of Done](#definition-of-done)
  - [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | The VR POV button (`bi-badge-vr`) renders in `MediaPlayerControls.razor`'s toolbar for every selected non-music video, in both the Video Library and Archive Browser usages of `Player.razor`, with no per-item classification gate. |
| FR2 | Clicking the button toggles `VrPovState.IsActive`; `aria-pressed`, the active toggle CSS class, and the title/aria-label text all reflect the current state, matching the existing Fill-tab/Fullscreen button behavior. |
| FR3 | With POV active, the `<canvas>` is visible and the `<video>` is visually hidden but still present/playing in the DOM; with POV inactive, the reverse is true. |
| FR4 | `vrPovRenderer.js` is loaded via the same `IJSObjectReference` import pattern as `videoEditor.js`; no per-frame pixel data is passed through `InvokeAsync`/`InvokeVoidAsync` (only element references and start/stop/resize commands). |
| FR5 | The rendered POV image visibly shows only the left half of the source frame (side-by-side left eye), verified against any side-by-side VR180 reference video. |
| FR6 | The rendered image is a curved (non-flat) 180° POV projection with a fixed forward camera; there is no drag/orientation interaction in this build. |
| FR7 | Toggling POV on/off, and switching between footer/playlist/Fill-tab/fullscreen while POV is active, always leaves the canvas correctly sized to its container with no stretching, letterboxing gaps, or stale backing-buffer size. |
| FR8 | After exiting POV mode, changing the selected video, or navigating away from the player, no `requestAnimationFrame` loop or `ResizeObserver` from a prior POV session remains active (verified via DevTools performance/memory inspection during manual testing). |
| FR9 | Forcing `getContext` to return `null` (e.g. via a browser flag or DevTools override) results in a visible player error and the flat video remaining visible, with no unhandled exception in the browser console. |
| FR10 | No new server endpoint, FFmpeg invocation, or DTO field is added; `GET /api/videos/{id}/stream` (and the Archive Browser equivalent) network requests are unchanged in shape when POV mode is toggled. |
| FR11 | The same VR POV button/behavior is present and functional whether the video was opened from the Video Library or from the Archive Browser. |
| FR12 | Two source videos with different resolutions (e.g. one 4K side-by-side file and one 1080p side-by-side file) both render the left-eye POV image correctly framed, with no stretching/squashing difference caused by a hardcoded aspect assumption; dimensions are read from `video.videoWidth`/`video.videoHeight` at runtime. |
| FR13 | After exiting POV mode or leaving it active and then selecting a different video, the newly selected video always starts with POV mode off; the button never shows the active state for a video the user hasn't explicitly re-enabled it for in the current selection. |

## Test Cases

**Unit tests:**

- `WebApp.Tests/Client/VrPovStateTests.cs`: default `IsActive` is `false`; `Toggle()` flips the value; `Select(newId)` with a different id always resets `IsActive` to `false` (including when it was previously `true` for a different selection — covering FR13's "no persisted preference" rule); `Select(sameId)` is a no-op — following the existing structure of `FillTabStateTests.cs`.
- `WebApp.Tests/Client/` (extend or add a `Player`-focused test): `ShowVrPovButton`/equivalent computed property returns `false` when `PlayerState.IsMusic` is `true` and `true` otherwise, mirroring how `HasMultipleAudioTracks`/`HasSubtitles` computed properties are exercised in existing tests.

**Integration tests:**

- ⚠️ TODO: this repo has no browser/WebGL-capable integration test harness (`WebApp.Tests` uses `WebApplicationFactory` for HTTP/endpoint/static-root checks, not a headless browser). Actual WebGL rendering correctness (texture upload, left-eye sampling, curved projection, resize behavior) is not covered by an automated integration test in this repo and is validated manually below, consistent with how Fill-tab/fullscreen/pointer-drag behavior is already validated manually rather than via `WebApplicationFactory`.

## Manual Verification

1. Ensure a side-by-side VR180 reference video is reachable under the configured video root (it appears under `/videos/...` inside the running container — see `AGENTS.md` → Execution Environment). Never write real host paths or personal file names into committed files.
2. Run `make docker-run` (or `make docker-run-bg` + `make docker-logs`) to start the stack with hot reload.
3. Open the app, scan the Video Library, and select the VR reference video (or any other video, since the button is universal).
4. Confirm the VR POV button (`bi-badge-vr`) appears in the player toolbar next to Fullscreen/Fill-tab, with a visible tooltip and correct `aria-pressed`/label state.
5. Click the VR POV button and confirm: the flat video view is replaced by a WebGL canvas showing a curved panoramic projection of the left half of the frame; playback continues (audio, timeline) without interruption.
6. While POV is active, exercise play/pause, seek, volume, A/B markers + Save Cut, audio-track selection (if the item has multiple tracks), and subtitle toggle (if available) — confirm all behave identically to flat mode.
7. While POV is active, enter Fill-tab and then fullscreen (and exit each) — confirm the canvas resizes correctly with no distortion or blank frame in every mode transition.
8. Click the VR POV button again to exit — confirm the flat video reappears at the same playback position/play state, with no visible flicker/reset.
9. Open browser DevTools Performance/Memory tooling, toggle POV on/off and switch videos several times, and confirm no growing count of active `requestAnimationFrame` callbacks or `ResizeObserver` instances persists after each exit (no leak).
10. Simulate WebGL unavailability (e.g. `chrome://flags` disable WebGL, or a DevTools context-creation override) and confirm clicking VR POV shows the existing player-error alert instead of a blank canvas or console exception, and the flat video keeps playing.
11. Repeat steps 3–8 from the Archive Browser's video playback path to confirm parity with the Video Library path (FR11).
12. Select at least two videos of different native resolutions (e.g. a 4K and a 1080p side-by-side source) and enable POV mode on each — confirm both render correctly framed with no stretching/squashing difference, verifying dimensions are read per-video at runtime rather than assumed (FR12).
13. With POV mode active on one video, select a different video from the grid/playlist — confirm POV mode is off for the newly selected video and the button shows the inactive state; re-select the original video and confirm it also starts with POV off (no remembered state) (FR13).
14. Run `make test` and confirm all existing and new tests pass.

## Definition of Done

- Requirements, Plan, and Validation docs in this spec folder are complete and internally consistent.
- All existing `WebApp.Tests` pass under `make test`.
- `VrPovStateTests.cs` and the `Player.razor` VR-button-visibility test are added and pass.
- `MediaPlayerControls.razor` and `Player.razor` changes follow the existing icon-button/design-system contract (accessible name, tooltip, `aria-pressed`, 40×40 target) with no new bespoke CSS beyond what Bootstrap utilities already express for the sibling Fullscreen/Fill-tab buttons.
- `vrPovRenderer.js` fully tears down its render loop/GL resources on stop/dispose, verified per manual step 9.
- No FFmpeg/server/DTO changes were introduced; confirmed by diff review against `AGENTS.md`'s FFmpeg-pipeline constraint list.
- `README.md`'s `## Current Supported Features` table is updated with a new row for VR POV playback, per `AGENTS.md`'s requirement to update it whenever a spec adds a user-facing feature.
- Manual verification steps 1–14 above have been performed against the running Docker stack.

## Rollback Plan

- The feature is additive and isolated: reverting the `Player.razor`/`MediaPlayerControls.razor` changes and deleting `wwwroot/js/vrPovRenderer.js`/`VrPovState.cs` fully removes VR POV mode with no data migration, no server config, and no FFmpeg/volume changes to undo.
- If a regression is found post-merge but a full revert isn't desired immediately, the VR POV button can be hidden without removing the renderer by forcing `ShowVrPovButton` to `false` in `Player.razor` (a one-line change), which disables user access to the feature while leaving the code in place for a fix-forward.
