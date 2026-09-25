# Validation: Playlist Player Preferences

## Table of Contents

- [Validation: Playlist Player Preferences](#validation-playlist-player-preferences)
  - [Acceptance Criteria](#acceptance-criteria)
  - [Test Cases](#test-cases)
  - [Manual Verification](#manual-verification)
  - [Definition of Done](#definition-of-done)
  - [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | Selecting next/previous, clicking a queue item, and automatic advance within one playlist retain transferable preferences. |
| FR2 | VR remains enabled for each subsequent playlist video, starts a new renderer for that source, and begins with the existing default camera orientation/zoom. |
| FR3 | Saturation, volume, mute, speed, subtitle preference, and Fill-tab remain reflected in controls and media after an applicable playlist transition. |
| FR4 | A music entry uses its applicable settings without showing/applying video-only behavior; a later video restores retained VR, saturation, and subtitle preferences. |
| FR5 | Loop modes, A/B points, audio-track selection, time, crop, VR camera view, errors, and validation do not transfer to the next item. |
| FR6 | Leaving the playlist, clearing playback, selecting standalone media, or entering another playlist drops playlist-only preferences; no network request or browser-storage write is added. |
| FR7 | Existing same-playlist navigation/resume retains preferences; a distinct playlist begins with defaults. |
| FR8 | Restored controls remain operable and correctly announced; WebGL startup failure clears VR active state and preserves the current error message behavior. |
| FR9 | README documents playlist-session preference retention. |

## Test Cases

**Unit tests:**

- `WebApp.Tests/Client/PlaylistPlayerPreferencesStateTests.cs` — verify default values; capture/apply of each transferable preference; video-only values remain stored through music; and explicit reset removes all state.
- `WebApp.Tests/Client/PersistentPlayerStateTests.cs` — verify same-playlist next/previous/queue selection retains the playlist context, while `ExitPlaylistView`, `Clear`, standalone selection, and a different playlist clear or replace it.
- `WebApp.Tests/Client/VrPovStateTests.cs` — verify standalone selection still turns VR off, while a playlist-mediated restored intent can activate the replacement video's renderer lifecycle without retaining camera state.
- `WebApp.Tests/Client/SaturationStateTests.cs` — verify standalone selection resets to default and playlist restoration applies a clamped retained value.
- `WebApp.Tests/Client/MediaPlayerStateTests.cs` — verify volume, mute, rate, and subtitles can be restored while standard/A-B loops, markers, audio track, duration, and current time remain selection-local.

**Integration tests:**

- No server/API integration test is needed: the behavior is fully browser-client state and must not modify endpoints. Run `make test` to ensure all host and client unit/endpoint tests pass in the documented Docker Compose test stack.
- ⚠️ TODO: The repository has no Blazor component/JS interop test harness. Validate keyed element replacement and actual `startVrPov`/`stopVrPov` order manually in a browser.

## Manual Verification

1. Create or choose an archive folder containing at least two VR180 videos and one music file; start it with **Play as a Playlist**.
2. Run `make docker-run`, open the playlist route in a supported browser, enable VR POV, alter saturation, set volume/mute and playback speed, disable subtitles if available, and enter Fill-tab.
3. Select the next video and verify retained video controls, Fill-tab, and an active VR canvas; double-click/drag/zoom the canvas and verify its camera begins at the normal fresh-source view.
4. Let a video auto-advance and repeat the verification; use previous and a direct queue-item click as separate transition paths.
5. Select the music entry. Verify volume/mute/speed and Fill-tab remain effective, while VR/saturation/subtitle controls are not applied as active media behavior.
6. Select a later video. Verify VR, saturation, and subtitle choice return. Verify its crop is centered, playback starts at the normal new-item position, no loop/A-B setting transfers, and no embedded audio-track selection transfers.
7. Leave the playlist, select a standalone video, then open a different playlist. Verify playlist preferences no longer apply in either context.
8. Test a browser/device without WebGL or temporarily force renderer startup failure using the existing development approach; verify VR is not shown active after the existing error message.
9. Run `make test` and confirm it completes successfully.

## Definition of Done

- Requirements, plan, and validation are complete in this spec folder.
- Playlist-only retention and every listed reset boundary have xUnit coverage consistent with `WebApp.Tests/Client` conventions.
- `make test` passes in the isolated Docker Compose stack.
- Manual browser verification covers auto-advance, queue clicks, previous/next, mixed music/video, Fill-tab, VR renderer startup failure, accessibility-visible control state, and leaving/replacing playlists.
- `README.md`'s Current Supported Features table describes the implemented playlist behavior.
- No endpoints, DTOs, filesystem paths, logs, browser persistence, packages, or FFmpeg behavior are changed.
- Microsoft-specific lifecycle/interop choices are verified with the Microsoft Learn MCP when available, or remain marked pending as documented in the plan.

## Rollback Plan

- Revert the playlist-preference state object and the related `Player.razor`/`PersistentPlayerState` transition wiring. This restores the existing per-selection `Select(...)` resets without data migration or server cleanup.
- Revert the associated client tests and README row together. There is no persisted state, configuration, endpoint, queue, or generated media to remove.
