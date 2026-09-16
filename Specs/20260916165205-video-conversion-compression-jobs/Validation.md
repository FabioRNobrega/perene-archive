# Validation: Video Conversion and Compression Jobs

## Table of Contents

- [Acceptance Criteria](#acceptance-criteria)
- [Test Cases](#test-cases)
- [Manual Verification](#manual-verification)
- [Definition of Done](#definition-of-done)
- [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | Each listed extension is identified as convertible in an archive response without making unsupported input browser-playable; existing playable-video behavior remains unchanged. |
| FR2 | A convertible file shows Convert / Compress to MP4 in its action menu; folders, generic files, Trash items, and duplicate active source jobs do not. |
| FR3 | Valid submission returns 202 with an opaque ID; invalid/moved/non-convertible IDs return a client error; a full queue returns 503 without a phantom job. |
| FR4 | Probe parsing reads the five stated fields and rejects missing/malformed/video-less media output. |
| FR5 | Tests cover every `MediaAction`, including a no-output `Keep`/Skipped path and bitrate threshold boundaries. |
| FR6 | A changed source, FFmpeg failure, cancellation, or invalid temporary output leaves the source intact and no published partial MP4; a valid result is atomically published beside it. |
| FR7 | Argument-builder tests prove each action's codec/copy/CRF behavior, distinct argument entries, stable frame-rate/dimension handling where required, and collision-safe MP4 names. |
| FR8 | Validation confirms required output streams/duration; subtitle handling either preserves a compatible stream or gives a failed diagnostic before publication; metadata/chapters are mapped where supported. |
| FR9 | Dashboard has keyboard-operable Overview/Jobs tabs; Jobs shows ordered state/action/outcome, polls only while active, and manual Refresh refreshes both views. |
| FR10 | Endpoint JSON, job DTOs, and diagnostics never contain physical/archive-relative source paths, temporary paths, or raw FFmpeg commands. |
| FR11 | Queue-full/free-space/worker-failure states are visible safely; restart behavior and temp cleanup are documented. |
| FR12 | README Current Supported Features describes source formats and non-destructive sibling-MP4 behavior. |

## Test Cases

**Unit tests:**

- `WebApp.Tests/Services/FfprobeVideoConversionProbeTests.cs` — parse representative JSON for MP4/H.264/AAC, MKV/H.264/AAC, incompatible audio/video, bitrate/resolution, malformed output, and no-video inputs.
- `WebApp.Tests/Services/MediaConversionPlannerTests.cs` — assert Keep, Remux, ConvertAudio, CompressVideo, and FullTranscode selection, including bitrate/resolution threshold edges.
- `WebApp.Tests/Services/VideoConversionNamingServiceTests.cs` — produce collision-safe sibling `.mp4` names without changing the source name/path.
- `WebApp.Tests/Services/VideoConversionJobQueueTests.cs` and `VideoConversionJobStatusStoreTests.cs` — FIFO capacity behavior, no duplicate active source, and Pending/Processing/Completed/Failed/Skipped ordered transitions.
- `WebApp.Tests/Services/FfmpegVideoConversionGeneratorTests.cs` — separate argument entries for hostile filenames, each action's expected stream copy/codec flags, source revalidation, free-space rejection, temp cleanup, redacted failures, successful post-probe/atomic-publish path, and source preservation.
- `WebApp.Tests/Services/ArchiveServiceTests.cs` — convertible extension classification, opaque source resolution, and rejection for Trash/outside-category/non-convertible entries.
- `WebApp.Tests/Client/VideoConversionJobStateTests.cs` — state labels, job ordering, status presentation, and saved-size calculations used by the dashboard.

**Integration tests:**

- `WebApp.Tests/Endpoints/ArchiveEndpointsConversionTests.cs` using `WebApplicationFactory<Program>` — ensure valid opaque source submission is accepted and visible through `GET /api/dashboard/jobs`; verify invalid, unsupported, changed, and saturated cases; assert all responses omit root/temp paths.
- `WebApp.Tests/Endpoints/DashboardEndpointsTests.cs` — include `/api/dashboard/jobs` and assert it returns OK when no jobs exist and when the FFmpeg worker is unavailable/failed.
- Run `make test` in the documented isolated Docker Compose test stack. Add a small legal FFmpeg fixture only if the existing Docker image can generate/probe it within test time; otherwise retain deterministic generator/probe fakes and cover the full binary invocation manually.

## Manual Verification

1. Create the normal archive layout described in `README.md`, including `Videos`, then place one each of a high-bitrate browser-compatible MP4, compatible-codec MKV, incompatible-codec/container sample, and one non-video file in `Videos`.
2. Start the Docker-only application with `make docker-run`.
3. Open `/videos`, confirm each listed video source appears as a file; confirm only convertible files show **Convert / Compress to MP4** in the individual action menu.
4. Submit each sample and verify the immediate accepted feedback; submit one again while active and verify duplicate submission is unavailable/rejected.
5. Open `/`, select **Jobs**, and verify Pending then Processing state, selected action, accessible progress/status text, and automatic polling. Use Refresh to confirm immediate state refresh.
6. After completion, verify a unique sibling MP4 exists, the original remains byte-for-byte present, the output plays through the normal archive browser, and an MP4 compression result is smaller when the planner chose CompressVideo.
7. Inspect Dashboard Jobs and browser network responses to confirm no physical/archive-relative paths or raw commands are shown.
8. Queue a long conversion, stop the stack with `make docker-down`, and confirm no incomplete sibling MP4 remains; restart and confirm in-memory job status is not claimed as resumable/completed.
9. Run `make test` and verify all tests pass.

## Definition of Done

- Requirements, Plan, and Validation documents are complete in this spec folder.
- All existing and new xUnit tests pass through `make test`.
- The archive action, probe/planner, queue/worker/output validation, Dashboard Jobs tab, responsive/accessibility states, and README feature table are implemented consistently.
- No browser/API/log contract weakens opaque IDs, archive containment, original preservation, or Docker-only execution.
- FFmpeg/FFprobe use is confined to the new spec-approved conversion pipeline, uses `ProcessStartInfo.ArgumentList`, and all vendor-specific choices have cited evidence in `Plan.md`.

## Rollback Plan

- Remove the conversion service registrations and route mappings from `WebApp/WebApp/Program.cs`, `ArchiveEndpoints.cs`, and `DashboardEndpoints.cs`; then the feature accepts no new jobs while existing archive browsing remains available.
- Remove the Archive Browser menu action and Dashboard Jobs tab/component; Overview remains the current dashboard grid.
- Leave originals and successfully published sibling MP4s untouched, because the feature never replaces source files. Delete only identified temporary conversion files after confirming they belong to a failed job.
- Revert the `VideoConversionOptions` configuration and README feature row with the feature code. No database migration, persistent queue, or archive schema rollback is required.
