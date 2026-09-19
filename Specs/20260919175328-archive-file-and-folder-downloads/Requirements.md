# Requirements: Archive File and Folder Downloads

## Table of Contents

- [Problem Statement](#problem-statement)
- [User Stories](#user-stories)
- [Functional Requirements](#functional-requirements)
- [Non-Functional Requirements](#non-functional-requirements)
- [Out of Scope](#out-of-scope)
- [Open Questions](#open-questions)

## Problem Statement

`ArchiveBrowser.razor` lets users upload, organize, view, and play archive content, but its per-item actions panel has no way to retrieve that content to the device. This affects uploaded files in every archive category. Users also need folders to download as a ZIP, including folders currently in Trash, without exposing physical paths or leaving generated archive files on the server.

## User Stories

- Given I can see a file in any Archive Browser category, including Trash, when I choose Download from its actions menu, then my browser downloads that exact file using its existing name.
- Given I can see a folder in any Archive Browser category, including Trash, when I choose Download from its actions menu, then my browser downloads a ZIP containing that folder and its nested contents.
- Given an item ID is unknown, malformed, or belongs to a different category, when a download URL is requested, then the server returns 404 without exposing an archive path.
- Given a folder contains nested folders or files, when its ZIP is downloaded, then the ZIP preserves the folder hierarchy and excludes symlinks/reparse points.

## Functional Requirements

1. FR1 — `ArchiveBrowser.razor` adds a Download action, using the approved `bi-download` Bootstrap Icon, to every file and folder item action panel, including panels rendered for `Category == "trash"`.
2. FR2 — Selecting Download for a file requests a browser-download endpoint scoped by the current opaque category and item IDs; the response is an attachment with the item's existing safe display name.
3. FR3 — Selecting Download for a folder requests the same item-scoped download endpoint; the response is an attachment named `<folder name>.zip` containing the selected folder as its top-level directory and all recursively contained files/subfolders.
4. FR4 — `ArchiveEndpoints` resolves every download target exclusively through `IArchiveService` using the requested category and opaque item ID. It must reject an unknown ID, a category/ID mismatch, a reparse point, unreadable content, or a target outside the resolved category with 404 and without physical-path disclosure.
5. FR5 — Folder ZIP generation preserves root-relative entry names, safely skips reparse points and unreadable/disappeared descendants, supports an empty folder, and streams the ZIP directly to the HTTP response without writing a ZIP artifact into any archive, preview cache, or temporary persistent location.
6. FR6 — File downloads use the existing asynchronous, read-share-safe file-stream pattern and enable range processing; folder ZIP responses use `application/zip` and an attachment filename. Both response types set a download filename rather than relying on browser inline-preview behavior.
7. FR7 — The implementation adds focused endpoint/service tests for file attachment headers/content, folder ZIP hierarchy/content, empty-folder ZIP behavior, Trash support, invalid/cross-category IDs, and proof that response bodies/logs do not disclose the physical archive root.
8. FR8 — `README.md`'s Current Supported Features table is updated to state that archive files can be downloaded and folders can be downloaded as ZIPs.

## Non-Functional Requirements

- Preserve the repository's server-only filesystem and opaque-ID boundary: physical and root-relative paths must never enter browser DTOs, response diagnostics, or ordinary logs.
- Reuse the existing `ArchiveService` containment, category, and reparse-point rules; do not introduce a static-files mapping for archive roots.
- Keep generated ZIPs request-scoped and streaming. No background job, FFmpeg use, database migration, new volume, or persisted cache is authorized.
- Follow the Bootstrap-first design contract: actionable controls retain visible focus, at least a 40×40 CSS-pixel target where icon-only, and an accessible name. The existing action-row pattern supplies visible text for Download.
- Keep individual-file downloads compatible with clients that use HTTP ranges. Handle client cancellation and filesystem IO failures without leaking internal paths.
- Keep the implementation testable: ID resolution remains behind `IArchiveService`; ZIP traversal/entry construction is isolated behind a focused server-side abstraction that can be directly tested.

## Out of Scope

- Downloading an entire archive category root, multiple independently selected items, or a mixed custom selection as one ZIP.
- Uploading ZIPs and extracting them, or restoring/altering files through download.
- Password-protected/encrypted ZIPs, configurable compression levels, checksum generation, resumable ZIP downloads, download history, quotas, or audit logging.
- Adding authentication, sharing links, Internet hosting, or changing the LAN host-header policy.
- Persisting generated ZIPs or adding a background queue, cache, volume, package, or FFmpeg pipeline.

## Open Questions

- None. The confirmed scope is every individual file and folder item, including Trash; folders download as ZIP files.
