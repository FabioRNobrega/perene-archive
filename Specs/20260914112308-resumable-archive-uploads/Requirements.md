# Requirements: Resumable Archive Uploads

## Table of Contents

- [Problem Statement](#problem-statement)
- [User Stories](#user-stories)
- [Functional Requirements](#functional-requirements)
- [Non-Functional Requirements](#non-functional-requirements)
- [Out of Scope](#out-of-scope)
- [Open Questions](#open-questions)

## Problem Statement

`ArchiveBrowser.razor` currently posts each selected file as one buffered multipart request to `POST /api/archive/{category}/upload`. `ArchiveEndpoints.UploadAsync` receives an `IFormFile`, and `ArchiveService.SaveUploadedFileAsync` writes it straight to its final destination after a request-scoped temporary write. The 2 GiB client limit, loss of all progress on interruption, and lack of acknowledged progress make this unsuitable for reliably moving 100 MB to 10 GB archive files over a local NAS connection.

## User Stories

- Given I upload a supported file to any writable archive folder, when its chunks reach the server, then I see progress based on bytes the server has acknowledged.
- Given an upload is interrupted, when I return to its folder, then I see a failed resumable upload and can reselect the original local file to continue from the persisted offset or cancel it.
- Given every byte has been received, when I explicitly complete the upload, then the server validates its byte count and atomically makes it visible in the chosen archive folder.
- Given I abandon an incomplete upload for 24 hours, when cleanup runs, then its metadata and temporary content are removed without affecting archive items.

## Functional Requirements

1. FR1 — Replace the one-request `ArchiveBrowser.razor` upload workflow with resumable sessions for every `ArchiveCategory` whose existing `CanCreateFolder` capability permits uploads, including nested folders; Trash remains forbidden.
2. FR2 — A create-session API validates the category, opaque parent ID, safe supported file name, declared total byte count, and final-name collision before creating an opaque upload ID and returning browser-safe session status plus a server-selected chunk size.
3. FR3 — The server persists each session's metadata and incomplete file under a dedicated server-only temporary-upload directory beneath the configured archive root, never under a browseable category folder or a static-file root; sessions survive application restart.
4. FR4 — The client uploads one sequential chunk at a time using its opaque upload ID and byte offset. The server streams the request body directly to the session file, accepts only the exact next offset, verifies the actual received chunk length and total-byte boundary, persists the new acknowledged offset, and rejects gaps, overlaps, stale/expired/completed IDs, and malformed requests without exposing paths.
5. FR5 — Chunk selection is adaptive and deterministic: it uses a small chunk for small files, medium chunks for approximately 100 MB–1 GB files, and progressively larger bounded chunks for 1 GB–10 GB-plus files. The selected size is returned by the server and is configurable/testable rather than hard-coded in Razor.
6. FR6 — The Interactive WebAssembly client uses an isolated browser-file slice bridge and `HttpRequestMessage.SetBrowserRequestStreamingEnabled(true)` for each chunk where the browser/protocol supports request streaming; it continues through the documented browser fallback without attempting to buffer the whole file.
7. FR7 — The UI renders per-file overall name, acknowledged bytes/total bytes, percentage, transfer rate/estimated remaining time when calculable, and clear Pending/Uploading/Interrupted/Completing/Done/Error states. Its progress never reports bytes merely attempted by the browser.
8. FR8 — On a request failure, reload, or restored incomplete session, the UI retains server chunks, shows an actionable failure state, and offers **Reupload/Resume** (the user reselects the original file and the client verifies name and total bytes before continuing at the server offset) and **Cancel** (which deletes only that session's temporary content and metadata).
9. FR9 — A separate complete-session API checks that the persisted byte count equals the declared total and the temporary file's actual length equals that total, then atomically moves it to the already-validated final archive destination. It must not expose an item until this succeeds, must preserve collision protection through completion, and must return the standard browser-safe listing/DTO result.
10. FR10 — A background hosted service periodically removes sessions whose `LastActivityAt` is more than 24 hours old, including both metadata and temporary files, while avoiding active-session races and never deleting final archive items.
11. FR11 — The new API responses, errors, logs, persisted metadata exposed through APIs, and UI state contain opaque IDs and safe display names only: no host/container physical paths or root-relative paths.
12. FR12 — Replace/remove the legacy multipart upload endpoint and `SaveUploadedFileAsync` path only after the session endpoints fully cover current supported upload extensions, folder targeting, collision behavior, and listing refresh; update `README.md`'s Current Supported Features row for Archive management.

## Non-Functional Requirements

- Preserve the private Docker-only/local-network deployment, existing host-header allowlist, server-owned filesystem containment, `ArchiveService` category rules, and no-static-files access boundary.
- Store session metadata atomically and coordinate per-session writes so a restart, duplicate retry, or cleanup cannot corrupt an acknowledged offset or publish a partial file.
- The initial policy must support at least 10 GB declared files; its maximum accepted size and chunk thresholds must be validated options with conservative defaults appropriate to NAS storage.
- Byte-count completion detects truncation/incomplete transfer but is not a cryptographic integrity guarantee: no checksum is requested for this release.
- Keep JavaScript limited to browser `File` slicing/stream interop; C# owns session state, chunk sequencing, validation, retries, and UI decisions.
- Follow Bootstrap 5.3.8/Bootstrap Icons and the governing design guide: accessible labels, keyboard-operable Resume/Cancel controls, live status/error announcements, responsive progress layout, and reduced-motion-safe feedback.
- Add focused xUnit service/endpoint/client-state coverage and run exclusively through `make test`.

## Out of Scope

- Parallel/out-of-order chunks, cross-device upload ownership, authentication, cloud storage, sync clients, drag-and-drop, compression, encryption, virus scanning, or arbitrary file support.
- Cryptographic hashes/checksums, deduplication, resumable downloads, and automatic recovery without the user reselecting the local file after browser file-handle access is lost.
- Changing any authorized FFmpeg pipeline or processing an upload before it is committed.

## Open Questions

- None. The initial adaptive thresholds/default maximum should be documented as configuration values during implementation and tuned against the operator's NAS measurements without changing the sequential protocol.
