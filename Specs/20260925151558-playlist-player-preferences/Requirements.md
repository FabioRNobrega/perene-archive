# Requirements: Playlist Player Preferences

## Table of Contents

- [Requirements: Playlist Player Preferences](#requirements-playlist-player-preferences)
  - [Problem Statement](#problem-statement)
  - [User Stories](#user-stories)
  - [Functional Requirements](#functional-requirements)
  - [Non-Functional Requirements](#non-functional-requirements)
  - [Out of Scope](#out-of-scope)
  - [Open Questions](#open-questions)

## Problem Statement

`Player.razor` treats every change of `PersistentPlayerState.Selected` as a new standalone selection. Its `HandleSelectionChanged` calls `Select` on `VrPovState` and `SaturationState`, which deliberately resets VR POV and saturation for the next item. This makes the folder playlist experience in `PlaylistView.razor` inconsistent: a user who enables VR POV or adjusts playback preferences loses applicable preferences when the queue auto-advances or they choose another entry. The player must retain transferable preferences only while moving within one active playlist, without applying them to unrelated media or carrying video-specific settings to a different video.

## User Stories

- Given I enable VR POV while a playlist video is playing, when the playlist advances to another video or I select another video in that same playlist, then VR POV remains enabled and the renderer starts for that next video.
- Given I adjust saturation, volume/mute, speed, subtitle visibility, or Fill-tab while using a playlist, when I move between items in that playlist, then each applicable preference is retained.
- Given a playlist contains music between videos, when I later move to another video in that same playlist, then my retained video preferences, including VR POV, are restored; music continues to use only controls applicable to audio.
- Given I leave the playlist or select media outside it, when I begin a different playback context, then playlist-only preferences are discarded and the normal defaults/current standalone behavior apply.
- Given I set A/B points, a loop, a crop position, or choose an embedded audio track for one video, when I move to another item, then those item-specific settings do not transfer.

## Functional Requirements

1. FR1 — `Player.razor` shall preserve transferable player preferences when `PersistentPlayerState` changes selection by playlist queue click, previous/next action, or automatic end-of-track advance, provided the active playlist is the same one.
2. FR2 — While an active playlist remains open, VR POV enabled/disabled state shall carry from one playable video to the next. On each eligible video, `vrPovRenderer.js` shall be stopped for the prior source and started for the new source after it renders; its camera yaw, pitch, and zoom retain the existing per-source reset behavior.
3. FR3 — While an active playlist remains open, saturation (0–300%), volume, mute state, playback rate, subtitle enabled/disabled preference, and Fill-tab presentation state shall be retained across queue entries wherever the control applies.
4. FR4 — When a playlist item is music, video-only preferences shall not be applied to its media element or shown as active video behavior. They remain retained in the playlist session and are restored on a subsequent playlist video. Audio-applicable volume, mute, speed, and Fill-tab preferences remain effective for music.
5. FR5 — Standard loop, A/B points and A/B loop, selected embedded audio track, playback position, crop/reframe position, VR camera orientation/zoom, validation messages, and transient errors shall remain scoped to the selected item and shall reset or behave exactly as they do today on a track change.
6. FR6 — Playlist preferences shall exist only in the in-memory active playlist session. They shall be cleared when `PersistentPlayerState.ExitPlaylistView`, `Clear`, a standalone selection method, or entry into a different playlist ends/replaces that context; they shall not be written to browser storage, sent to an API, or shared between trusted LAN clients.
7. FR7 — Returning to the same active playlist through existing navigation/resume behavior shall retain playlist preferences along with the existing selected item and playback-progress behavior. Re-entering a different playlist shall use fresh defaults.
8. FR8 — The player controls shall accurately represent the retained state after each selection change, remain keyboard and screen-reader usable, and retain the existing WebGL-unavailable error behavior without leaving VR reported active when renderer startup fails.
9. FR9 — `README.md`'s `Current Supported Features` Playlists row shall describe that transferable player preferences are retained during an active playlist.

## Non-Functional Requirements

- **Client ownership and privacy:** This is browser-session state owned by `WebApp.Client`; it must not alter server endpoints, archive DTOs, filesystem access, opaque-ID handling, or application logging.
- **Separation of responsibilities:** A focused C# state object owns playlist-scoped preference rules; `Player.razor` coordinates DOM/JS lifecycle only; `VrPovState`, `MediaPlayerState`, `SaturationState`, `VideoFrameState`, and `FillTabState` retain their focused media/presentation responsibilities.
- **Performance:** Track changes must not introduce polling, server calls, or fine-grained JS interop. VR start/stop remains one renderer lifecycle operation per applicable source change.
- **Compatibility:** Preserve the existing video-library, archive-browser footer player, standalone playback, VR camera drag/zoom, custom controls, and mixed video/music playlist behavior.
- **Testability:** State-transfer and reset decisions must be unit-testable without a renderer or browser; JS lifecycle behavior is covered by existing manual Docker/browser validation conventions.

## Out of Scope

- Persisting preferences across page reloads, browser sessions, playlists, or clients.
- Per-video remembered settings, including remembering an earlier video's crop, A/B points, audio-track choice, or VR camera position.
- Carrying either loop mode or A/B markers between videos.
- Changing VR180 projection, camera controls, the playlist API, media processing, or archive access controls.
- Adding new toolbar controls, settings pages, local-storage schema, packages, or server-side state.

## Open Questions

- None. The agreed scope is active-playlist-only retention; video-only preferences survive intervening music entries; loop behavior stays per item.
