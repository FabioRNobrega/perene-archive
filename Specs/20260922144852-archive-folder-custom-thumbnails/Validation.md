# Validation: Archive Folder Custom Thumbnails

## Table of Contents

- [Validation: Archive Folder Custom Thumbnails](#validation-archive-folder-custom-thumbnails)
  - [Acceptance Criteria](#acceptance-criteria)
  - [Test Cases](#test-cases)
  - [Manual Verification](#manual-verification)
  - [Definition of Done](#definition-of-done)
  - [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | Opening the options panel for any item where `Kind == ArchiveItemKind.Folder` shows a "Select a custom thumbnail" button; the same panel for a file item never shows it. |
| FR2 | Clicking "Select a custom thumbnail" opens a file picker whose `accept` attribute matches the app's supported image extensions; choosing a file POSTs it to the new folder-thumbnail endpoint. |
| FR3 | Uploading a non-image file (e.g. a renamed `.txt`) or a file over the configured max size returns a 4xx response with a distinguishable reason, and the UI shows an inline error without changing the folder's displayed icon. |
| FR4 | After a valid upload, the reserved-name JPEG file exists directly inside the folder's own physical directory, at the configured fixed dimensions, fully filling the frame (cropped, not padded/letterboxed); it is published via a temp-file-then-atomic-move so no partially-written file is ever visible at the final path. |
| FR5 | `GET /api/archive/{category}/items` for a folder with a thumbnail includes a non-null `FolderThumbnailUrl`; for a folder without one, and for every non-folder item, the field is `null`. |
| FR6 | The Archive Browser grid renders the uploaded image (cover-sized, `object-fit-cover`) for a folder with `FolderThumbnailUrl` set, and still renders the folder name below it; a folder without a thumbnail still renders the `bi-folder-fill` icon exactly as before. |
| FR7 | The options panel shows "Remove thumbnail" instead of "Select a custom thumbnail" once a folder has a thumbnail, and shows "Select a custom thumbnail" again immediately after removal. |
| FR8 | `GET /api/archive/{category}/items/{id}/folder-thumbnail` returns the image bytes for a resolvable folder ID with a thumbnail, and 404 for a folder ID with no thumbnail or an unresolvable ID; the URL never contains a physical or root-relative path segment. |
| FR9 | Listing a folder's contents (`GET` on the folder itself, and any recursive scan) never includes the reserved thumbnail filename as a child item, even though it physically exists inside the folder. |
| FR10 | Renaming, moving (including batch move), or trashing a folder that has a thumbnail results in the thumbnail still being resolvable (via the folder's new opaque ID) immediately after the operation completes, with no explicit thumbnail-migration code involved — the ordinary move/delete of the physical directory is sufficient. |

## Test Cases

**Unit tests** (xUnit, `WebApp.Tests/Services/`, following the existing `ArchiveServiceTests.cs`/`ThumbnailCacheTests.cs` patterns):

- `FolderThumbnailProcessorTests.cs`: a valid PNG/JPEG/WebP input produces a JPEG output at the configured fixed size with the frame fully filled (crop, not letterbox); a non-image byte stream (e.g. plain text) returns a validation failure instead of a corrupt file; a very small or very large source image is still resized/cropped to the exact configured output size.
- `ArchiveServiceTests.cs` (extend existing file): a folder containing the reserved thumbnail filename never returns it as a child item from `BuildListing`; a folder with no thumbnail resolves `FolderThumbnailUrl`/thumbnail-lookup as absent; the reserved filename is also excluded from the recursive scans used for size calculation and trash operations.

**Endpoint/integration tests** (`WebApp.Tests/Endpoints/`, using `WebApplicationFactory` per existing convention):

- Upload a valid image via multipart to `POST /api/archive/{category}/items/{id}/folder-thumbnail` against a `WebApplicationFactory`-hosted app pointed at a temp archive root; assert 200 and that `GET /api/archive/{category}/items` subsequently reports a non-null `FolderThumbnailUrl` for that folder, and that the reserved file now exists on disk inside that folder's temp directory.
- Upload an invalid file (wrong content, oversized) and assert a 4xx with the expected error shape, and that no reserved file was written.
- `DELETE` the thumbnail and assert the subsequent listing reports `FolderThumbnailUrl: null`, the direct `GET .../folder-thumbnail` now 404s, and the reserved file no longer exists on disk.
- Rename the folder via `PATCH .../items/{id}/name`, then confirm the folder's (new) ID still resolves a thumbnail via `GET .../folder-thumbnail` — this is the key regression test proving the "no explicit propagation code" design actually works.
- Move the folder via `PATCH .../items/{id}/location` (poll `GET /api/archive/jobs` to completion, since move is asynchronous), then confirm the thumbnail is still resolvable at the destination.
- Move the folder to trash, then confirm the thumbnail is still resolvable under the trash category's listing (if trash exposes folder browsing) or at minimum that the reserved file physically exists inside the relocated directory.
- List the folder's contents directly and assert the reserved filename never appears as a regular file entry in the response.

## Manual Verification

Starting from a clean state, using this repo's documented Docker workflow (`AGENTS.md` → Execution Environment):

1. `make docker-run-bg` to start the stack, then `make get-url` to find the LAN URL.
2. In the Archive Browser, navigate into any category with at least one subfolder; confirm the folder currently shows the default yellow `bi-folder-fill` icon and its name below it.
3. Open that folder's options panel and confirm "Select a custom thumbnail" is present (and "Remove thumbnail" is not).
4. Pick a supported image file (e.g. a `.jpg`) under the configured size cap, ideally with a different aspect ratio than 640×360; confirm the folder card updates in place to show the image, cover-sized and fully filling the tile (cropped, not letterboxed), with the folder name still shown below it.
5. Re-open the options panel and confirm it now shows "Remove thumbnail" instead of "Select a custom thumbnail".
6. Attempt to upload an unsupported file (e.g. a `.txt` renamed to look like an image, or a file over the size cap) and confirm a clear inline error appears and the folder's thumbnail is unchanged.
7. Open that folder (navigate into it) and confirm the reserved thumbnail file itself never appears as a regular file in the folder's contents.
8. Rename the folder; confirm the thumbnail is still shown immediately after the rename completes.
9. Move the folder to a different subfolder/category (or batch-move it alongside other items) and, once the move job completes, confirm the thumbnail is still shown at the new location.
10. Select "Remove thumbnail"; confirm the card reverts to the default folder icon.
11. Move the folder (with a thumbnail re-applied) to trash; use `make docker-exec` to inspect the relocated directory on disk and confirm the reserved thumbnail file moved along with it; then empty the trash and confirm the whole directory (thumbnail included) is gone.
12. Run `make test` and confirm all existing and new tests pass in the isolated Docker Compose stack.

## Definition of Done

- Requirements, Plan, and Validation docs in this spec folder reflect the implemented behavior.
- All existing tests still pass; new unit and endpoint tests listed above are added and pass via `make test`.
- UI changes cover default/empty (no thumbnail), loading (upload in progress — disable the button and show a spinner/progress state), error (validation failure), and success (thumbnail displayed) states, with the same responsive/accessibility conventions as the rest of `ArchiveBrowser.razor`'s options panel (Bootstrap Icons at `currentColor`, 40×40 minimum touch target, accessible names, tooltips).
- The `IFormFile`/request-size-limit ASP.NET Core guidance referenced in `Plan.md`'s External / Vendor Documentation Evidence section is verified via the Microsoft Learn MCP server before merging, or explicitly flagged as pending if the server was unavailable. **Verified.**
- `README.md`'s `## Current Supported Features` table has a new/updated row for folder custom thumbnails.
- No physical or root-relative path is ever present in a browser-facing response, log line, or DTO field (spot-check by inspecting network responses and container logs during manual verification).
- The reserved thumbnail filename is verified, by test, to never appear in any folder-listing response.

## Rollback Plan

- The feature is additive: it introduces new endpoints, a new DTO field, and a new options-panel UI branch, none of which change existing folder/file behavior when `FolderThumbnailUrl` is `null`.
- To disable without a code revert: stop routing the three new endpoints in `ArchiveEndpoints.MapArchiveEndpoints` (comment out the `MapPost`/`MapDelete`/`MapGet` lines for `folder-thumbnail`) and hide the two new buttons in `ArchiveBrowser.razor`'s options panel; existing folders simply keep showing the default icon (any reserved thumbnail files already written on disk are harmless and stay excluded from listings as long as the listing-exclusion code isn't also reverted).
- To fully roll back stored data: delete the reserved thumbnail file from inside any folder where it was applied — this is a plain file delete with no separate cache/index to clean up.
- No database migration or schema change is involved, so there is no migration to reverse.
