# Validation: Archive File and Folder Downloads

## Table of Contents

- [Acceptance Criteria](#acceptance-criteria)
- [Test Cases](#test-cases)
- [Manual Verification](#manual-verification)
- [Definition of Done](#definition-of-done)
- [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | Each file and folder card in the shared Archive Browser actions panel, including Trash, shows a text-labelled Download action with `bi-download`. |
| FR2 | A valid file download returns its original bytes and an attachment download filename equal to the item name. |
| FR3 | A valid folder download returns an attachment named `<folder>.zip`; opening it reveals the selected folder as the top-level entry and all expected nested files/directories. |
| FR4 | Unknown, malformed, and cross-category IDs return 404 and response content does not contain the archive test-root path. |
| FR5 | ZIP output preserves nested paths, includes an empty selected/nested directory, and contains no reparse-point target content. No ZIP appears under the archive root, preview root, or another persisted output directory after the response. |
| FR6 | File downloads include range support (a valid `Range` request returns 206 and expected bytes); folder downloads return `application/zip` with an attachment filename. |
| FR7 | Focused unit/integration tests cover all stated download, ZIP, Trash, failure, and path-privacy behavior using the repository's xUnit patterns. |
| FR8 | README's Archive management row explicitly documents individual file download and folder-as-ZIP download. |

## Test Cases

**Unit tests:**

- `WebApp.Tests/Services/ArchiveServiceTests.cs` — `TryResolveDownloadableItem` returns an opaque-ID-resolved file and folder in their own category, rejects a different category and unknown ID, and rejects reparse points where a supported fixture can create one.
- `WebApp.Tests/Services/ArchiveDownloadServiceTests.cs` — create a nested fixture and write its ZIP to a `MemoryStream`; assert normalized entries such as `Reports/`, `Reports/2026/`, and `Reports/2026/q1.txt`, exact file bytes, and an explicit empty-directory entry.
- `WebApp.Tests/Services/ArchiveDownloadServiceTests.cs` — assert a reparse-point descendant is skipped (platform-conditional if symlink creation is unavailable) and a missing/unreadable descendant does not put any path in an exception/response diagnostic.

**Integration tests:**

- `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs` — list a fixture file, request `/api/archive/downloads/items/{id}/download`, and assert 200, original bytes, `Content-Disposition: attachment` with the source name, no physical root in headers/body, and correct content type.
- `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs` — request the same file with a byte `Range` header and assert 206, `Content-Range`, and the expected partial bytes.
- `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs` — list a nested folder, request its download URL, assert 200 and `application/zip`, open response bytes using `ZipArchive`, and assert hierarchy/content including an empty directory.
- `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs` — list and download both a file and a folder in Trash; assert both succeed even though Trash remains non-creatable/non-uploadable.
- `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs` — request an unknown ID, slash/path-like ID, and a valid Downloads item ID under another category; assert 404 and no root path disclosure in each response.
- `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs` — extend `Archive_browser_markup_uses_unified_dropdown_cards_and_keeps_video_grid_specialized` (or a more focused replacement) to assert `bi-download` and the new opaque `/download` action URL in `ArchiveBrowser.razor`.

## Manual Verification

1. Prepare the documented archive root with a normal category folder containing one standalone file, a folder with nested files, and an empty nested directory; place equivalent file/folder fixtures in Trash.
2. Start the Compose stack with `make docker-run` and open the local/LAN URL reported by the project workflow.
3. In Downloads (or another normal category), open a file's action panel and click Download. Confirm the browser saves the original filename, the bytes match the source, and no inline player/PDF/image preview replaces the download.
4. Open a folder's action panel and click Download. Confirm the saved name ends in `.zip`; extract it and confirm the selected folder is the top-level directory, its nested files match byte-for-byte, and empty directories are present.
5. Open Trash and repeat steps 3–4. Confirm Download is available and works while the existing create/upload restrictions remain unchanged.
6. In browser developer tools, request the download URL with a `Range: bytes=0-3` header for a file. Confirm `206 Partial Content` and the corresponding byte range.
7. Attempt a direct URL with an unknown ID, an ID from another category, and a path-shaped ID. Confirm 404 responses reveal neither host archive paths nor root-relative paths.
8. Inspect the archive root and configured preview/output locations after folder download. Confirm no generated ZIP was retained. Test both light and dark themes and a narrow viewport to confirm the additional action remains visible, focusable, and usable.
9. Run `make test` and confirm the isolated Docker Compose test suite passes.

## Definition of Done

- Requirements, Plan, and Validation documents in this spec folder are complete and mutually consistent.
- `ArchiveBrowser.razor` exposes Download for every file and folder action panel, including Trash, with the approved Bootstrap icon and accessible visible label.
- The opaque endpoint returns secure attachment downloads for files and streamed ZIPs for folders without path leakage or persistence.
- ZIP traversal is contained, ignores reparse points, preserves hierarchy, supports empty folders, and is covered by tests.
- Existing `make test` tests pass and new xUnit endpoint/service coverage passes in the Docker Compose test stack.
- README's Current Supported Features table is updated.
- The official ASP.NET Core/.NET documentation evidence in `Plan.md` is retained with the implementation.

## Rollback Plan

- Remove the Download link from `WebApp/WebApp.Client/Components/ArchiveBrowser.razor` and the `GET /api/archive/{category}/items/{id}/download` registration/handler from `WebApp/WebApp/Endpoints/ArchiveEndpoints.cs` to disable the UI and public route.
- Remove the `IArchiveDownloadService` registration and its implementation from `Program.cs`/`Services` if a full code revert is required; no migration, cache, generated ZIP, or filesystem cleanup is needed because ZIPs are response-streamed only.
- Revert the matching README row and new tests with the implementation change. Existing archive listing, upload, playback, edit, move, and Trash behavior remains independent of this additive feature.
