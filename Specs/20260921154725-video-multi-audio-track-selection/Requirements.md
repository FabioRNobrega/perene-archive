# Requirements: Video Player Multi Audio Track Selection

## Table of Contents

- [Problem Statement](#problem-statement)
- [User Stories](#user-stories)
- [Functional Requirements](#functional-requirements)
- [Non-Functional Requirements](#non-functional-requirements)
- [Out of Scope](#out-of-scope)
- [Open Questions](#open-questions)
- [Revision History](#revision-history)

## Problem Statement

`VideoConversionArgumentBuilder.Build` already maps every audio stream from the source (`-map 0:a?`), so a converted MP4 such as `Ghost in the Shell 1995 Converted 0001.mp4` can retain both its Japanese and English audio streams. Nothing downstream is aware of this: the Player renders a plain `<video>` element and the browser silently plays its default (usually first) audio stream. Users with a multi-audio-track file cannot discover or switch tracks, unlike subtitles, which already have a full `HasSubtitles`/`ToggleSubtitlesAsync` pattern.

The first revision of this spec relied on the browser's native `HTMLMediaElement.audioTracks`. Console diagnostics on Chrome 152/Vivaldi confirmed `video.audioTracks` is `undefined` (Chromium ships it disabled), so the control could never be enabled. This revision switches tracks **server-side**: the server stream-copy remuxes the chosen audio stream into a cached MP4 and the Player swaps the `<video>` source.

## User Stories

- Given a video whose file has more than one audio stream, when I open it in the Player (Video Library or Archive Browser), then I can see and choose which audio-language track plays via a translate-icon control next to the subtitles toggle.
- Given a video with only one audio stream, when I open it, then the audio-track control does not appear and playback is unchanged.
- Given I choose a non-default track for the first time, when the server is preparing it, then the control shows a busy state, playback continues on the current track, and the source swaps automatically when the track is ready.
- Given I switch tracks mid-playback, when the new source loads, then playback position, play/pause state, volume and playback rate are preserved.
- Given I pick another item, when the Player loads it, then the selection resets to that item's default track (no cross-item persistence).
- Given I choose a track that was prepared earlier, when I select it again, then the swap is immediate (cached).

## Functional Requirements

1. FR1 — `WebApp.Models.VideoMetadata` gains an `AudioTracks` collection (zero-based audio-stream index, language tag, codec) populated by `IVideoAudioTrackProbe`/`FfprobeAudioTrackProbe` (ffprobe `ProcessStartInfo`/`ArgumentList` pattern of `FfprobeResolutionProbe`), run in `VideoMetadataCoordinator.ProbeAsync`'s `Task.WhenAll`. The probe returns an empty list unless the file has more than one audio stream. *(Delivered in revision 1.)*
2. FR2 — `VideoItemDto` and `ArchiveItemDto` each carry a trailing optional `AudioTracks` (`AudioTrackDto(int Index, string? Language, string Label)`). `Label` falls back to `Track N (language unknown)`. `VideoEndpoints.BuildDto` and `ArchiveEndpoints.ToDtoAsync` populate it; `PersistentPlayerState.SelectArchiveVideo` copies it into the selected `VideoItemDto`.
3. FR3 — `Player.razor` exposes `HasMultipleAudioTracks` (true only when `Selected?.AudioTracks?.Count > 1` and not music). No audio-track UI or request happens otherwise.
4. FR4 — `MediaPlayerControls.razor` shows an audio-track dropdown button using `<i class="bi bi-translate">`, visible only when `HasMultipleAudioTracks`, listing each `AudioTrackDto.Label`. It is always enabled when visible, except while a track is being prepared (busy state with spinner, `aria-busy`).
5. FR5 — Track index 0 is the default and is served from the original file with no remux. Selecting a non-default track calls `POST {StreamBasePath}/{id}/audio-tracks/{index}`, which idempotently starts (or reports) a server-side remux job and returns `{ "state": "Pending" | "Ready" | "Failed" }`. The Player polls it roughly once per second until `Ready` or `Failed`.
6. FR6 — `GET {StreamBasePath}/{id}/stream?audio=N` (`api/videos` and `api/archive/{category}/items`) serves the cached remuxed MP4 with range processing when `N > 0` and the cache is ready, the original file when `audio` is absent or `0`, `409 Conflict` when the remux is not ready, and `404` for an unknown ID or an index outside `1..(trackCount-1)`.
7. FR7 — The remux is `ffmpeg -c copy -map 0:v -map 0:a:N -movflags +faststart` (no re-encoding, no subtitle/data streams), run via `ProcessStartInfo`/`ArgumentList`, one job at a time, writing a temp file in `<preview-root>/audio-tracks/` and atomically publishing `<sha256 key>.mp4`. The key covers relative path, size, last-write time and track index. The source is never written to. Cached files are not evicted.
8. FR8 — On `Ready`, the Player sets the selected index in `MediaPlayerState`, swaps the `<video>` `src` to `...stream?audio=N`, and on `loadedmetadata` restores current time, play/pause state, and playback rate through the existing `_pendingResumeTime`/`_pendingResumePlaying` path. On `Failed`, it shows the existing player command error and stays on the current track.
9. FR9 — `MediaPlayerState` keeps `SelectedAudioTrackIndex` reset in `Select` (like `IsSubtitlesEnabled`); `IsAudioTrackSelectable` and all browser `audioTracks` code (`getAudioTracks`, `setAudioTrack`, `[audio-tracks]` debug logging) are removed.
10. FR10 — `README.md` Current Supported Features documents switching between multiple embedded audio tracks (Video Library and Archive Browser); `AGENTS.md` documents the new FFmpeg audio-track remux pipeline and endpoints.

## Non-Functional Requirements

- Preserve server-only path containment: no physical/root-relative paths, raw ffprobe JSON, or ffmpeg diagnostics reach the browser; diagnostics are redacted in logs like `FfmpegSubtitleGenerator`.
- The remux touches only stream-copy of the selected source into `/previews`; no transcoding. `VideoConversionArgumentBuilder`/`ConversionProfileResolver` stay unchanged.
- Remux runs at most one at a time; repeated requests for the same key never start duplicate jobs; failures are remembered per process so the UI does not retry forever.
- Bootstrap 5.3/Bootstrap Icons: visible accessible label, keyboard operability, 40px minimum targets, dark/light compatibility.
- Nullable-aware C#, client/server model separation, deterministic xUnit tests following existing conventions.

## Out of Scope

- Any change to how the conversion pipeline maps or transcodes audio.
- Cross-item or cross-session persistence of a preferred audio language.
- Audio-track selection for music playback, cuts, or compositions.
- Transcoding audio/video, or supporting non-MP4-family sources (`.webm` sources may report `Failed`).
- Cache eviction or size limits for remuxed files (cached indefinitely, like thumbnails).
- Browser-native `audioTracks` support (removed).

## Open Questions

- Resolved: browsers (Chrome/Vivaldi) do not expose `audioTracks`; server-side switching chosen (Option A: background job with polling).
- Resolved: no cache eviction; default track served from the original; Archive Browser videos included.

## Revision History

- Rev 1 (2026-09-21): browser-native `audioTracks` approach; probe + DTO + UI delivered.
- Rev 2 (2026-09-21): replaced with server-side stream-copy remux and source swap; browser `audioTracks` code removed; Archive Browser included.
