# Requirements: VR POV Player Mode

## Table of Contents

- [Requirements: VR POV Player Mode](#requirements-vr-pov-player-mode)
  - [Problem Statement](#problem-statement)
  - [User Stories](#user-stories)
  - [Functional Requirements](#functional-requirements)
  - [Non-Functional Requirements](#non-functional-requirements)
  - [Out of Scope](#out-of-scope)
  - [Open Questions](#open-questions)

## Problem Statement

`WebApp/WebApp.Client/Components/Player.razor` currently renders every video as a flat `<video>` element with object-position framing (`VideoFrameState`) and standard playback controls from `MediaPlayerControls.razor`. Some library content — for example side-by-side VR180 recordings in the mounted video root — is authored so that the left half of each frame is a full 180-degree POV capture meant to be viewed as an immersive panorama, not as a flat rectangle. Today there is no way to view that half of the frame projected as a POV image; the user only ever sees the raw side-by-side frame stretched to the player viewport. There is also no per-item server classification for this content today (unlike `IsMusic`/`IsImage` on `ArchiveItemDto`), and the user has confirmed none is wanted: every video should offer the same manual toggle, and the user decides per-viewing whether to enable it.

## User Stories

- Given the Player is showing any video (Video Library or Archive Browser), when the user clicks the new VR POV button in the player toolbar, then the flat video view is replaced by a WebGL-rendered POV projection of the left half of the current video frame, updated live as playback continues.
- Given VR POV mode is active, when the user clicks the same button again, then the player returns to the normal flat `<video>` view at the same playback position and play/pause state, with no interruption to audio or timeline.
- Given VR POV mode is active in the footer/playlist-sized viewport, when the user also enters Fill-tab or fullscreen, then the POV canvas keeps rendering correctly and resizes to fill the new viewport, and vice versa (entering POV while already in Fill-tab/fullscreen).
- Given VR POV mode is active, when the user uses the existing timeline, play/pause, volume, A/B markers, Save Cut, audio-track, or subtitle controls, then those controls keep working exactly as they do in flat mode, because the underlying `<video>` element remains the single source of truth for decoding and playback state.
- Given the user's browser or GPU does not support WebGL, when the user clicks the VR POV button, then the player shows a clear inline error instead of a blank canvas or a crash, and flat playback is unaffected.

## Functional Requirements

1. FR1 — `MediaPlayerControls.razor` renders a VR POV toggle button, icon `<i class="bi bi-badge-vr"></i>`, in the same toolbar `btn-group`/`btn-toolbar` region as the existing Fullscreen/Fill-tab buttons, for every non-music video item (i.e. whenever the player is not in `IsMusicMode`, matching the existing pattern in `Player.razor`'s `@if (PlayerState.IsMusic)` branch that already excludes audio-only playback from video-only controls). No server-side "VR format" classification field is introduced; the button is always available and the user decides per click whether to enable POV mode.
2. FR2 — Clicking the button when POV mode is inactive enables it; clicking it again disables it. The button reflects active/inactive state the same way `IsFillTabActive`/`IsFullscreenActive` do today (`aria-pressed`, active toggle CSS class via `ToggleCssClass`, and a title/aria-label that changes between "Enable VR POV" and "Exit VR POV").
3. FR3 — `Player.razor` adds a `<canvas>` element inside the same `VideoViewportCssClass` container as the existing `<video>` element (for the non-music branch only). When POV mode is active, the canvas is visible and the `<video>` element is visually hidden (e.g. `opacity-0`/`position-absolute` with zero size impact) while continuing to decode, play, and drive `ontimeupdate`/`onplay`/`onpause`/etc. exactly as today. When POV mode is inactive, the canvas is hidden and the video is shown as today.
4. FR4 — A new client-side JS module (e.g. `wwwroot/js/vrPovRenderer.js`), imported the same way `videoEditor.js` is imported in `Player.razor`'s `OnAfterRenderAsync` (`JS.InvokeAsync<IJSObjectReference>("import", "./js/vrPovRenderer.js")`), owns all WebGL state: context creation, the video texture, the POV mesh/shader program, the render loop, and teardown. `Player.razor`'s C# code only calls exported functions (`startVrPov(video, canvas)`, `stopVrPov(canvas)`, `resizeVrPov(canvas)`) — no per-frame pixel data crosses the Blazor/JS interop boundary.
5. FR5 — The WebGL renderer uploads the current decoded `<video>` frame as a WebGL texture (`texImage2D` with the `<video>` element as source, refreshed every animation frame while playing) and samples only the left 50% of that texture horizontally (`u` in `[0, 0.5]`), matching the side-by-side VR180 layout of the reference file.
6. FR6 — The sampled left-eye image is projected onto 180-degree curved (hemispherical/equirectangular) geometry viewed by a camera fixed at the center, facing forward, with no orientation input (no drag, no gyroscope, no mouse-look) in this iteration.
7. FR7 — The canvas is resized to match its container's rendered size (via `ResizeObserver` or an equivalent JS-side resize handler registered in `startVrPov`) so POV mode renders correctly in every existing `VideoViewportCssClass` mode: footer preview, playlist, Fill-tab, and fullscreen, and reacts correctly when the user switches between those modes while POV is active.
8. FR8 — Toggling POV mode off (including via `StopAsync`/selection change/component disposal) calls `stopVrPov` to cancel the render loop and release WebGL resources (delete textures/buffers/program, drop the canvas context reference) so no leaked `requestAnimationFrame` loop or GPU resource persists after the video changes or the component unmounts.
9. FR9 — If `canvas.getContext("webgl2")` (with fallback to `"webgl"`) returns `null`, `startVrPov` reports failure back to C# (thrown `JSException` or a returned boolean), `Player.razor` shows the existing player-error alert region (`_playerError`/`ShowPlayerCommandError` pattern) with a WebGL-specific message, and POV mode is not entered (the flat video remains visible).
10. FR10 — No FFmpeg pipeline, server endpoint, or `VideoLibraryOptions`/`VideoItemDto`/`ArchiveItemDto` schema change is introduced; the video is requested from the existing `GET /api/videos/{id}/stream` (or the Archive Browser's equivalent `.../stream`) endpoint exactly as today, with the browser-side opaque ID unchanged.
11. FR11 — The VR POV button and rendering behavior are available identically from both the Video Library's `Player.razor` usage and the Archive Browser's `Player.razor` usage, since both share the same `Player.razor`/`MediaPlayerControls.razor` components and the button is not gated by any archive-specific or library-specific condition beyond "is a playable video, not music."
12. FR12 — The renderer does not assume a fixed source resolution or aspect ratio. Library videos are not uniform in resolution (confirmed by the user), so `startVrPov` reads the actual decoded frame size from the `<video>` element at runtime (`video.videoWidth`/`video.videoHeight`, available once `loadedmetadata` has fired — `Player.razor` already waits on `@onloadedmetadata` before other setup) and derives the left-eye texture region (still the left 50% of width per FR5) and the mesh/projection aspect from those real dimensions, not from a hardcoded constant. If `video.videoWidth`/`video.videoHeight` change (e.g. a new source is loaded into the same `<video>` element after a selection change while POV was left running), the renderer re-derives these values before the next draw rather than continuing to use stale dimensions.
13. FR13 — VR POV mode always starts inactive for a newly selected video, with no persisted or remembered preference across selections; `VrPovState` unconditionally resets `IsActive` to `false` in `Player.razor.HandleSelectionChanged`, and the user must re-enable the button every time they select a different video, even if the previous video had POV mode active. (Resolved from the prior Open Questions — see below.)

## Non-Functional Requirements

- **No new runtime dependency**: the renderer is implemented in raw WebGL (no Three.js or other 3D library), per explicit decision — see Plan.md.
- **Accessibility**: the VR POV button is icon-only and must have an accessible name (`aria-label`), a Bootstrap tooltip (`data-bs-toggle="tooltip" data-bs-title="..."`), a minimum 40×40 CSS-pixel hit target, and a visible focus state — consistent with the existing Fullscreen/Fill-tab buttons and the design-system icon-button contract in `AGENTS.md`.
- **Resilience**: WebGL context-creation failure, texture-upload failure, or a video source that errors mid-POV must degrade to a visible error state, never a blank canvas, frozen frame, or unhandled JS exception that breaks the rest of the player.
- **Resource cleanup**: exactly one active WebGL render loop and one active `ResizeObserver` per mounted `Player.razor` instance; both must be torn down on POV exit, video-selection change, and component `DisposeAsync`, mirroring the existing `_module.InvokeVoidAsync("exitFillTab")` / `"unregisterVideoFullscreenChange"` cleanup pattern already in `Player.razor.DisposeAsync`.
- **Testability**: the C# side (button visibility/state, POV toggle wiring, error-state propagation) must be coverable the same way existing `Player.razor`/`MediaPlayerControls.razor` behavior is covered in `WebApp.Tests/Client`; the WebGL rendering itself is not unit-testable in this repo's xUnit setup and is validated manually per `Validation.md`.

## Out of Scope

- Any drag/mouse-look/gyroscope orientation control of the POV camera (explicitly first-iteration-excluded per the design notes; camera stays fixed forward).
- Top-and-bottom (over/under) VR stereo layout support — only side-by-side (left-eye-left-half) is handled.
- Right-eye/stereo rendering, dual-canvas stereo output, or WebXR/headset device output — this is a flat-screen "POV window" mode, not a true stereo VR headset mode.
- Any server-side VR format detection, folder/filename classification, metadata tagging, or `VideoItemDto`/`ArchiveItemDto` schema change — the user explicitly rejected auto-classification; the button is manual and universal.
- Any FFmpeg transcoding, remuxing, or new server endpoint for VR content — playback continues to use the existing stream endpoints unchanged.
- Cuts/Composition pipeline changes — VR POV is a `Player.razor` presentation feature only, not a new pipeline.

## Open Questions

- ✅ Resolved — per-video resolution: the library has videos of varying resolutions, so the renderer must not assume a fixed source size. See FR12: `video.videoWidth`/`video.videoHeight` are read at runtime (post-`loadedmetadata`) and drive the left-eye texture region and mesh/projection aspect, re-derived if the source changes.
- ✅ Resolved — no persisted preference: POV mode always resets to off on every video-selection change; the user re-enables it per video, with no cross-selection memory. See FR13.
- ⚠️ TODO: Exact hemispherical mesh subdivision count and base horizontal field-of-view (before per-video aspect adjustment from FR12) are left to implementation judgment in `Plan.md`/code, since they don't change observable acceptance criteria beyond "renders a curved POV projection, not a flat rectangle, correctly framed for the actual source resolution."
