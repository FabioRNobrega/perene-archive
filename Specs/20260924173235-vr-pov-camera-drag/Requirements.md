# Requirements: VR POV Camera Drag

## Table of Contents

- [Requirements: VR POV Camera Drag](#requirements-vr-pov-camera-drag)
  - [Problem Statement](#problem-statement)
  - [User Stories](#user-stories)
  - [Functional Requirements](#functional-requirements)
  - [Non-Functional Requirements](#non-functional-requirements)
  - [Out of Scope](#out-of-scope)
  - [Open Questions](#open-questions)

## Problem Statement

The VR POV mode from `Specs/20260924170726-vr-pov-player-mode/` renders the left half of a side-by-side VR180 frame through a fixed-forward camera in `WebApp/WebApp.Client/wwwroot/js/vrPovRenderer.js`; the projection matrix is static and there is no way to look around. The user wants to look left/right (yaw) and up/down (pitch) by dragging the canvas, with mouse, touch and pen. The existing flat-mode drag (`Player.razor` `StartDragAsync`/`MoveDrag`/`EndDragAsync` → `VideoFrameState.ApplyDrag`, which repositions the 9:16 crop via object-position) must remain completely unaffected and completely separate from the new camera drag. All videos in the library are 180° content.

## User Stories

- Given VR POV mode is active, when the user presses and drags horizontally/vertically on the canvas (mouse, touch or pen), then the view rotates (yaw/pitch) and the video keeps playing normally.
- Given VR POV mode is active and the camera has been moved, when the user double-clicks the canvas, then the view returns to the forward-centered position.
- Given VR POV mode is inactive (flat mode, including Fill-tab), when the user drags the video, then the existing crop-reframing drag behaves exactly as before and no camera code runs.
- Given the user disables and re-enables VR POV, or selects another video, then the camera starts at yaw 0 / pitch 0.
- Given VR POV mode is active, when the user scrolls the mouse wheel over the canvas (up = zoom in, down = zoom out) or pinches with two fingers (fingers apart = zoom in, together = zoom out), then the field of view narrows or widens smoothly within limits and the page does not scroll.
- Given the user drags more than the tap threshold on the canvas, when the pointer is released, then the player controls are not revealed; a tap without drag still reveals them as today.

## Functional Requirements

1. FR1 — A new JS module `WebApp/WebApp.Client/wwwroot/js/vrPovCameraController.js` owns the camera state: `yaw`, `pitch`, drag state, sensitivity, clamping and reset. It has no WebGL code and no knowledge of Blazor.
2. FR2 — The controller uses Pointer Events on the VR canvas (`pointerdown`, `pointermove`, `pointerup`, `pointercancel`, `lostpointercapture`) with `setPointerCapture`/`releasePointerCapture`, so mouse, touch and pen behave identically. Only the pointer that started the drag is tracked.
3. FR3 — While dragging, `yaw += movementX * sensitivity` and `pitch -= movementY * sensitivity` (dragging up looks up). Default sensitivity is a "medium" constant of `0.002` rad/px.
4. FR4 — Yaw and pitch are clamped after every update so the visible frustum never leaves the recorded 180° hemisphere: `|yaw| <= 90° - horizontalFov/2` and `|pitch| <= min(60°, 90° - verticalFov/2)`. The FOV values come from the renderer's per-video projection (derived from `video.videoWidth/videoHeight`, per FR12 of the prior spec), so limits are recomputed whenever the source dimensions or canvas aspect change.
5. FR5 — The renderer applies the camera as a view matrix (rotation order yaw-then-pitch, i.e. `Ry(yaw)·Rx(pitch)` inverse for the eye) multiplied with the existing perspective matrix; mesh, UVs, texture and shader sampling are unchanged. Three.js is not introduced (the renderer is raw WebGL).
6. FR6 — The pointermove loop runs entirely in JS. Blazor receives no pointermove/pointerdown events for the canvas and no per-move interop calls; C# only starts/stops the renderer (existing `startVrPov`/`stopVrPov`), and may call `resetVrPovView(canvas)` and `setVrPovSensitivity(canvas, value)`.
7. FR7 — Double-clicking the canvas resets yaw and pitch to 0. The camera also starts at 0/0 on every `startVrPov` call, so toggling VR off/on or selecting another video resets the view. Nothing is persisted.
8. FR8 — A pointer sequence whose total travel exceeds the existing `DragThreshold` (6 px, matching `Player.razor`) suppresses the canvas's Blazor `@onpointerup="RevealControls"`; a tap below the threshold keeps the current behavior (controls revealed) and does not toggle play/pause.
9. FR9 — The canvas has `touch-action: none` so touch drags do not scroll the page or trigger browser pan/zoom gestures.
10. FR10 — The flat-mode drag (`VideoFrameState`, `Player.razor` drag handlers on `<video>`, `videoEditor.js` `measureAndCapture`/`releasePointer`) is not modified in behavior. The camera controller attaches only to the VR canvas and the flat drag handlers only to the `<video>`; neither references the other.
11. FR11 — The controller is attached when `startVrPov` succeeds and detached (listeners removed, pointer capture released, state discarded) in `stopVrPov`/teardown, including the renderer's error/disconnect teardown path and `Player.razor.DisposeAsync`.
12. FR12 — Behavior is identical in Video Library and Archive Browser, in footer/playlist/Fill-tab/fullscreen viewports, and drag continues to work after the canvas is resized.
13. FR13 — `README.md`'s `## Current Supported Features` VR POV row is updated to mention drag-to-look and zoom.
14. FR14 — The controller owns a `zoom` factor (1 = default view, larger = zoomed in), clamped to `[1, 3]`. The renderer applies it by narrowing the effective vertical FOV: `effectiveVFov = 2·atan(tan(baseVFov/2) / zoom)`; the horizontal FOV follows from the canvas aspect. Zoom is exposed through the view-source contract as `getZoom()`; the renderer treats a missing `getZoom` as 1.
15. FR15 — Mouse wheel over the canvas changes zoom (`zoom *= exp(-deltaY * 0.001)`, wheel up = zoom in) via a non-passive `wheel` listener that calls `preventDefault()` so the page does not scroll.
16. FR16 — Two-finger pinch changes zoom by the ratio of the current to the starting distance between the two pointers (`zoom = startZoom * distance / startDistance`, fingers apart = zoom in). Starting a second pointer cancels any single-pointer drag and marks the sequence as moved so controls are not revealed on release; when one finger lifts, the pinch ends and the remaining finger does not resume a drag until it is lifted and pressed again.
17. FR17 — Because the effective FOV shrinks when zoomed in, the yaw/pitch limits of FR4 are computed from the effective FOV and orientation is re-clamped whenever zoom changes (zooming in widens the allowed range; zooming out re-clamps).
18. FR18 — Double-click and toggling VR off/on reset zoom to 1 together with yaw/pitch (FR7); zoom is never persisted.
19. FR19 — The existing saturation rail (`_saturation`/`SaturationState`, 0–300%) applies to the VR canvas exactly as it does to the flat `<video>`: `Player.razor` exposes one `SaturationFilter` style (`filter: saturate(N%)`) used by both the `<video>` (`VideoStyle`) and the VR `<canvas>`. Saturation is not implemented in the WebGL shader and no JS/interop is added for it.

## Non-Functional Requirements

- **SOLID**: single responsibility per module (renderer draws, controller owns orientation, Blazor toggles mode); the renderer depends on a small "view source" abstraction (`getViewMatrix()`), not on the concrete controller (dependency inversion); a different source (e.g. gyroscope later) can be supplied without editing the renderer (open/closed).
- **Performance**: no allocations per pointermove beyond reused matrix arrays; rendering stays driven by the existing `requestAnimationFrame` loop (no extra loop).
- **Resource cleanup**: exactly one controller per canvas session and no listeners left after teardown.
- **Accessibility**: canvas keeps `aria-label`; no keyboard camera control in this iteration (decided).
- **Testability**: orientation math (clamp, sensitivity, reset) is kept in pure functions inside the controller module so it can be tested later; the repo has no JS test harness, so validation is manual.
- **No new dependencies, endpoints, DTO fields or FFmpeg use.**

## Out of Scope

- Inertia, smoothing, gyroscope/device orientation, WebXR, stereo/right-eye rendering.
- Keyboard camera control, zoom UI buttons/slider, zoom below the default view (min zoom 1 keeps the whole frame inside the 180° hemisphere), user-facing sensitivity UI, a toolbar reset button.
- Persisting camera position across toggles or videos.
- Any change to flat-mode / Fill-tab drag behavior.
- Tap-to-play/pause on the canvas.
- Shader-based saturation (rejected in favor of the shared CSS filter, FR19).

## Open Questions

- ⚠️ Assumption to confirm: the user said all videos are 180°, so the clamp uses the FOV-aware limits in FR4 (no black edges) rather than a raw ±90° yaw; the pitch cap of 60° comes from the user's brief.
- ⚠️ Assumption: zoom range 1–3× and wheel speed 0.001 are starting values; the default view is the widest.
- ⚠️ TODO: tune sensitivity (0.002) and pitch limit after trying it on the real videos.
