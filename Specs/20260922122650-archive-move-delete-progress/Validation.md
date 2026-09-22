# Validation: Archive Move & Delete Progress

## Table of Contents

- [Validation: Archive Move & Delete Progress](#validation-archive-move--delete-progress)
  - [Acceptance Criteria](#acceptance-criteria)
  - [Test Cases](#test-cases)
  - [Manual Verification](#manual-verification)
  - [Definition of Done](#definition-of-done)
  - [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | `ArchiveService.Move`/`MoveToTrash`/`EmptyTrash` no longer call `Directory.Move`/`File.Move`/`Directory.Delete`/`File.Delete` directly inside the request; those calls occur only inside `ArchiveMutationExecutor`, invoked from `ArchiveMutationBackgroundWorker`. |
| FR2 | `PATCH .../location`, `DELETE .../items/{id}`, and `DELETE .../items` return an `ArchiveMutationJobDto` with `State == Pending` immediately, before the underlying filesystem operation has necessarily finished; the response no longer includes the final `ArchiveListingDto`. |
| FR3 | `ArchiveService.Rename` still executes its `MovePhysical`/rename call synchronously inside `PATCH .../name` and still returns `ArchiveListingDto` directly, unchanged from today. |
| FR4 | `ArchiveMutationBackgroundWorker` is registered as a hosted service in `Program.cs` and processes queued jobs sequentially; a queued job's status transitions from `Pending` to `Processing` to a terminal state without the HTTP request thread performing the I/O. |
| FR5 | For a folder with N files sent to `EmptyTrash` or moved, `GET /api/archive/jobs` reports `TotalItems == N` before `ProcessedItems` starts incrementing; for a single file, `TotalItems == 1`. |
| FR6 | `EmptyTrash`'s executor path deletes files individually (not via one opaque `Directory.Delete(recursive:true)` call) and `ProcessedItems` increases monotonically as each file is removed, reaching `TotalItems` at completion; the trash root directory structure itself is empty afterward. |
| FR7 | A single-file `Move`/`MoveToTrash` job reports `TotalItems == 1` and progresses `0 -> 1`; a folder `Move`/`MoveToTrash` job's `ProcessedItems` increases per file moved and the destination folder mirrors the source folder's structure once `Completed`, with the source subtree removed. |
| FR8 | `ArchiveMutationJobStatusStore.GetAll()` exposes `JobId`, `Kind`, `State`, `TotalItems`, `ProcessedItems`, `Label`, and `Diagnostic` (null unless `Failed`) for every seeded job in the current process lifetime. |
| FR9 | `GET /api/archive/jobs` (and/or `GET /api/archive/jobs/{jobId}`) returns 200 with the current job list/single job; an unknown `jobId` on the single-job route returns 404. |
| FR10 | Triggering Move/Delete/Empty Trash from `ArchiveBrowser.razor` shows a Bootstrap progress popup with the item's label, a status line, and a `progress-bar` reflecting `ProcessedItems`/`TotalItems`; a `PeriodicTimer`-driven poll against the jobs endpoint runs every 2 seconds while the job is `Pending`/`Processing` and stops once terminal. |
| FR11 | On `Completed`, the popup shows a success state, the archive grid re-fetches and reflects the moved/deleted item(s) (e.g. the moved item disappears from the source folder and appears in the destination; an emptied-trash item disappears from Trash), and the popup can be dismissed. |
| FR12 | On `Failed`, the popup shows the job's `Diagnostic` text and a close control; the archive grid still re-fetches and shows the true partial on-disk state. |
| FR13 | Simulating a mid-operation failure (e.g. an `IOException` thrown after N of M files have moved, in a unit test with a fake executor) leaves the already-processed files in their new location/deleted, and the job's `Diagnostic` communicates a partial result rather than claiming full success or throwing an unhandled exception out of the worker. |
| FR14 | Enqueuing a second Move/Delete job while one is already `Processing` succeeds (returns `Pending`) rather than being rejected, and it is processed after the first job completes, verified by `IArchiveMutationJobQueue.ActiveCount` transitioning as expected. |
| FR15 | Inspecting the JSON payload of every new/changed endpoint response confirms no field contains a physical or root-relative filesystem path — only opaque ids, labels, counts, and state. |

## Test Cases

**Unit tests:**
- `WebApp.Tests/Services/ArchiveMutationExecutorTests.cs`: against a temp directory (created/cleaned up per test, matching the temp-directory pattern already used by `ArchiveServiceTests.cs`), verify `EmptyTrashAsync` deletes all files/subfolders and reports progress counts matching the number of files created; verify `MoveAsync`/`MoveToTrashAsync` for both a single file and a multi-file nested folder produce the correct destination structure and report progress incrementally; verify a simulated failure (e.g. a locked/read-only file) surfaces a diagnostic without corrupting already-moved files.
- `WebApp.Tests/Services/ArchiveMutationJobQueueTests.cs`: verify `TryEnqueue`/`DequeueAsync`/`ActiveCount` behave like `CompositionJobQueueTests` (if one exists) — enqueue increments `ActiveCount`, dequeue+`Complete()` decrements it, queue drains in FIFO order.
- `WebApp.Tests/Services/ArchiveMutationJobStatusStoreTests.cs`: verify `Seed`/`MarkProcessing`/`ReportProgress`/`MarkCompleted`/`MarkFailed`/`GetAll` transitions match expectations, including that `ReportProgress` calls after `MarkCompleted`/`MarkFailed` don't regress the terminal state (defensive ordering check).
- `WebApp.Tests/Services/ArchiveServiceTests.cs` (extend existing file): verify `Move`/`MoveToTrash`/`EmptyTrash` still perform all existing validation (conflict, self-move, trash-only guard for `EmptyTrash`) synchronously and return a job descriptor with the correct `TotalItems` for a fixture folder, without touching the filesystem themselves.

**Integration tests:**
- `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs` (extend existing file) using `WebApplicationFactory`: `PATCH .../location`, `DELETE .../items/{id}`, `DELETE .../items` return `202`/`200` with an `ArchiveMutationJobDto` in `Pending` state immediately; polling `GET /api/archive/jobs` after the in-process background worker has had time to run (test-friendly small fixture, e.g. 2-3 files) eventually reports `Completed` with `ProcessedItems == TotalItems`, and the corresponding `GET /api/archive/{category}/items` listing reflects the change.
- ⚠️ TODO: an end-to-end "large folder" integration test (hundreds of files) does not exist yet but should be added once the executor is implemented, to catch any performance regression versus the current single-call `Directory.Move`/`Directory.Delete` before this change ships.

## Manual Verification

1. `make docker-run-bg` to start the stack, then open the app at the LAN/local URL reported by `make get-url`.
2. In the Archive Browser, create a test folder with a large number of files (e.g. a few thousand small files, or a few large video files) under a writable category.
3. Move that folder to a different category/folder. Confirm a progress popup appears immediately with the folder's name, a status line, and a progress bar that visibly advances (not stuck at 0% or jumping straight to 100%) while `make docker-logs` shows no errors.
4. Confirm the popup reaches a "Completed" state, the folder now appears at the destination, and it no longer appears at the source, without a full page reload.
5. Soft-delete a similarly large folder to Trash; confirm the same popup behavior appears for `MoveToTrash`.
6. With Trash containing that large folder (and ideally other trash items), click Empty Trash; confirm the progress popup shows increasing `ProcessedItems`/`TotalItems` as it works through the trash root, and Trash appears empty once `Completed`.
7. Force a failure case (e.g. attempt a move that will hit a name conflict, or make a destination temporarily read-only if feasible in the dev container) and confirm the popup shows a `Failed` state with a readable diagnostic instead of an unhandled error or an indefinitely-stuck spinner.
8. While a large move/delete job is `Processing`, confirm the rest of the Archive Browser (browsing other folders, triggering thumbnails) still responds — the app is not frozen elsewhere while the job runs.
9. Run `make test` and confirm all existing and new tests pass.

## Definition of Done

- Requirements, Plan, and Validation docs in this spec folder are complete and internally consistent.
- All existing tests still pass, and new tests cover the executor, queue, status store, and endpoint changes per the Test Cases above.
- `README.md`'s `## Current Supported Features` table has a new/updated row describing progress reporting for large archive move/delete operations.
- The Archive Browser's progress popup follows the Bootstrap modal/progress-bar conventions already used elsewhere in this codebase (`DashboardConversionJobsTab.razor`), with no hand-authored SVGs or bespoke color CSS.
- No physical or root-relative filesystem path appears in any new/changed client-facing payload (verified by inspecting the DTOs and a manual network-tab check during Manual Verification).
- `make docker-run` / `make test` continue to be the only documented way to run/build/test the app; no native `dotnet run`/`dotnet test` workflow was introduced.

## Rollback Plan

- The change is additive at the API-shape level in a way that can be reverted by reverting the `ArchiveEndpoints.cs`/`ArchiveService.cs` changes back to their synchronous form and removing the new `ArchiveMutation*` service registrations from `Program.cs` — there is no persisted data format or migration to undo, since job status is in-memory only.
- If the new background worker misbehaves in production use (e.g. jobs stop draining), `make docker-down` followed by reverting to the previous commit and `make docker-run-bg` restores the prior synchronous-but-blocking behavior with no data loss, since the underlying files themselves are untouched by this change — only how the move/delete work is scheduled and reported changes.
