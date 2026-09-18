# Requirements: Conversion Job Pause and Stop Controls

## Table of Contents

- [Problem Statement](#problem-statement)
- [User Stories](#user-stories)
- [Functional Requirements](#functional-requirements)
- [Non-Functional Requirements](#non-functional-requirements)
- [Out of Scope](#out-of-scope)
- [Open Questions](#open-questions)

## Problem Statement

`DashboardConversionJobsTab.razor` currently shows progress for an active video-conversion job but offers no control over the one FFmpeg process being run by `VideoConversionBackgroundWorker`. A user cannot temporarily free host resources or deliberately stop a mistaken conversion, and therefore cannot be assured that an interrupted process preserves the source, removes partial output, and remains visible in the current session.

## User Stories

- Given a conversion is processing, when I select Pause, then its FFmpeg process suspends, the card clearly reports Paused, and no queued job starts.
- Given a paused conversion, when I select Resume, then the same FFmpeg process continues from its preserved in-memory/temporary-file state and the job returns to Processing.
- Given an active or paused conversion, when I select Stop, then I must confirm a modal that explains the process will stop and generated temporary files will be deleted.
- Given I confirm Stop, when cleanup completes, then its job stays in the Jobs list as Stopped, its source remains unchanged, and no incomplete output is published.

## Functional Requirements

1. FR1 — The conversion status contract shall add `Paused` and terminal `Stopped` states. `Paused` remains active for duplicate-source and polling decisions; `Stopped` remains visible in the in-memory current-session history with a safe outcome message.
2. FR2 — `DashboardConversionJobsTab.razor` shall render accessible Bootstrap Pause and Stop controls only for the single job that is `Processing` or `Paused`; queued and terminal jobs shall not expose those actions. Pause changes to Resume for a paused job.
3. FR3 — `POST /api/dashboard/jobs/{id}/pause`, `POST /api/dashboard/jobs/{id}/resume`, and `POST /api/dashboard/jobs/{id}/stop` shall resolve only an existing current-session conversion job, reject an invalid state or non-active job with a client error, and never disclose a physical path, FFmpeg command, or process ID.
4. FR4 — Pausing a processing job shall suspend its currently running FFmpeg child process without terminating it, retain its temporary file, transition its state to `Paused`, and prevent `VideoConversionBackgroundWorker` from dequeuing another job until it is resumed or stopped.
5. FR5 — Resuming a paused job shall continue the same suspended FFmpeg process and transition it back to `Processing`; it shall not enqueue a replacement job, restart from zero, or create a second temporary output.
6. FR6 — Selecting Stop shall first open an accessible Bootstrap confirmation modal. Its content shall explicitly say that the current process will be stopped, temporary files generated for that job will be deleted, and the original source will not be changed. Confirm shall call the stop endpoint; Cancel shall have no server-side effect.
7. FR7 — Stopping a processing or paused job shall terminate its FFmpeg process, clean its owned temporary output in a `finally` path, prevent validation/publish, transition the status to `Stopped`, and leave queued jobs available to continue after the worker releases the stopped job.
8. FR8 — The existing single-reader, one-at-a-time queue behavior shall be retained: a paused job still occupies the worker, and only stopping or completing the active job permits the next queued job to start.
9. FR9 — Stop, pause, resume, worker shutdown, FFmpeg failure, and publishing must be race-safe: a late progress/finalization callback cannot overwrite `Paused` or `Stopped`, and a stop request that loses a completed-publish race returns a conflict without deleting a published MP4.
10. FR10 — The DTO/API and Jobs UI shall retain the existing two-second polling only for `Pending`, `Processing`, `Paused`, or `Finalizing` jobs. Paused presentation shall use a non-animated status cue and explain that conversion can be resumed later in the current running application session.
11. FR11 — `README.md` shall update the Video conversion feature row to describe current-session pause/resume and confirmed stop cleanup.

## Non-Functional Requirements

- Preserve the server-only archive containment, opaque-ID, source-preservation, atomic-publication, Docker-only, and `ProcessStartInfo.ArgumentList` boundaries of the existing conversion pipeline.
- The deployed container is Linux. Process suspend/resume must be implemented through a focused, testable server-side POSIX signal abstraction; no browser-visible PID or shell command is permitted.
- Pause/resume state is intentionally in-memory and applies only while the application remains running; application shutdown continues to cancel jobs and clean temporary output.
- Use Bootstrap 5.3 utilities/components, Bootstrap Icons, accessible labels, live status feedback, keyboard-operable modal controls, 40px minimum icon target, and dark/light design tokens.
- Follow existing xUnit and Docker Compose-only validation conventions.

## Out of Scope

- Pausing, stopping, reordering, or removing queued jobs.
- Persisting paused jobs across application restart or host/container restart.
- Checkpointing/restarting FFmpeg from a partial encoded output, parallel workers, priority controls, or controls for thumbnail, subtitle, cut, composition, or upload jobs.

## Open Questions

- None. This slice controls only the one currently running conversion process; queued jobs remain unstarted and wait in FIFO order.
