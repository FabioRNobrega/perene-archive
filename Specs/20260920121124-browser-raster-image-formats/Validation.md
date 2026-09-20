# Validation: Browser Raster Image Formats

## Table of Contents

- [Acceptance Criteria](#acceptance-criteria)
- [Test Cases](#test-cases)
- [Manual Verification](#manual-verification)
- [Definition of Done](#definition-of-done)
- [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | `.gif`, `.webp`, `.avif`, `.bmp`, and `.ico` direct files classify as `IsImage = true` in writable and read-only archive categories, case-insensitively, while `.svg` and an arbitrary unsupported extension remain non-images. |
| FR2 | Creating a resumable upload session for each added extension succeeds in a writable category and follows the existing session contract; `.svg` still receives the existing unsupported-file validation error. |
| FR3 | Listing an added-format file includes `isImage: true` and a non-null opaque `/image` URL; the Archive Browser follows its existing image tile/viewer branch without populating the persistent media player. |
| FR4 | Each added format’s resolved `/image` URL returns 200, original bytes, and the specified content type; wrong-category, unknown-ID, and non-image requests return 404. |
| FR5 | Archive metrics count one each of GIF, WebP, AVIF, BMP, and ICO under Images, not Other. |
| FR6 | JPEG/PNG retain an enabled crop action. Added-format images show cropping as unavailable with an accessible explanation and do not call the crop endpoint/generator. |
| FR7 | README’s Current Supported Features table lists JPEG, PNG, GIF, WebP, AVIF, BMP, and ICO archive-image support and notes SVG is excluded. |

## Test Cases

**Service tests** (`WebApp.Tests/Services/ArchiveServiceTests.cs`):

- Use the existing archive fixture to verify each added extension appears as `IsImage`, resolves through `TryResolveImage`, and passes `ValidateUploadDestination` for a writable category.
- Verify uppercase extension handling and that `.svg` remains rejected by `ValidateUploadDestination` and non-image in a listing.

**Endpoint tests** (`WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs`):

- Parameterize or add focused cases for each added extension. Assert the listing has an opaque image URL, the response media type exactly matches FR4, and no response exposes the temporary archive-root path.
- Confirm the existing non-image and wrong-category 404 boundaries remain intact.

**Metrics tests** (`WebApp.Tests/Services/ArchiveMetricsServiceTests.cs`):

- Place representative added-format files in a fixture category and assert their counts are added to `Images`, not `Other`.

**Client/component coverage** (`WebApp.Tests/Client/` if existing component/state patterns support it, otherwise manual verification):

- Verify the crop-capability predicate allows only `.jpg`, `.jpeg`, and `.png`, and added formats communicate that cropping is unavailable.

**Full suite:**

- Run `make test` in the documented isolated Docker Compose test stack.

## Manual Verification

1. Create an archive-root fixture with writable `Pictures` and place one valid small file for each supported added format in it; include an SVG control file.
2. Start the application through `make docker-run`.
3. In Archive Browser, upload a WebP and an AVIF to a writable folder. Confirm resumable upload completion and normal listing refresh.
4. Confirm GIF, WebP, AVIF, BMP, and ICO show image tiles; open each and verify the existing fullscreen viewer opens without selecting the footer player.
5. Inspect the browser network response for each image URL and verify the media type specified in FR4; verify no URI or response text includes host/container archive paths.
6. Confirm the scissors control works for JPEG/PNG and is disabled with an explanation for every added format.
7. Attempt an SVG upload and confirm it remains rejected.
8. Open the dashboard and confirm the files are included in the Images count.
9. Run `make test` and confirm the isolated suite passes.

## Definition of Done

- Requirements, Plan, and Validation documents exist in this spec folder.
- The shared archive image allowlist, endpoint MIME map, and metrics classification all cover the same new extensions.
- The crop UI preserves its JPEG/PNG-only contract and tells users why it is unavailable for added formats.
- Focused xUnit coverage and the full `make test` suite pass in Docker Compose.
- README accurately documents the supported archive image formats and SVG exclusion.
- Vendor documentation verification remains explicitly marked pending unless Microsoft Learn tooling becomes available during implementation.

## Rollback Plan

- Revert the added extensions in `ArchiveService.ImageExtensions`, the matching `ArchiveEndpoints.ImageContentTypes` entries, and `ArchiveMetricsService`’s image set.
- Revert the crop-capability UI branch and its tests, then restore the prior README feature-table wording.
- Existing files of those formats remain on disk but return to generic archive-file treatment; no migration, generated derivative, background job, or persistent schema needs rollback.
