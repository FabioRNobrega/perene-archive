# Validation: Video Player Multi Audio Track Selection

## Table of Contents

- [Acceptance Criteria](#acceptance-criteria)
- [Test Cases](#test-cases)
- [Manual Verification](#manual-verification)
- [Definition of Done](#definition-of-done)
- [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | `FfprobeAudioTrackProbe.ParseOutput` returns one entry per audio stream for 2+ streams and an empty list for 0/1 streams or malformed JSON; the coordinator runs the probe in `Task.WhenAll`. |
| FR2 | `GET /api/videos` and archive listings return `audioTracks` with `Track N (language unknown)` fallback labels for multi-audio files and `null` otherwise; `SelectArchiveVideo` keeps `AudioTracks`. |
| FR3 | `HasMultipleAudioTracks` is false for music and for 0/1 tracks, true only for 2+. |
| FR4 | The audio control renders with `bi-translate` only when `HasMultipleAudioTracks`, lists every label, and shows a busy/disabled state only while preparing. |
| FR5 | `POST .../audio-tracks/{n}` returns `Pending` then `Ready`, is idempotent, and returns 404 for an unknown ID or an out-of-range index. |
| FR6 | `stream?audio=N` returns the cached MP4 with range support when ready, 409 when not ready, the original file when `audio` is absent/0, and 404 for invalid values. |
| FR7 | The remux uses `-map 0:v -map 0:a:N -c copy`, writes only to `/previews/audio-tracks`, publishes atomically, never modifies the source, and de-duplicates concurrent requests. |
| FR8 | Switching preserves current time, play/pause state and playback rate; a failed remux shows the player error and keeps the current track. |
| FR9 | `MediaPlayerState.Select` resets `SelectedAudioTrackIndex`; no `audioTracks` code or `[audio-tracks]` logging remains. |
| FR10 | README and AGENTS.md document the feature and the FFmpeg remux pipeline. |

## Test Cases

**Unit tests:**
- `WebApp.Tests/Services/FfprobeAudioTrackProbeTests.cs` — parsing (done in rev 1).
- `WebApp.Tests/Endpoints/VideoEndpointsAudioTrackTests.cs` — label mapping/fallback (rev 1).
- `WebApp.Tests/Client/MediaPlayerStateTests.cs` — `SelectedAudioTrackIndex` reset on `Select`.
- `WebApp.Tests/Services/AudioTrackRemuxServiceTests.cs` (new, fake `IAudioTrackRemuxer`) — Pending→Ready transition, single job for concurrent requests, remembered failure, cache-hit skips the remuxer.
- `WebApp.Tests/Services/FfmpegAudioTrackRemuxerTests.cs` (new) — argument list and redaction.
- `VideoEndpointsTests`/`CutEndpointsTests` — serialized JSON contract includes `audioTracks`.
- ⚠️ TODO: `Player.razor` swap/polling and the JS-side behavior have no automated coverage (no component/JS harness in this repo); covered manually.
- ⚠️ TODO: coordinator cache-hit test and `PersistentPlayerState` round-trip test (carried from rev 1).

**Integration tests:**
- `make test` — full `WebApp.Tests` suite in Docker. Three pre-existing VOB-related failures exist on the baseline (`Build_burns_only_the_server_resolved_subtitle_stream`, `Vob_files_are_convertible_and_uploadable_but_not_playable` x2) and are unrelated.

## Manual Verification

1. `make docker-run-bg`, then `make get-url`.
2. Open the Video Library in Chrome/Vivaldi (no flags) and select `Ghost in the Shell 1995 Converted 0001.mp4`; the translate-icon button appears next to the subtitles toggle.
3. Pick the second track; the button shows a busy spinner while video keeps playing, then the source swaps and the spoken language changes at the same position and play state.
4. Switch back and forth; a previously prepared track swaps instantly. Confirm files under `/previews/audio-tracks`.
5. Repeat from the Archive Browser (Videos category).
6. Select a single-audio video and confirm the control is absent.
7. `make docker-down`.

## Definition of Done

- Requirements, Plan and Validation reflect the implemented behavior.
- `make test` passes apart from the three pre-existing VOB failures.
- New services are registered in `Program.cs` and unit-tested with a fake remuxer.
- UI covers hidden, idle, busy and failed states with accessible labeling.
- Manual verification was run in a Chromium-based browser without flags.
- No changes to `VideoConversionArgumentBuilder`/`ConversionProfileResolver`; no `audioTracks`/debug-log leftovers.

## Rollback Plan

- Remove the audio-track POST/`?audio=` handling and the `AudioTrackRemuxService` registration in `Program.cs`; the control can be hidden by returning `null` from `BuildAudioTracks`, which collapses `HasMultipleAudioTracks` to false.
- Delete `/previews/audio-tracks` to reclaim space. No schema, persisted preference or migration exists.
