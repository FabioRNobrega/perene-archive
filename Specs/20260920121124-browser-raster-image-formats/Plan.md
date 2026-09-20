# Plan: Browser Raster Image Formats

## Table of Contents

- [Summary](#summary)
- [Technical Approach](#technical-approach)
- [Component Breakdown](#component-breakdown)
- [Dependencies](#dependencies)
- [External / Vendor Documentation Evidence](#external--vendor-documentation-evidence)
- [Flow](#flow)
- [Risk Assessment](#risk-assessment)

## Summary

Extend the archive’s existing raster-image classification path to accept, list, serve, and count GIF, WebP, AVIF, BMP, and ICO files. The work reuses the current opaque-ID archive flow; it introduces neither client conversion nor a media-processing pipeline.

## Technical Approach

`ArchiveUploadService.Create` deliberately has no independent extension policy: it calls `IArchiveService.ValidateUploadDestination` before creating a session. The implementation will therefore extend the one server-owned `ImageExtensions` set in `WebApp/WebApp/Services/ArchiveService.cs`, which already feeds `UploadableExtensions`, `BuildListing`, `CreateEntry`, and `TryResolveImage`. This maintains a single source of truth for upload admission and browser-visible `IsImage` classification.

`WebApp/WebApp/Endpoints/ArchiveEndpoints.cs` will extend its private `ImageContentTypes` map with the five extension-to-media-type pairs in FR4. `GetImage` already resolves only an opaque item ID through `TryResolveImage`, rejects unrecognized extension mappings, and returns `Results.File` with `lastModified`; this remains the sole image-byte route. The existing `ArchiveBrowser.razor` and `ImageCarouselViewer.razor` consume `ImageUrl` through native `<img>` elements, so no component redesign or JavaScript is needed for viewing.

`WebApp/WebApp/Services/ArchiveMetricsService.cs` owns a separate nested file-type counting allowlist, so it will receive the same five extensions. This duplication should be kept aligned in this small, independent metrics concern rather than exposing filesystem-oriented archive service internals as a broad shared utility.

The Image Cutter’s approved requirements explicitly limit same-format crop output to JPEG and PNG. `ImageCarouselViewer.razor` will use a narrow extension predicate for crop capability: for GIF, WebP, AVIF, BMP, and ICO it renders the existing crop action disabled, with a visible/accessible message explaining that cropping is only available for JPEG and PNG. This avoids relying on ImageSharp codec support or changing frames, animation, ICC data, or ICO representations while still permitting safe original-byte archive viewing.

No package, service registration, endpoint route, options change, or Docker change is required. `README.md` will be updated during implementation because the repository rules require supported-format changes to be reflected in its Current Supported Features table.

## Component Breakdown

**Existing files to modify:**

- `WebApp/WebApp/Services/ArchiveService.cs` — add `.gif`, `.webp`, `.avif`, `.bmp`, and `.ico` to the shared image extension allowlist used by upload validation and image classification.
- `WebApp/WebApp/Endpoints/ArchiveEndpoints.cs` — add the MIME mappings used by the existing opaque image endpoint.
- `WebApp/WebApp/Services/ArchiveMetricsService.cs` — classify the same extensions as images in dashboard file counts.
- `WebApp/WebApp.Client/Components/ImageCarouselViewer.razor` — disable the existing crop control for the newly supported formats and expose a concise explanation while retaining JPEG/PNG crop behavior.
- `WebApp.Tests/Services/ArchiveServiceTests.cs` — extend existing archive classification/upload-validation coverage for the added extensions.
- `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs` — extend existing opaque image route/listing coverage with the added content types.
- `WebApp.Tests/Endpoints/ArchiveEndpointsCropTests.cs` or a focused sibling test — verify crop is rejected or unavailable for added formats and remains available for JPEG/PNG.
- `WebApp.Tests/Services/ArchiveMetricsServiceTests.cs` — verify the dashboard metrics classify the added extensions as images.
- `README.md` — update the Current Supported Features table’s archive image-format description.

**New files to create:**

- None required.

## Dependencies

- Existing .NET 10 ASP.NET Core host, hosted Blazor WebAssembly client, and `Results.File` endpoint implementation.
- Existing browser support for the selected raster formats. Browser support is a client capability; unsupported clients will retain normal broken-image behavior without server conversion.
- Existing Docker Compose test workflow: `make test`.

## External / Vendor Documentation Evidence

- Microsoft Learn MCP documentation tooling is not available in this session, so verification of the ASP.NET Core `Results.File` behavior is pending. The implementation intentionally reuses the repository’s existing `GetImage` pattern rather than making a new ASP.NET API design decision.
- Media types in FR4 are explicit interoperability contracts to test at the endpoint boundary; no new vendor package or browser API is introduced.

## Flow

```mermaid
sequenceDiagram
    actor User
    participant UI as ArchiveBrowser.razor
    participant Upload as ArchiveUploadService
    participant Archive as ArchiveService
    participant API as ArchiveEndpoints
    participant Metrics as ArchiveMetricsService

    User->>UI: Select a .webp/.avif/.gif/.bmp/.ico file
    UI->>Upload: Create upload session (safe name, byte count)
    Upload->>Archive: ValidateUploadDestination(category, parentId, fileName)
    Archive-->>Upload: Extension accepted; opaque session can start
    Upload-->>UI: Opaque upload ID and progress contract
    User->>UI: Open archive folder
    UI->>API: GET archive listing
    API->>Archive: List / classify item
    Archive-->>API: IsImage + opaque ID
    API-->>UI: ImageUrl using opaque ID only
    UI->>API: GET /items/{id}/image
    API->>Archive: TryResolveImage(category, id)
    API-->>UI: Original bytes with matching image media type
    Metrics->>Archive: Scan archive files independently
    Metrics-->>User: Added formats included in image count
```

## Risk Assessment

| Risk | Evidence | Mitigation |
| --- | --- | --- |
| An extension is accepted but cannot be loaded by a particular browser. | AVIF and ICO support varies more than JPEG/PNG across older clients. | Serve the standards-correct media type and original bytes; do not add conversion or claim universal legacy-browser support. |
| MIME map and classification allowlist drift apart. | They are currently separate structures in `ArchiveService` and `ArchiveEndpoints`. | Add parameterized/representative tests covering each extension’s upload/classification and endpoint media type. |
| Crop could alter animation or unsupported encoder data. | `ImageCropService` saves through ImageSharp, while its governing spec is limited to JPEG/PNG. | Keep crop intentionally unavailable for all five new formats; do not invoke the generator. |
| SVG content introduces active-content/sanitization risk. | SVG is XML-based and does not have the same passive-raster policy. | Exclude SVG from every allowlist and document that a future feature needs a separate security design. |
| Filesystem paths leak while serving newly supported images. | The endpoint uses server file paths after opaque-ID resolution. | Reuse `TryResolveImage` and `Results.File`; test DTOs and errors for absence of the archive root path. |
