
# Requirements: CBZ Comic Viewer
## Table of Contents

- [Problem Statement](#problem-statement)
- [User Stories](#user-stories)
- [Functional Requirements](#functional-requirements)
- [Non-Functional Requirements](#non-functional-requirements)
- [Out of Scope](#out-of-scope)
- [Open Questions](#open-questions)

## Problem Statement

The Archive Browser currently classifies browser-raster image files, EPUB books, PDFs, and other archive content, but it treats `.cbz` comics as generic files. A CBZ is a ZIP archive containing ordered page images, so users cannot open one as a comic or navigate its pages. Users need a read-only, full-viewport `ComicViewer.razor` that safely renders supported image entries in archive order without exposing the CBZ's physical path or internal entry names.

## User Stories

- Given a `.cbz` file in any Archive Browser category, when I activate its card, then a fullscreen comic viewer opens at its first readable page.
- Given an open comic, when I swipe horizontally, use the left/right tap zones, press the left/right arrow keys, or use the previous/next controls, then I move one page in that direction without wrapping past either end.
- Given an open comic, when I choose fit-height or fit-width, then the current page uses that fit mode and remains legible on desktop and touch devices.
- Given an open comic, when I inspect the viewer toolbar, then I can identify the comic name and current page number out of the total page count.
- Given a corrupt, empty, unsupported, or policy-rejected CBZ, when I try to open it, then I receive a safe, actionable error and no archive path or ZIP entry path is disclosed.

## Functional Requirements

1. FR1 — `ArchiveService` and its browser-safe DTO mapping must recognize `.cbz` files in every Archive Browser category as comics, while retaining their existing generic-file behavior for all non-CBZ formats.
2. FR2 — `ArchiveBrowser.razor` must render and activate a CBZ item as a comic entry; activation must open `ComicViewer.razor` rather than populate `PersistentPlayerState` or the footer media player.
3. FR3 — The server must expose opaque-ID comic metadata and page routes under `/api/archive/{category}/items/{id}/comic`, resolving only a current, contained archive item and never returning a physical path or a CBZ internal entry name.
4. FR4 — Comic metadata must include the browser-safe comic display name and the total number of readable pages, with pages determined from ZIP file entries that are supported browser-raster formats and ordered by ordinal filename comparison.
5. FR5 — A comic page route must validate the requested zero- or one-based page index against the resolved metadata and stream only that page's bytes with its correct image content type; folders, non-comics, malformed ZIPs, invalid indices, cross-category IDs, and unsafe archive contents must not be served.
6. FR6 — `ComicViewer.razor` must use the existing full-viewport Fill-tab lifecycle and provide explicit accessible previous, next, and exit controls. Previous/next are disabled at their respective boundaries and navigation never wraps.
7. FR7 — `ComicViewer.razor` must provide mutually exclusive “fit to viewport height” and “fit to viewport width” controls. The selected mode must apply to the current image and all subsequently navigated pages for the open session.
8. FR8 — The viewer must show the comic name and a page status in the form “Page N of M”, including an accessible live update when navigation changes the page.
9. FR9 — The viewer must support a single-page previous/next gesture through horizontal touch/pointer swipe, the existing EPUB-style left/right tap zones, and non-repeating Left/Right Arrow keyboard input. It must ignore interactions originating from controls and must distinguish taps from swipes so one gesture cannot turn multiple pages.
10. FR10 — The viewer must have explicit loading, empty-comic, and error states. A failed page request must preserve the viewer chrome and permit the user to exit or navigate to another valid page.
11. FR11 — The archive upload validation and README supported-features table must include `.cbz` as an accepted, read-only comic format.

## Non-Functional Requirements

- Preserve the archive boundary: all comic access uses snapshot/category-scoped opaque IDs, contained server paths, and browser-safe DTOs; do not expose archive-root paths or ZIP entry names in API responses, markup, or ordinary logs.
- Read CBZ archives server-side with `System.IO.Compression.ZipArchive` in read-only mode. Do not extract entries to the archive root, a temporary directory, or a persistent cache.
- Treat CBZ input as untrusted: define validated maximum entry count, per-page decompressed byte size, and aggregate decompressed byte size before opening/streaming a page; reject directory entries and unsupported formats.
- Keep ZIP parsing/content-type determination in `WebApp`; keep page state, fit state, responsive rendering, and input behavior in `WebApp.Client`.
- Reuse Bootstrap 5.3.8, Bootstrap Icons, the project design tokens, and the existing Fill-tab/JS-module patterns. Icon-only controls need accessible names, 40px minimum targets, visible focus, and documented tooltip/title fallback.
- Add focused xUnit tests using the existing service, endpoint, and client-state test conventions. Run them through the Docker Compose Makefile workflow.

## Out of Scope

- Reading `.cbr`, `.cb7`, `.cbt`, PDFs, EPUBs, or arbitrary ZIP files as comics.
- Extracting, editing, annotating, cropping, converting, downloading individual pages, thumbnails, or persistent reading progress/bookmarks.
- Double-page spreads, right-to-left reading mode, vertical continuous scrolling, zoom/pan, rotation, or image enhancement controls.
- Supporting image formats not already classified as browser-raster images by the archive.
- Changing existing raster image, EPUB, PDF, video, music, or footer-player behavior.

## Open Questions

- ⚠️ TODO: Choose concrete CBZ safety limits (entry count, page byte size, aggregate decompressed size) based on expected archive sizes and container memory budget during implementation.
- ⚠️ TODO: Confirm whether the existing upload chunk-size ceiling needs adjustment after real CBZ upload testing; no change is assumed by this spec.
