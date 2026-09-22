# Requirements: Archive Folder Custom Thumbnails

## Table of Contents

- [Requirements: Archive Folder Custom Thumbnails](#requirements-archive-folder-custom-thumbnails)
  - [Problem Statement](#problem-statement)
  - [User Stories](#user-stories)
  - [Functional Requirements](#functional-requirements)
  - [Non-Functional Requirements](#non-functional-requirements)
  - [Out of Scope](#out-of-scope)
  - [Open Questions](#open-questions)

## Problem Statement

Every folder rendered in the Archive Browser (`WebApp/WebApp.Client/Components/ArchiveBrowser.razor`) falls through the same generic `else` branch as any unrecognized file type and always shows the same `bi-folder-fill` Bootstrap icon (`ArchiveBrowser.razor:487`) with its name below it (`ArchiveBrowser.razor:494`). Unlike videos, images, music, books, and comics — which all get a cover-sized visual (`ThumbnailUrl`, `ImageUrl`, `AlbumCoverUrl`, `BookCoverUrl`) — folders have no way to be visually distinguished from one another in the grid. Users organizing a large NAS-style library have no existing mechanism to set a recognizable custom image for a folder. The codebase already has a close precedent for a folder-associated image file living directly inside that folder — `ArchiveService.FindAlbumCover` (`ArchiveService.cs:808-857`) picks up the first image file it finds inside a music folder and attaches it as that folder's album cover. This feature follows the same "image lives physically inside the folder it decorates" idea, but with an explicit, user-controlled, reserved file rather than an auto-detected one.

## User Stories

- Given a folder in the Archive Browser, when the user opens that folder's options panel, then they see a "Select a custom thumbnail" action.
- Given the user selects "Select a custom thumbnail", when they pick a supported image file from their device, then the folder card immediately shows that image (cover-sized) in place of the folder icon, with the folder name still shown below it.
- Given a folder already has a custom thumbnail, when the user opens that folder's options panel, then they see a "Remove thumbnail" action instead of (or in addition to) "Select a custom thumbnail".
- Given a folder has a custom thumbnail, when the user selects "Remove thumbnail", then the folder card reverts to the default `bi-folder-fill` icon.
- Given a folder with a custom thumbnail, when the user renames or moves that folder (including via batch move) or sends it to trash, then the thumbnail stays attached to the folder at its new name/location (or travels with it into trash) because the thumbnail file lives physically inside the folder itself.
- Given the user uploads a file that is not a supported image format, or an oversized file, when the upload is submitted, then the user sees a clear inline error and no thumbnail is applied.

## Functional Requirements

1. FR1 — The Archive Browser's per-item options panel (`ArchiveBrowser.razor:320-376`) shows a "Select a custom thumbnail" action for any item where `Kind == ArchiveItemKind.Folder`.
2. FR2 — "Select a custom thumbnail" opens a native file picker restricted to the app's already-supported image formats (the existing `ImageExtensions` set in `ArchiveService.cs:18`: png/jpg/jpeg/gif/webp/avif/bmp/ico) and uploads the chosen file via a new single-shot multipart endpoint.
3. FR3 — The server validates the uploaded file's size (reject over a fixed 10 MB constant) and content (must decode as a supported raster image via ImageSharp) before accepting it; invalid uploads return a 4xx error with a machine-readable reason the client surfaces to the user.
4. FR4 — On a valid upload, the server normalizes the image — resizing and cropping it to fill a fixed output size (default 640×360) exactly, discarding whatever doesn't fit rather than preserving the source aspect ratio with padding — re-encodes it to JPEG, and atomically publishes it as a single reserved-name file (e.g. `.pereneFolderThumbnail.jpg`) written directly inside that folder's own physical directory, alongside its regular contents.
5. FR5 — `ArchiveItemDto` (`WebApp.Client/Models/ArchiveItemDto.cs`) gains a `FolderThumbnailUrl` field, populated only for folder items by checking whether the reserved thumbnail file exists inside that folder, so `GET /api/archive/{category}/items` (and any nested folder listing) reports whether a folder has a custom thumbnail and its serving URL.
6. FR6 — `ArchiveBrowser.razor` renders a cover-sized `<img>` (mirroring the existing `archive-cover-tile` image-item pattern at `ArchiveBrowser.razor:403-424`) instead of the folder icon whenever a folder's `FolderThumbnailUrl` is set, while still rendering the folder name below it exactly as today.
7. FR7 — The options panel shows a "Remove thumbnail" action instead of "Select a custom thumbnail" whenever the folder currently has a thumbnail; selecting it deletes the reserved thumbnail file from inside the folder and the folder card reverts to the default icon on the next refresh.
8. FR8 — A new opaque endpoint (e.g. `GET /api/archive/{category}/items/{id}/folder-thumbnail`) serves the folder's thumbnail bytes by resolving the opaque folder ID through the current archive snapshot the same way `/thumbnail`, `/cover`, and `/image` already do — never exposing a physical or root-relative path.
9. FR9 — The reserved thumbnail filename is excluded from every folder-content listing (`ArchiveService.BuildListing` and any other folder-enumeration path) so it never appears to the user as a regular browsable file inside the folder it decorates.
10. FR10 — Because the thumbnail file lives physically inside the folder, no separate propagation logic is needed for rename/move/batch-move/trash: the existing filesystem move/delete operations already carry (or remove) it along with the rest of the folder's contents. This must be confirmed by test rather than assumed.

## Non-Functional Requirements

- Server-only physical/root-relative path handling: thumbnail file resolution stays inside server-side services (path containment checks identical to the ones already used for every other physical-path operation in `ArchiveService`), never crossing into DTOs or browser-visible URLs (matches the existing opaque-ID boundary).
- The new upload endpoint is validated the same way the app validates every other user-facing input: explicit size cap, explicit format allowlist, and no reliance on client-supplied MIME type alone (decode with ImageSharp to confirm it is a real image).
- The feature must not use FFmpeg; it is out of the FFmpeg exception list in `AGENTS.md` and must use the already-referenced `SixLabors.ImageSharp` package instead.
- Concurrency: a single folder should not be able to process two simultaneous thumbnail uploads that race to publish; write via a temp file inside the folder followed by an atomic move to the final reserved filename (matching `ThumbnailCache`'s publish pattern) and de-duplicate concurrent uploads per folder (matching the lightweight `SemaphoreSlim`/`ConcurrentDictionary` pattern used by the audio-track remux feature) rather than a full queue/worker.
- Testability: the image-processing step (decode/crop/encode) must be behind a small interface so it can be unit-tested with fakes, following the existing `IThumbnailGenerator` separation; folder-thumbnail file resolution can be tested directly against a temp directory the way `ArchiveServiceTests.cs` already does for other folder operations.
- All new/changed workflows run and are tested exclusively through `make test` / `make docker-run`, per `AGENTS.md`; no native `dotnet` workflow outside Docker.

## Out of Scope

- Custom thumbnails for individual files (videos, music, images, books, comics already have their own cover mechanisms).
- Cropping/editing tools for the uploaded image beyond the fixed automatic crop-to-fill (no interactive crop UI like the existing video A/B/crop editor).
- Bulk/batch thumbnail assignment across multiple folders at once.
- Any change to how folder IDs are computed (unaffected by this feature, since the thumbnail is resolved by checking the folder's own physical directory for the reserved filename, not by a separate ID-keyed store).
- Category-root-level (top-level) thumbnails or icons — this applies to ordinary folders inside a category, not the category roots themselves.
- Preventing a user from placing their own same-named file inside a folder to collide with the reserved thumbnail filename outside the app (e.g. via direct NAS filesystem access) — the reserved filename is chosen to be unlikely to collide, but external filesystem edits are already an accepted source of drift elsewhere in the archive.

## Open Questions

None — the two open items from the prior draft are resolved:

- The reserved thumbnail filename is `.pereneFolderThumbnail.jpg`, fixed as a named constant in `ArchiveService` (not user-configurable).
- The max upload size is a fixed constant, 10 MB, rather than a configurable option (no new `appsettings`/env var entry). Output thumbnail pixel dimensions remain 640×360, matching the existing video-thumbnail convention.
