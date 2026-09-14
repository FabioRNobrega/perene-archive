# Plan: Resumable Archive Uploads

## Table of Contents

- [Summary](#summary)
- [Technical Approach](#technical-approach)
- [Component Breakdown](#component-breakdown)
- [Dependencies](#dependencies)
- [External / Vendor Documentation Evidence](#external--vendor-documentation-evidence)
- [Flow](#flow)
- [Risk Assessment](#risk-assessment)

## Summary

Replace the buffered multipart archive upload with a persistent, sequential, chunked protocol. The implementation extends the existing `ArchiveRootOptions` → `IArchiveService`/`ArchiveService` → `ArchiveEndpoints` → `ArchiveBrowser.razor` pattern while preserving category containment and opaque IDs.

## Technical Approach

Add `ArchiveUploadOptions` for a server-owned `<ArchiveRootOptions.Path>/.uploads` workspace, a 24-hour session TTL, cleanup cadence, maximum declared file size (initially at least 10 GB), and adaptive thresholds. Validate this directory as absolute, writable, contained in the archive root but disjoint from every `ArchiveCategory` root. `Program.cs` registers it and a cleanup `BackgroundService`, following the existing hosted-worker registrations.

Introduce server-only `ArchiveUploadSession` and a focused `IArchiveUploadService`/`ArchiveUploadService`; keep `ArchiveService` responsible for category/parent resolution, supported extensions, safe names, collision checks, and contained final-file publication. At session creation, resolve the opaque parent once, retain only server-only final destination information in `<upload-id>.json`, and create `<upload-id>.part` with atomic metadata writes. The upload service revalidates category/destination containment and final collisions at completion. Session access is by opaque GUID only and never serializes filesystem paths.

Replace `POST /api/archive/{category}/upload` with browser-safe session routes under `ArchiveEndpoints.cs`: create, list/query status for recovery, put the next chunk, complete, and cancel. Chunk handlers bind `HttpRequest.Body` rather than `IFormFile`/multipart model binding, use the offset and expected length contract, copy directly to the positioned `.part` stream, and update metadata only after exact byte confirmation. Serialize operations per upload ID so retries and the cleanup worker cannot race. Expected protocol conflicts return a retriable response with current session status; malformed/category/name/size failures map through existing `ArchiveException` handling without paths.

Define shared client DTOs for create requests and session status, including opaque ID, safe original name, category/parent context as needed for recovery, total/received bytes, selected chunk size, timestamps, and status—but no physical paths. The adaptive policy belongs in the server options/service, e.g. 5 MiB up to 100 MiB, 20 MiB through 1 GiB, 64 MiB through 10 GiB, and 128 MiB above that, bounded by an option. These are starting values, not client authority.

`ArchiveBrowser.razor` replaces `MultipartFormDataContent` and the 2 GiB constant with an upload coordinator/state model. It creates sessions, restores matching incomplete sessions for the displayed category/folder, uploads chunks serially, refreshes the listing only after commit, and exposes Resume and Cancel. An isolated `wwwroot/js/archiveUploadInterop.js` returns a stream reference for a selected `File.slice(offset, length)`; this avoids rereading/discarding already-acknowledged gigabytes. C# wraps each slice in `StreamContent`, creates a `HttpRequestMessage`, calls `SetBrowserRequestStreamingEnabled(true)`, and sends it. JS contains no session/progress decisions. On reselect it compares name and size to the persisted session before sending at `ReceivedBytes`.

Use an explicit complete call—not the final chunk—as the commit boundary. It checks declared bytes, metadata bytes, and `FileInfo.Length`, then atomically moves the temporary file into the original validated parent. Byte count detects incomplete transfer only; the deliberately excluded checksum means same-size corrupt replacement content cannot be detected. The worker uses `PeriodicTimer`, deletes only expired session pairs after acquiring the same lock, and is cancellation-aware.

The UI reuses the archive card's Bootstrap list-group status area but upgrades it with `<progress>`/ARIA progress semantics, textual acknowledged size, status badge, retry error, Resume/Cancel buttons, and responsive wrapping. Existing `_operationPending` remains for archive mutations; upload state is per item so independent sequential file sessions can be displayed without incorrectly claiming server acknowledgement. No new package or infrastructure is required.

## Component Breakdown

**Existing files to modify:**

- `WebApp/WebApp/Configuration/ArchiveRootOptions.cs` — add contained temporary-workspace validation or coordinate it with upload options.
- `WebApp/WebApp/Program.cs` — bind/validate `ArchiveUploadOptions` and register the upload service, lock/store dependencies, and cleanup worker.
- `WebApp/WebApp/Services/IArchiveService.cs` and `WebApp/WebApp/Services/ArchiveService.cs` — expose narrowly scoped, server-only upload destination validation/final publish support; retire `SaveUploadedFileAsync` after migration.
- `WebApp/WebApp/Endpoints/ArchiveEndpoints.cs` — replace the `IFormFile` multipart route with create/status/chunk/complete/cancel handlers and browser-safe mapping.
- `WebApp/WebApp.Client/Components/ArchiveBrowser.razor` — replace direct multipart loop and three-status `UploadItem` with session/recovery/progress UI and request streaming.
- `WebApp/WebApp.Client/Components/ArchiveBrowser.razor.css` — add only upload-progress geometry not expressible with Bootstrap.
- `README.md` — revise the Archive management supported-feature row to state resumable sequential uploads and supported media/document types.
- `WebApp.Tests/Services/ArchiveServiceTests.cs` and `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs` — migrate current single-request upload tests and add session safety/recovery coverage.

**New files to create:**

- `WebApp/WebApp/Configuration/ArchiveUploadOptions.cs` — validated workspace, TTL, size, cleanup, and chunk-policy configuration.
- `WebApp/WebApp/Models/ArchiveUploadSession.cs` and related internal status/value types — persistent server-only session contract.
- `WebApp/WebApp/Services/IArchiveUploadService.cs` and `ArchiveUploadService.cs` — session lifecycle, sequential writes, metadata persistence, completion, cancel, and recovery listing.
- `WebApp/WebApp/Services/ArchiveUploadCleanupWorker.cs` — periodic 24-hour-expiry removal.
- `WebApp/WebApp.Client/Models/ArchiveUploadCreateRequest.cs`, `ArchiveUploadSessionDto.cs`, and `ArchiveUploadStatus.cs` — browser-safe protocol contracts.
- `WebApp/WebApp.Client/wwwroot/js/archiveUploadInterop.js` — minimal selected-file slicing stream bridge.
- `WebApp.Tests/Services/ArchiveUploadServiceTests.cs` — temporary-directory unit tests for session lifecycle and race/expiry rules.
- `WebApp.Tests/Client/ArchiveUploadStateTests.cs` — pure client upload status/progress/reselection matching rules, if extracted from Razor for testability.

## Dependencies

- Docker Compose remains the only supported run/test workflow (`make docker-run`, `make test`); the writable `/archive` bind mount persists session files across container restarts.
- The existing archive category roots and `ArchiveService` opaque-ID/containment behavior remain the authority for target folders and supported extensions.
- Request streaming is available in Chromium over HTTPS and HTTP/2; local HTTP development must retain the framework's non-streaming fallback while keeping each request bounded to one chunk.

## External / Vendor Documentation Evidence

- [ASP.NET Core Blazor call web API](https://learn.microsoft.com/en-us/aspnet/core/blazor/call-web-api?view=aspnetcore-10.0) documents `HttpRequestMessage.SetBrowserRequestStreamingEnabled(true)` and its Chromium + HTTPS + HTTP/2 requirements. The design enables it per chunk and retains a bounded fallback for the current local HTTP workflow.
- [ASP.NET Core Blazor file uploads](https://learn.microsoft.com/en-us/aspnet/core/blazor/file-uploads?view=aspnetcore-10.0) recommends request streaming for large CSR uploads, explains `InputFile` selection replacement, and notes the non-streaming 2 GB/device-memory limitation. This supports explicit reselect-to-resume and per-slice transfer rather than a whole-file request.
- [Upload files in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-10.0) distinguishes buffered `IFormFile` uploads from unbuffered streaming and recommends a dedicated upload area plus untrusted-filename safeguards. This design replaces `IFormFile`, validates names in the existing service, and stores partials outside categories.
- [Implement the IHostedService interface](https://learn.microsoft.com/en-us/dotnet/core/extensions/timer-service) documents DI registration and cancellation/disposal for timer-based hosted services. It informs the cleanup worker registration and shutdown behavior.

## Flow

```mermaid
sequenceDiagram
    actor User
    participant Browser as ArchiveBrowser.razor
    participant Slice as archiveUploadInterop.js
    participant Api as ArchiveEndpoints
    participant Uploads as IArchiveUploadService
    participant Archive as IArchiveService
    participant Disk as /archive/.uploads
    User->>Browser: Select supported file
    Browser->>Api: Create upload session (name, size, opaque parent)
    Api->>Uploads: CreateAsync
    Uploads->>Archive: Validate category, parent, name, collision
    Uploads->>Disk: Persist metadata + .part
    Uploads-->>Browser: Upload ID, offset 0, adaptive chunk size
    loop Sequential acknowledged chunks
        Browser->>Slice: File.slice(offset, chunk size)
        Browser->>Api: PUT chunk, request streaming enabled
        Api->>Uploads: AppendNextChunkAsync
        Uploads->>Disk: Stream bytes and atomically update metadata
        Uploads-->>Browser: Acknowledged received bytes
    end
    Browser->>Api: Complete upload
    Api->>Uploads: CompleteAsync
    Uploads->>Archive: Revalidate and atomically publish final file
    Uploads-->>Browser: Completed session/listing
    Browser-->>User: Done; listing refreshes
```

## Risk Assessment

| Risk | Evidence | Mitigation |
| --- | --- | --- |
| Partial file becomes visible | Current `SaveUploadedFileAsync` publishes after a single request; interrupted transfers have no durable session. | Keep `.part` only in `.uploads`; publish only after explicit complete and length checks. |
| Memory/request limits break large files | Current UI caps `OpenReadStream` at 2 GiB and endpoint binds `IFormFile`. | Direct request-body chunks, adaptive bounded size, request streaming, and configured total limit. |
| Path exposure/traversal | `ArchiveService` already centralizes category containment and opaque IDs. | Do not put paths in DTO/metadata APIs; validate via service both at creation and commit. |
| Resume sends wrong file | Browser file access is lost after reload and byte count alone cannot prove identity. | Require user reselect, compare safe name + total size, communicate checksum limitation. |
| Cleanup deletes active work | TTL worker and chunk writes share durable files. | Per-session synchronization, refresh `LastActivityAt` only after acknowledged write, lock before expiry deletion. |
| Streaming unavailable in current development transport | Blazor request streaming requires HTTPS/HTTP/2 while Compose exposes HTTP 8080. | Enable streaming opportunistically; each chunk remains bounded and functionally valid through fallback. |
