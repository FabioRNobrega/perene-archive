# Plan: Archive Multi-Select Move

## Table of Contents

- [Plan: Archive Multi-Select Move](#plan-archive-multi-select-move)
  - [Summary](#summary)
  - [Technical Approach](#technical-approach)
  - [Component Breakdown](#component-breakdown)
  - [Dependencies](#dependencies)
  - [External / Vendor Documentation Evidence](#external--vendor-documentation-evidence)
  - [Flow](#flow)
  - [Risk Assessment](#risk-assessment)

## Summary

`ArchiveBrowser.razor` gains Dolphin-style Ctrl+Click/Shift+Click multi-select over its existing card grid, indicated by a green card border, and a selection toolbar with a "Move" action that enqueues one combined batch-move job through a new `BatchMove` addition to the already-implemented archive-mutation job pipeline (`Specs/20260922122650-archive-move-delete-progress`).

## Technical Approach

**Selection state and interaction (client).** Blazor's `MouseEventArgs` already exposes `CtrlKey`/`ShiftKey` (`Microsoft.AspNetCore.Components.Web`), so no JS interop is needed to detect modifiers — this follows the codebase's existing rule that custom JavaScript is reserved for what Blazor can't do natively. `ArchiveBrowser.razor` adds `_selectedForMoveIds` (`HashSet<string>`) and `_selectionAnchorId` (`string?`) fields, distinct from the existing `IsSelected`/"currently playing" concept so the two states never collide. The card's outer clickable surface (currently a plain `@onclick="() => ActivateAsync(item)"` button, `WebApp/WebApp.Client/Components/ArchiveBrowser.razor:356-359`) is wrapped by a new handler `HandleCardClick(ArchiveItemDto item, MouseEventArgs args)` that:
- Ctrl (no Shift): toggles `item.Id` in `_selectedForMoveIds`, sets `_selectionAnchorId = item.Id`, returns without calling `ActivateAsync`.
- Shift (Ctrl not required — confirmed: plain Shift+Click extends the range; Ctrl+Shift+Click works identically since Ctrl is ignored once Shift is held): if `_selectionAnchorId` is set, finds both indices in the current `_filteredItems` order and selects the inclusive range, replacing `_selectedForMoveIds`; otherwise behaves like a Ctrl+Click.
- Neither modifier: unchanged — calls `ActivateAsync(item)` exactly as today.

This mirrors the standard file-manager convention (Ctrl = toggle one, Shift = extend range from last anchor) and reuses the already-existing category-root exclusion: category-root items never render a card in the grid (they're the folder being browsed, not an item in it), so FR4's exclusion is automatically satisfied by construction — no extra client-side check is needed.

A selected card's outer `div.card` gets a `border-success border-2` class (decided during implementation instead of a separate per-card checkbox: the green border alone is a clear, non-conflicting visual state, and it does not change the existing "Selected"/currently-playing badge, which stays at `top-0 start-0`, since the two states never render for the same reason at the same time in a way that collides). Ctrl+Click and Shift+Click remain the only ways to build a selection; there is no touch/keyboard-free equivalent control.

A new toolbar section (Bootstrap `d-flex align-items-center gap-2 border-bottom p-2`) renders when `_selectedForMoveIds.Count > 0`, above the grid, showing `"@_selectedForMoveIds.Count selected"`, a `btn-primary` "Move" button (`bi-folder-symlink`), and a `btn-outline-secondary` "Clear" button (`bi-x-lg`) that empties `_selectedForMoveIds`. It stays non-modal, matching the non-blocking assumption already made for the move/delete progress popup. The "Move" button carries `disabled="@(_activeJob is not null)"`, the same condition that already gates the progress popup's visibility (`ArchiveBrowser.razor:690`), so only one move/delete job — and one progress popup — is ever in flight at a time; the user must let it finish (or dismiss it) before starting the next move.

**Batch move picker.** `ArchiveMovePicker.razor` (`WebApp/WebApp.Client/Components/ArchiveMovePicker.razor`) is reused unmodified except for its header title: it currently hardcodes `Move @MovingItem.Name` (`ArchiveMovePicker.razor:8`). A new optional `[Parameter] public string? TitleOverride { get; set; }` lets the header render `TitleOverride ?? $"Move {MovingItem.Name}"`; `ArchiveBrowser.razor` passes `TitleOverride="@($"Move {_selectedForMoveIds.Count} items")"` and the first selected item as `MovingItem` (kept required, since `MovingItem` isn't otherwise read for anything but the title) when opening the picker for a batch. `OnConfirm` is wired to a new `BatchMoveAsync((string DestinationCategory, string? DestinationFolderId) destination)` method paralleling the existing single-item `MoveAsync` (`ArchiveBrowser.razor:1677`), which calls the existing generic `EnqueueMutationAsync` helper (`ArchiveBrowser.razor:1700`) against the new batch endpoint — so the existing `_activeJob`/`StartJobPolling`/`PollJobAsync` progress-popup flow (`ArchiveBrowser.razor:1700-1775`) is reused completely unchanged.

**Server: batch endpoint and validation.** A new `BatchMoveArchiveItemsRequest(IReadOnlyList<string> ItemIds, string DestinationCategory, string? DestinationFolderId)` DTO (in `WebApp.Client/Models/`, alongside the existing `MoveArchiveItemRequest`) backs a new route `PATCH /api/archive/{category}/items/location` registered in `MapArchiveEndpoints` (`WebApp/WebApp/Endpoints/ArchiveEndpoints.cs:12-57`), placed next to the existing per-item `Move` route registration. Its handler follows the exact shape of the existing `Move` handler (`ArchiveEndpoints.cs:332-339`), calling a new `IArchiveService.BatchMove(...)` through the same `EnqueueMutation` helper (`ArchiveEndpoints.cs:357-388`) — no new error-mapping logic is needed since `EnqueueMutation` already maps `ArchiveValidationException`/`ArchiveForbiddenException`/`ArchiveNotFoundException`/`ArchiveConflictException`/IO exceptions generically from any `Func<ArchiveMutationJob>`.

`ArchiveService.BatchMove` (`WebApp/WebApp/Services/ArchiveService.cs`) resolves the category once, then resolves and validates every item ID with the same per-item logic `Move` already uses inline (`ArchiveService.cs:180-204`: `ResolveItem`, `EnsureNotCategoryRoot`, `ContainedPath`, `Exists` conflict check, `IsWithinOrSame` self-move-into-descendant check) — extracted into a small private helper (e.g. `ValidateAndPlanMove(ArchiveCategory category, ArchiveItemEntry item, ArchiveCategory destinationCategory, ArchiveItemEntry destinationFolder)` returning a `(string Source, string Destination, bool IsFolder, int FileCount)` plan or throwing) so both the existing single-item `Move` and the new `BatchMove` share it rather than duplicating the validation rules. `BatchMove` calls this helper once per requested item ID, collecting all plans before touching the filesystem; if any single item fails validation, the exception propagates immediately and nothing is enqueued (all-or-nothing), matching FR9. On success it returns one `ArchiveMutationJob` with `Kind = ArchiveMutationKind.BatchMove`, `TotalItems` = the sum of each plan's `FileCount`, and the full list of plans attached (see model change below).

**Job model.** `ArchiveMutationJob` (`WebApp/WebApp/Models/ArchiveMutationJob.cs`) is a `record` with required positional parameters (`JobId, Kind, SourcePath, DestinationPath, IsFolder, TotalItems, Label`). This plan adds one new optional trailing parameter `IReadOnlyList<ArchiveMutationBatchEntry>? BatchEntries = null` (a new small record `ArchiveMutationBatchEntry(string SourcePath, string DestinationPath, bool IsFolder, int FileCount)` in the same file) so existing `Move`/`MoveToTrash`/`EmptyTrash` construction sites are unaffected (they simply don't set it). For a `BatchMove` job, `SourcePath`/`DestinationPath` are left as the destination folder's physical path (used only for the job's own bookkeeping, never surfaced to the browser) and `BatchEntries` holds the real per-item plans.

**Executor and worker.** `ArchiveMutationExecutor` (`WebApp/WebApp/Services/ArchiveMutationExecutor.cs`) gains `BatchMoveAsync(ArchiveMutationJob job, Action<int> reportProgress, CancellationToken)`, which iterates `job.BatchEntries!` in order, reusing the existing private `MoveFolderRecursive`/single-`File.Move` logic per entry (the same code paths `MoveEntryAsync` already calls), but with `reportProgress` driven by a running total across *all* entries (not reset between entries) so the popup shows one combined "X of Y" across the whole batch, satisfying FR11. If an entry throws `IOException`/`UnauthorizedAccessException`, the method stops immediately (remaining entries are not attempted) and returns `ArchiveMutationResult.Failed(...)` naming how many of the total files were moved before the stop — mirroring the existing single-item catch block's message shape (`ArchiveMutationExecutor.cs:78-82`). `ArchiveMutationBackgroundWorker` (`WebApp/WebApp/Services/ArchiveMutationBackgroundWorker.cs:31-40`) adds one more arm to its existing `switch (job.Kind)` for `ArchiveMutationKind.BatchMove => await executor.BatchMoveAsync(...)`.

**Client model/enum.** `ArchiveMutationKind` (`WebApp/WebApp.Client/Models/ArchiveMutationKind.cs`) gains `BatchMove`. `JobActionLabel` (`ArchiveBrowser.razor:1783`) gains a matching `BatchMove => "Moving"` arm (same label text as `Move` today, since the UI already appends `@job.Label`, which will read the destination folder's display context or a synthesized "N items" label carried on the job's `Label` field — `ArchiveService.BatchMove` sets `Label` to `"{N} items"` for the job, same as `EmptyTrash` already sets a synthetic `"Trash"` label today).

This entire design reuses the existing single-responsibility split (`ArchiveService` = validation/path resolution, `ArchiveMutationExecutor` = file I/O, `ArchiveMutationBackgroundWorker`/`IArchiveMutationJobQueue`/`IArchiveMutationJobStatusStore` = scheduling/status) with zero new services — it only adds one new job kind and one new executor method to the pipeline `Specs/20260922122650-archive-move-delete-progress` already built, per the general guidance to extend existing patterns rather than introduce a parallel structure.

## Component Breakdown

**Existing files to modify:**

- `WebApp/WebApp.Client/Components/ArchiveBrowser.razor` — add `_selectedForMoveIds`/`_selectionAnchorId` state, `HandleCardClick`, the green-border selected-card style, the selection toolbar, `BatchMoveAsync`, clearing selection in `LoadAsync`, and a `BatchMove` arm in `JobActionLabel`.
- `WebApp/WebApp.Client/Components/ArchiveMovePicker.razor` — add optional `TitleOverride` parameter, use it in the header.
- `WebApp/WebApp.Client/Models/ArchiveMutationKind.cs` — add `BatchMove`.
- `WebApp/WebApp/Endpoints/ArchiveEndpoints.cs` — register the new `PATCH /api/archive/{category}/items/location` route and its handler.
- `WebApp/WebApp/Services/ArchiveService.cs` (and its `IArchiveService` interface) — extract shared per-item move validation into a private helper, add `BatchMove(...)`.
- `WebApp/WebApp/Models/ArchiveMutationJob.cs` — add the optional `BatchEntries` parameter and the new `ArchiveMutationBatchEntry` record.
- `WebApp/WebApp/Services/ArchiveMutationExecutor.cs` (and `IArchiveMutationExecutor`) — add `BatchMoveAsync`.
- `WebApp/WebApp/Services/ArchiveMutationBackgroundWorker.cs` — route `BatchMove` jobs to the new executor method.

**New files to create:**

- A `BatchMoveArchiveItemsRequest` DTO — added to the existing file that already declares `MoveArchiveItemRequest` (or a sibling file in `WebApp.Client/Models/` if that record is one-per-file, matching this repo's existing convention — confirm at implementation time by checking the existing file).

## Dependencies

- Builds directly on the already-implemented `IArchiveMutationJobQueue`/`ArchiveMutationJobQueue`, `IArchiveMutationJobStatusStore`/`ArchiveMutationJobStatusStore` from `Specs/20260922122650-archive-move-delete-progress` — no changes needed there, since a `BatchMove` job is still exactly one `ArchiveMutationJob` flowing through the existing one-job-at-a-time queue.
- No new runtime package, external service, or infrastructure dependency.

## External / Vendor Documentation Evidence

Not applicable in the vendor-specific-decision sense — this feature uses only `Microsoft.AspNetCore.Components.Web.MouseEventArgs`'s existing `CtrlKey`/`ShiftKey` properties and minimal API routing/binding patterns already used identically elsewhere in this codebase (`ArchiveEndpoints.cs`), so no new ASP.NET Core/Blazor API surface is introduced that would need fresh Microsoft Learn verification beyond what the existing move/delete-progress spec already established.

## Flow

```mermaid
sequenceDiagram
    participant User
    participant ArchiveBrowser as ArchiveBrowser.razor
    participant Picker as ArchiveMovePicker.razor
    participant API as PATCH /api/archive/{category}/items/location
    participant Service as ArchiveService.BatchMove
    participant Queue as IArchiveMutationJobQueue
    participant Worker as ArchiveMutationBackgroundWorker
    participant Executor as ArchiveMutationExecutor.BatchMoveAsync

    User->>ArchiveBrowser: Ctrl+Click item A
    ArchiveBrowser->>ArchiveBrowser: _selectedForMoveIds = {A}, anchor = A
    User->>ArchiveBrowser: Shift+Click item D
    ArchiveBrowser->>ArchiveBrowser: _selectedForMoveIds = {A..D}
    User->>ArchiveBrowser: Click "Move" (toolbar)
    ArchiveBrowser->>Picker: open with TitleOverride="Move 4 items"
    User->>Picker: choose destination, Confirm
    Picker->>ArchiveBrowser: OnConfirm(destinationCategory, destinationFolderId)
    ArchiveBrowser->>API: PATCH items/location {ItemIds, DestinationCategory, DestinationFolderId}
    API->>Service: BatchMove(category, itemIds, destCategory, destFolderId)
    Service->>Service: validate each item (conflict / self-move / root checks)
    Service-->>API: ArchiveMutationJob(Kind=BatchMove, TotalItems=sum, BatchEntries=[...])
    API->>Queue: TryEnqueue(job)
    API-->>ArchiveBrowser: 202 Accepted + ArchiveMutationJobDto (Pending)
    ArchiveBrowser->>ArchiveBrowser: _activeJob = job; StartJobPolling()
    Worker->>Queue: DequeueAsync()
    Worker->>Executor: BatchMoveAsync(job, reportProgress)
    loop each BatchEntries entry
        Executor->>Executor: move file/folder, reportProgress(runningTotal)
    end
    Executor-->>Worker: Success or Failed(diagnostic)
    Worker->>Worker: statusStore.MarkCompleted/MarkFailed
    ArchiveBrowser->>API: GET /api/archive/jobs (poll every 2s)
    API-->>ArchiveBrowser: job status (Processing → Completed/Failed)
    ArchiveBrowser->>ArchiveBrowser: refresh listing, show popup result
```

## Risk Assessment

| Risk | Evidence | Mitigation |
| --- | --- | --- |
| Shift-range selection computed against `_filteredItems` can shift if the user types into the filter box mid-selection, producing a surprising range | `_filteredItems` (`ArchiveBrowser.razor:772`) is a filtered projection of `_listing.Items` recomputed from `_filterText` | Range is computed fresh at Shift+Click time against the *current* `_filteredItems` order, matching how a real file manager behaves when its view re-sorts/filters; no stale-index bug since indices aren't cached across renders. |
| A green `border-success` selection outline could be visually ambiguous against the existing `border-primary` "currently playing" outline | `ArchiveBrowser.razor:279-284` already renders a `border-primary border-2` state for the currently-playing item | The two border colors are mutually exclusive per card (primary takes precedence when both would apply) and visually distinct (blue vs. green), verified visually during implementation/manual QA. |
| Extracting shared validation out of `ArchiveService.Move` into a helper risks subtly changing single-item Move behavior | `ArchiveService.Move` (`ArchiveService.cs:180-204`) is the only other caller of the extracted logic | Existing `WebApp.Tests` coverage for `ArchiveService.Move`/`Rename`/`MoveToTrash` (under `WebApp.Tests/Services`) must keep passing unmodified after the extraction — the refactor is behavior-preserving by construction (same checks, same order, same exceptions). |
| A very large batch (many large folders) held as `BatchEntries` in an in-memory job could be a large object graph | `ArchiveMutationJobStatusStore` already holds jobs in memory only (per FR8 of the move/delete-progress spec) with no persistence | Store only path/count tuples (no file contents, no large buffers) per entry — same memory profile as today's `EmptyTrash` job already handles for an entire trash root. |
