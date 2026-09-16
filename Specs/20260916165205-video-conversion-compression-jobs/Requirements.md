# Requirements: Video Conversion and Compression Jobs

## Table of Contents

- [Problem Statement](#problem-statement)
- [User Stories](#user-stories)
- [Functional Requirements](#functional-requirements)
- [Non-Functional Requirements](#non-functional-requirements)
- [Out of Scope](#out-of-scope)
- [Open Questions](#open-questions)

## Problem Statement

The Archive Browser currently treats only `.mp4`, `.webm`, `.mov`, and `.m4v` as videos in `ArchiveService`, so common archive formats such as MKV, AVI, and RMVB cannot be played or processed into a browser-safe version. The app already has bounded FFmpeg job queues and dashboard monitoring, but no user-initiated conversion workflow or unified dashboard view of its jobs. Users need to create a smaller, good-quality MP4 beside an original file without losing the original.

## User Stories

- Given an archive file with a supported conversion-source extension, when I choose Convert / Compress from that file's action menu, then the server probes it and queues the appropriate safe media action.
- Given a compatible MKV with H.264 video and AAC audio, when I start a job, then the worker remuxes it to a sibling MP4 without unnecessary video re-encoding.
- Given a video that is already browser compatible but unusually high bitrate for its resolution, when I start a job, then the worker creates a smaller sibling MP4 while preserving the original.
- Given one or more conversion jobs, when I open Dashboard and choose Jobs, then I can see each job's source name, selected action, state, outcome, and any safe diagnostic.
- Given a failed, cancelled by shutdown, or restarted in-memory job, when I inspect Dashboard Jobs, then no source is overwritten and no incomplete output is presented as complete.

## Functional Requirements

1. FR1 — `ArchiveService` and its browser-safe archive listing contract shall classify the configured conversion-source extensions `.mp4`, `.webm`, `.mov`, `.m4v`, `.avi`, `.mkv`, `.rmvb`, `.flv`, `.wmv`, `.mpeg`, `.mpg`, and `.3gp` as individually convertible video files, while retaining the current playable-video classification for browser-supported formats.
2. FR2 — `ArchiveBrowser.razor` shall expose a keyboard-accessible **Convert / Compress to MP4** action only for an individual convertible file; folders, non-video files, Trash items, and already pending/processing instances of the same source shall not offer a duplicate submission.
3. FR3 — `POST /api/archive/{category}/items/{id}/conversion` shall resolve the current opaque archive item ID server-side, reject an invalid/non-convertible/moved source with a client error, capture file identity (size and UTC last-write time), probe it, and return `202 Accepted` with an opaque job ID only if it was accepted by the bounded queue.
4. FR4 — The conversion probe shall obtain and parse only the first-release decision inputs: container format, primary video codec, primary audio codec, video bitrate, and video width/height. It shall reject unreadable or video-less inputs without queuing FFmpeg generation.
5. FR5 — A pure, independently tested decision planner shall select exactly one `MediaAction`: `Keep`, `Remux`, `ConvertAudio`, `CompressVideo`, or `FullTranscode`, based on the probe result. `Keep` shall complete without generating a duplicate; compatible video/audio in a non-MP4 container shall select `Remux`; compatible video plus incompatible audio shall select `ConvertAudio`; compatible MP4 streams with a bitrate above the configured resolution-based compression threshold shall select `CompressVideo`; all other valid inputs shall select `FullTranscode`.
6. FR6 — The background worker shall process one conversion job at a time using `ProcessStartInfo` and `ArgumentList`, revalidate the captured source identity before FFmpeg execution, preserve the original, write only a unique temporary sibling MP4, validate the completed output with a second probe, and atomically publish it only after validation.
7. FR7 — Generated MP4 output shall use browser-oriented H.264 video, AAC-LC audio, `yuv420p`, and 8-bit output where re-encoding is required; remuxing shall copy compatible streams; audio-only conversion shall copy compatible video and encode audio to AAC-LC; video compression shall preserve source dimensions and frame rate by default and use a high-quality CRF-based H.264 encode. The output filename shall be collision-safe and visibly derived from its source without exposing paths in the browser.
8. FR8 — The worker shall retain supported output metadata and streams where feasible: video duration and the selected primary video/audio stream are required; language/title metadata and chapters shall be mapped when FFmpeg can carry them to MP4; embedded subtitle streams shall not be silently discarded, and an unsupported subtitle-in-MP4 case shall fail safely with a user-visible diagnostic rather than publish a partial output.
9. FR9 — `Dashboard.razor` shall add Bootstrap-accessible Overview and Jobs tabs. Overview shall retain the current dashboard card grid; Jobs shall show in-memory conversion jobs in submission order with source file name, action, `Pending`/`Processing`/`Completed`/`Failed`/`Skipped` state, resulting browser-safe output reference where available, source/output size or saving when available, and safe error text. It shall poll only while a job is pending or processing and offer a manual refresh.
10. FR10 — Conversion job API DTOs and dashboard responses shall never contain an archive physical path, root-relative path, raw FFmpeg command, or unredacted process output; server logs shall use opaque job IDs and redacted diagnostics.
11. FR11 — The app shall surface queue saturation as an actionable unavailable response, safely clean temporary files after failure or shutdown cancellation, and document that job state is intentionally in-memory for this release; no queued or processing job is resumed after an application restart.
12. FR12 — `README.md` shall update Current Supported Features to describe conversion-source formats and the non-destructive MP4 conversion/compression workflow.

## Non-Functional Requirements

- Follow the established singleton bounded `Channel` queue, status-store, and sequential `BackgroundService` pattern used by composition jobs; do not add a database, scheduler, or external queue in this release.
- Respect the archive containment and opaque-ID boundary: all filesystem resolution, source verification, free-space checking, temp-file creation, and FFmpeg invocation remain server-only beneath the configured archive root.
- Check free space before an action that writes an output. The worker must reserve enough available capacity for an additional output and temporary file without touching the original.
- Preserve the archive's Docker-only execution model and use the image's existing `ffmpeg`/`ffprobe`; do not introduce a native workflow or new runtime package.
- Use Bootstrap 5.3 components/utilities, Bootstrap Icons, the existing dark/light tokens, live status semantics, responsive layouts, and 40px minimum icon target rules.
- Implement testable probe parsing, decision planning, naming, output verification, queue/status transitions, endpoint validation, and UI state models with xUnit conventions already used in `WebApp.Tests`.

## Out of Scope

- Batch selection, presets, manual CRF/bitrate/resolution controls, or converting every file in a folder.
- Replacing, deleting, or moving the source after conversion.
- Persistent job history, restart recovery, cancellation controls, priority/reordering, or parallel conversion workers.
- HDR/color-transfer preservation policy, frame-rate reduction, multi-audio-track selection UI, or a broad media-library expansion beyond the listed source extensions.
- Thumbnail/hover-preview/subtitle generation changes beyond their existing handling of newly published compatible MP4 files.

## Open Questions

- None for the first implementation slice. Compression thresholds, high-quality CRF value, and minimum predicted-saving threshold will be documented as explicit, tested defaults in `VideoConversionOptions` during implementation.
