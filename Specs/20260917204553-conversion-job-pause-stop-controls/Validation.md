# Validation: Conversion Job Pause and Stop Controls

## Table of Contents

- [Acceptance Criteria](#acceptance-criteria)
- [Test Cases](#test-cases)
- [Manual Verification](#manual-verification)
- [Definition of Done](#definition-of-done)
- [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | DTO and status-store tests prove `Paused` is active and `Stopped` is retained as terminal current-session history. |
| FR2 | Only the processing/paused card exposes keyboard-accessible Pause/Resume and Stop controls; queued/terminal cards do not. |
| FR3 | The three endpoints return only browser-safe DTOs, return 404 for unknown IDs, and return 409 for invalid transitions. |
| FR4–FR5 | Linux Docker verification proves a paused FFmpeg PID remains alive without progress, does not begin the next job, and resumes the same job rather than restarting it. |
| FR6 | Stop opens a keyboard-operable confirmation modal with the specified cleanup/source wording; cancellation sends no request. |
| FR7–FR9 | Confirmed stop terminates FFmpeg, deletes only its temporary file, preserves the original, publishes no partial MP4, retains `Stopped`, and cannot be overwritten by late progress/finalizing updates. |
| FR8 | The next queued job starts only after the active paused job resumes/completes or is stopped and cleanup releases the worker. |
| FR10 | Paused jobs continue polling with an unanimated state presentation; polling stops once all jobs are terminal. |
| FR11 | README documents pause/resume and confirmed stop cleanup. |

## Test Cases

**Unit tests:**

- `WebApp.Tests/Services/VideoConversionJobStatusStoreTests.cs` — verify only `Processing` can pause; only `Paused` can resume; stop is terminal; active-source, polling-state, and stale progress/finalization guards are correct.
- `WebApp.Tests/Services/VideoConversionProcessControllerTests.cs` — use a fake signal/process boundary to verify only the registered job can pause/resume/stop, duplicate/racing requests are rejected, and the worker-release signal is held while paused.
- `WebApp.Tests/Services/FfmpegVideoConversionGeneratorTests.cs` — cancellation/delete path removes the unique temp output and never invokes atomic publish; cancellation due to host shutdown remains distinct from user stop.
- `WebApp.Tests/Services/FfmpegVideoConversionProgressTests.cs` — paused/stopped jobs do not accept late progress that changes their state.

**Integration tests:**

- `WebApp.Tests/Endpoints/DashboardEndpointsTests.cs` — verify unknown ID and invalid state responses, successful control endpoint responses, and JSON never includes physical paths, temporary filenames, commands, or PIDs.
- Run `make test` using the isolated Docker Compose test stack.

## Manual Verification

1. Start the app with `make docker-run` and submit a sufficiently long conversion, then submit a second conversion so it is queued.
2. Open Dashboard → Jobs. Confirm only the processing card offers Pause and Stop.
3. Select Pause. Confirm state becomes Paused, progress no longer changes, FFmpeg remains alive in the container, and the queued job does not begin.
4. Select Resume. Confirm the same job returns to Processing and its progress continues without a new conversion output/temp file.
5. Select Stop. Confirm the modal explicitly warns about stopping the current process and deleting temporary files while preserving the source. Press Escape/Cancel once and confirm processing remains unchanged.
6. Confirm Stop. Verify the job remains as Stopped, the source is unchanged, its temporary sibling output is gone, no final MP4 was published, and the queued conversion starts afterward.
7. At mobile and desktop widths, verify buttons meet target size, modal focus/keyboard behavior works, screen-reader status is understandable, and dark/light tokens remain legible.

## Definition of Done

- Requirements, plan, and validation documentation are complete in this spec folder.
- Pause/resume preserves the same live FFmpeg process in the supported Docker Linux runtime.
- Stop confirmation, process termination, temp-file cleanup, source preservation, and retained stopped history are covered by automated tests and manual Docker validation.
- `make test` passes in the isolated Docker stack.
- API/UI remain opaque-ID-safe and disclose no filesystem path, PID, or command.
- README’s Current Supported Features table is updated.

## Rollback Plan

Remove the three Dashboard job-control endpoint mappings and the Jobs-card action/modal UI, then unregister the process controller and revert the added paused/stopped states. Existing jobs retain the original sequential completion/failure behavior; originals and any already-published MP4s remain untouched.
