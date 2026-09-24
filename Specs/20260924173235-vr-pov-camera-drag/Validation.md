# Validation: VR POV Camera Drag

## Table of Contents

- [Validation: VR POV Camera Drag](#validation-vr-pov-camera-drag)
  - [Acceptance Criteria](#acceptance-criteria)
  - [Test Cases](#test-cases)
  - [Manual Verification](#manual-verification)
  - [Definition of Done](#definition-of-done)
  - [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | `vrPovCameraController.js` exists, contains no WebGL/Blazor references, and owns yaw/pitch/sensitivity/reset. |
| FR2 | Dragging with mouse, touch and pen rotates the view; a second finger/pointer during a drag is ignored; releasing outside the canvas ends the drag. |
| FR3 | Dragging right/left turns the view accordingly; dragging up looks up, down looks down; motion at default sensitivity feels medium. |
| FR4 | Dragging to any extreme never shows black edge/outside-hemisphere area; limits hold after resizing to fullscreen/Fill-tab and with videos of different resolutions. |
| FR5 | Only the view changes; texture, left-eye sampling and curvature are unchanged versus before this spec when the camera is at 0/0. |
| FR6 | DevTools shows no Blazor event/interop traffic during a canvas drag; only `startVrPov`/`stopVrPov`/optional reset/sensitivity calls exist. |
| FR7 | Double-click recenters; toggling VR off/on and selecting another video both start at 0/0. |
| FR8 | Dragging ≥ 6 px does not reveal the controls on release; a tap reveals them as before; a tap does not toggle play/pause. |
| FR9 | Touch-dragging the canvas does not scroll or zoom the page. |
| FR10 | With VR off (including Fill-tab), dragging the video reframes the crop exactly as before; no camera code runs; with VR on, flat drag does not run. |
| FR11 | After stop/selection change/navigation there are no remaining pointer listeners or captured pointers for the canvas. |
| FR12 | Behavior identical in Video Library and Archive Browser and across footer/playlist/Fill-tab/fullscreen. |
| FR13 | README VR POV row mentions drag-to-look and zoom. |
| FR14 | Zoom never goes below the default view or above 3×; the projection uses a narrower FOV when zoomed in with no change to the texture or mesh. |
| FR15 | Wheel up over the canvas zooms in, wheel down zooms out, and the page does not scroll (also for Ctrl+wheel/trackpad pinch). |
| FR16 | On a touch device, spreading two fingers zooms in and pinching zooms out; the pinch does not also rotate the view or reveal controls; lifting one finger ends the pinch without a jump. |
| FR17 | Zoomed in, the view can be dragged farther without black edges than at zoom 1; zooming out while at an extreme re-clamps the view instead of exposing black edges. |
| FR19 | With VR on, the saturation slider visibly changes the canvas (0% grayscale, 300% boosted) and Reset returns it to 100%; flat mode looks identical to before. |
| FR18 | Double-click, toggling VR off/on and selecting another video all restore zoom 1. |

## Test Cases

**Unit tests:**

- Existing suites must remain green: `WebApp.Tests/Client/VrPovStateTests.cs`, `VideoFrameStateTests.cs`, `FillTabStateTests.cs` (guards FR10 on the C# side).
- No new C# state is introduced, so no new xUnit cases are required. ⚠️ TODO: the clamp/limit/view-matrix pure functions have no automated test because the repo has no JS test harness; keep them pure so one can be added later.

**Integration tests:**

- ⚠️ TODO: no headless-browser/WebGL harness exists in `WebApp.Tests`; behavior is validated manually below, like the prior VR POV spec.

## Manual Verification

1. `make docker-run`, open the app, open a 180° side-by-side video from the Video Library.
2. Enable VR POV; drag left/right/up/down with the mouse; confirm direction, medium speed and no black edges at the extremes.
3. Double-click the canvas; confirm the view recenters.
4. Drag then release: controls must not appear. Tap without moving: controls appear, playback state unchanged.
5. Toggle VR off then on, then select another video: view starts centered.
6. Repeat in Fill-tab and fullscreen, and with a differently sized video.
7. On a touch device (or DevTools touch emulation), drag with a finger; the page must not scroll.
8. Turn VR off; drag the video in Fill-tab/flat mode and confirm crop reframing is unchanged.
9. Repeat step 2 from the Archive Browser.
10. In DevTools, confirm no Blazor interop traffic during drags and no listeners after leaving the player.
11. Scroll the wheel over the canvas: zoom in/out, page does not scroll; zoom in and confirm you can drag farther; zoom out at an extreme and confirm no black edges.
12. On a touch device (or DevTools multi-touch emulation), pinch out/in and confirm zoom, no rotation jump, no controls reveal.
13. Double-click and confirm both view and zoom reset.
14. With VR on, move the saturation slider to 0% and 300%, press Reset; repeat in Fill-tab/fullscreen and after toggling VR off/on.
15. `make test`.

## Definition of Done

- Requirements, Plan and Validation are updated in this spec folder.
- `make test` passes.
- Manual steps 1–13 pass in Chromium and one other browser, plus touch.
- README feature row and AGENTS.md updated.
- No flat-drag file behavior changed.

## Rollback Plan

- Remove the controller creation in `startVrPov` (renderer then uses an identity view matrix) or revert the commit; delete `vrPovCameraController.js`. No data, config or server change is involved.
