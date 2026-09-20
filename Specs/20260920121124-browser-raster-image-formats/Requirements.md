# Requirements: Browser Raster Image Formats

## Table of Contents

- [Problem Statement](#problem-statement)
- [User Stories](#user-stories)
- [Functional Requirements](#functional-requirements)
- [Non-Functional Requirements](#non-functional-requirements)
- [Out of Scope](#out-of-scope)
- [Open Questions](#open-questions)

## Problem Statement

The archive currently treats only `.jpg`, `.jpeg`, and `.png` files as images. `ArchiveUploadService` delegates upload validation to `ArchiveService`, whose shared image extension allowlist excludes common browser-renderable raster formats; the archive image endpoint and dashboard count use separate matching lists. As a result, GIF, WebP, AVIF, BMP, and ICO files cannot be uploaded as images or consistently viewed in the Archive Browser.

## User Stories

- Given I select a `.webp`, `.avif`, `.gif`, `.bmp`, or `.ico` file for a writable archive folder, when I start its resumable upload, then the server accepts it under the same safe-name, containment, size, collision, and opaque-session rules as existing images.
- Given a supported additional image format is present in any archive category, when I open that folder, then it is represented as an image tile and opens in the existing fullscreen image viewer.
- Given I open a supported additional image through its opaque image URL, when the browser requests it, then it receives the original bytes with the matching image media type and no filesystem path exposure.
- Given the dashboard reports archive file counts, when it scans one of the added image formats, then it counts it as an image rather than “Other.”

## Functional Requirements

1. FR1 — `ArchiveService` must treat `.gif`, `.webp`, `.avif`, `.bmp`, and `.ico` as supported archive image extensions in addition to `.jpg`, `.jpeg`, and `.png`; matching remains case-insensitive and category-independent.
2. FR2 — The existing resumable upload session creation flow must accept the FR1 extensions in every writable archive category, preserving all existing name validation, containment, collision protection, declared-size limits, opaque upload IDs, and atomic publication behavior.
3. FR3 — Archive listings and `TryResolveImage` must classify every FR1 extension as `IsImage`, populate its existing opaque `ImageUrl`, and allow the existing Archive Browser tile and fullscreen `ImageCarouselViewer` path to render it without new client-side format conversion.
4. FR4 — `GET /api/archive/{category}/items/{id}/image` must serve a resolved image’s original bytes with these exact content types: `.gif` → `image/gif`, `.webp` → `image/webp`, `.avif` → `image/avif`, `.bmp` → `image/bmp`, and `.ico` → `image/x-icon`; all existing opaque-ID, category containment, not-found, and last-modified behavior remains unchanged.
5. FR5 — `ArchiveMetricsService` must count every FR1 extension as an image, so dashboard category and total file-type breakdowns agree with archive classification.
6. FR6 — The existing image-crop workflow remains limited to the formats authorized by its governing spec (`.jpg`, `.jpeg`, `.png`). The crop control must be unavailable for an FR1-added format, with accessible explanatory text, rather than attempting an unsupported or lossy in-process re-encode.
7. FR7 — `README.md`’s **Current Supported Features** table must state the expanded supported archive image formats and preserve the explicit SVG exclusion.

## Non-Functional Requirements

- Preserve server-owned extension validation and opaque archive item/session IDs. No client-supplied path, media type, or extension assertion is trusted in place of `ArchiveService` resolution.
- Serve original files only; do not add FFmpeg, thumbnail generation, raster conversion, browser polyfills, or a static-files mapping.
- SVG remains unsupported. Its XML/active-content and sanitization policy is distinct from passive raster-image support and requires a separate approved specification.
- Preserve the Docker Compose-only build/test workflow and add focused xUnit service and endpoint coverage.
- Maintain the existing Bootstrap-based responsive tile/viewer behavior and accessible controls. Where crop is unavailable, communicate that state programmatically and visually.

## Out of Scope

- SVG upload, rendering, sanitization, or conversion.
- TIFF, HEIC/HEIF, JPEG XL, RAW camera formats, PDF-as-image treatment, or any format outside FR1.
- Image transcoding, thumbnail derivatives, metadata extraction, or changing file bytes on upload.
- Extending ImageSharp crop encoding to the new formats, including animated GIF frame preservation or ICO multi-resolution handling.

## Open Questions

- None. The requested scope is GIF, WebP, AVIF, BMP, and ICO; SVG remains explicitly excluded.
