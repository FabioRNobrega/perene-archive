# Requirements: Conversion Planning and Configurable Profiles

## Table of Contents

- [Problem Statement](#problem-statement)
- [User Stories](#user-stories)
- [Functional Requirements](#functional-requirements)
- [Non-Functional Requirements](#non-functional-requirements)
- [Out of Scope](#out-of-scope)
- [Open Questions](#open-questions)

## Problem Statement

`ArchiveBrowser.razor` currently sends an immediate conversion request, while `MediaConversionPlanner` and `FfmpegVideoConversionGenerator` select an opaque, largely hard-coded action. Users cannot see source media facts, choose an output resolution or size, or know the likely size before queueing. For high-bitrate archive files, this can result in an MP4 that remains nearly as large as the source. The conversion feature needs a server-validated planning stage and a decomposed, testable conversion pipeline so the chosen output policy—not hidden rules—drives FFmpeg.

## User Stories

- Given one convertible archive video, when I choose **Convert / Compress to MP4**, then I see a plan dialog with its safe-to-display input format, dimensions, duration, and source size before a job is created.
- Given a source that is 720p or smaller, when the plan opens, then its default output resolution preserves the source dimensions; given a source above 720p, the user can deliberately retain the source resolution or choose 720p/480p.
- Given I choose a preset or target output size, when the plan recalculates, then I see the selected output format/settings, an estimated MP4 size, and estimated savings before I confirm.
- Given I confirm a valid plan, when the server accepts it, then one background job executes exactly that plan without altering the original; given I cancel, no job is created.
- Given a TS, MKV, MP4, or another supported conversion source, when I open the planner, then the same resolution, target-size, quality-preset, and audio controls are available and server validation—not the filename extension—determines whether the plan can run.

## Functional Requirements

1. FR1 — Selecting **Convert / Compress to MP4** for one `IsConvertibleVideo` archive item in `ArchiveBrowser.razor` shall request a server-side plan preview rather than immediately enqueueing a conversion; dismissing the dialog shall have no conversion side effect.
2. FR2 — A plan-preview endpoint shall resolve only the submitted opaque category/item ID through `IArchiveService`, reject moved, unreadable, non-convertible, or duplicate-active sources, probe the file server-side, and return a browser-safe source summary and valid planning options without physical/root-relative paths or raw FFmpeg arguments.
3. FR3 — The planner dialog shall display source container, primary video/audio codec, dimensions, duration, source size, and the predicted output format, dimensions, video/audio bitrates, estimated MP4 size, and estimated saving. It shall clearly label the result as an estimate.
4. FR4 — The dialog shall provide one consistent profile model for every supported conversion-source extension: (a) Make compatible/preserve quality, (b) compress at original resolution, and (c) compress at 720p or 480p where downscaling is applicable. It shall offer quality presets and a target-size control; the target-size choice shall derive a video bitrate from duration, selected audio bitrate, and a documented MP4-overhead reserve.
5. FR5 — The default resolution rule shall preserve a source at 720p or below; sources above 720p shall default to an explicitly documented recommended compression profile while retaining an accessible original-resolution option. The UI must never upscale a source.
6. FR6 — Plan values received at job submission shall be parsed into a server-only, immutable conversion profile and fully validated against a bounded allowlist of resolutions, codecs, audio bitrates, target sizes/bitrates, and source dimensions. The endpoint shall recompute the estimate and reject tampered, stale, unsupported, or unsafe values rather than accepting client FFmpeg options.
7. FR7 — A confirmed plan shall enqueue at most one job for the selected source and record the requested profile plus the resolved execution strategy in current-session job status. Existing queue capacity, duplicate-source protection, pause/resume/stop, source-identity revalidation, temporary-file cleanup, atomic publication, and source preservation behavior shall remain intact.
8. FR8 — FFmpeg generation shall follow the confirmed plan: compatible preservation may remux or copy compatible streams; compression/re-encoding shall produce H.264 video, AAC-LC audio, `yuv420p`, and MP4 output; requested downscaling shall use an aspect-ratio-preserving, non-upscaling filter; interlaced sources selected for re-encoding shall use the documented deinterlacing policy.
9. FR9 — The conversion implementation shall be split into focused SOLID-aligned responsibilities: media probing, profile catalog/defaulting, estimate calculation, profile validation/strategy selection, FFmpeg argument construction, process execution/publication, and queue/status orchestration. Endpoint and UI code shall depend on focused contracts rather than the current multi-responsibility `VideoConversionServices.cs` implementation.
10. FR10 — Dashboard conversion-job rows shall identify the chosen user-facing profile/output resolution and retain existing safe source name, status, progress, output-size, pause/resume, and stop behavior. Diagnostics, APIs, DTOs, UI, and logs shall continue to exclude source paths, command lines, PIDs, and unredacted FFmpeg output.
11. FR11 — Converted output containing the source's embedded subtitle streams is out of scope for this release. The plan shall state that embedded subtitle handling is unavailable; a conversion must fail safely or omit only as an explicit, documented conversion policy, never claim browser subtitle preservation.
12. FR12 — `README.md` shall update the Current Supported Features conversion row to describe per-file planning, configurable resolution/size profiles, estimated output size, and the deferred embedded-subtitle limitation.

## Non-Functional Requirements

- Preserve the private archive's server-only containment and opaque-ID boundaries; no client-provided path, filter expression, or FFmpeg command is accepted.
- Reuse the Docker-only runtime, Dockerfile-provided `ffmpeg`/`ffprobe`, bounded single-reader queue, and current-session in-memory job persistence. No database, browser processing, new runtime package, or parallel worker is introduced.
- Keep the existing Bootstrap 5.3 / Bootstrap Icons design system: keyboard-operable modal controls, labelled form fields, live estimate/error feedback, 40px minimum icon targets, responsive layout, dark/light tokens, and reduced-motion-safe behavior.
- The estimate for a target-size/bitrate plan must be deterministic and unit tested. Quality-preset estimates must disclose that media complexity may make the actual output differ.
- Follow nullable-aware C#, client/server separation, focused interfaces, xUnit conventions, and Docker Compose-only `make test` validation.

## Out of Scope

- Batch/folder conversion or joining Part 1 and Part 2 into one output.
- OCR, extraction, conversion, or browser presentation of embedded DVB or other subtitle streams.
- Arbitrary FFmpeg flags, arbitrary custom dimensions, arbitrary codec selection, GPU encoders, HDR policy, frame-rate selection, or multiple audio-track selection.
- Replacing/deleting/moving the source, persistent job history/restart recovery, queue reordering, or parallel conversion.

## Open Questions

- Resolved: sources above 720p default to the recommended balanced 720p profile (2.5 Mb/s video, 128 kb/s AAC audio, with a 3% MP4 container reserve); original resolution and 480p remain explicit alternatives.
- Resolved: plans for sources with embedded subtitle streams are rejected before queueing. Subtitle extraction/preservation remains out of scope, so the application never claims browser subtitle preservation.
