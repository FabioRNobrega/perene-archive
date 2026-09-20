# Validation: CBZ Comic Viewer

## Table of Contents

- [Acceptance Criteria](#acceptance-criteria)
- [Test Cases](#test-cases)
- [Manual Verification](#manual-verification)
- [Definition of Done](#definition-of-done)
- [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | A `.cbz` in each tested archive category is returned as a comic; non-CBZ files retain their prior classification. |
| FR2 | Activating a comic opens the fullscreen viewer and leaves the footer player unchanged. |
| FR3 | Metadata/page routes accept only valid category-scoped opaque comic IDs and never include physical or ZIP-entry paths in a response. |
| FR4 | Metadata returns the comic display name and correct count of supported image entries ordered ordinally. |
| FR5 | A valid page index returns exact image bytes and MIME type; invalid, cross-category, non-comic, malformed, and rejected CBZ requests fail safely. |
| FR6 | Exit/Escape works; accessible previous/next/exit controls have usable targets and no navigation wraps. |
| FR7 | Fit-height and fit-width are mutually exclusive and visibly constrain the rendered page on desktop and mobile viewport sizes. |
| FR8 | The viewer displays the comic name and updates “Page N of M” for every successful navigation. |
| FR9 | One horizontal swipe, tap-zone gesture, arrow keypress, or toolbar action moves exactly one bounded page; control interaction does not trigger a page turn. |
| FR10 | Loading, empty-comic, and page/metadata error states are visible, safe, and leave exit/navigation available where applicable. |
| FR11 | `.cbz` is accepted by upload validation and the README feature table describes its read-only comic support. |

## Test Cases

**Unit tests:**

- `WebApp.Tests/Services/ComicBookServiceTests.cs` — create temporary ZIP/CBZ fixtures using `ZipArchive`; verify supported-page filtering, ordinal name ordering, valid page bytes/content types, no-page result, malformed archive result, directory exclusion, invalid index handling, and each configured size/count limit.
- `WebApp.Tests/Services/ArchiveServiceTests.cs` — verify `.cbz` classification, upload allowlisting, and `TryResolveComic` behavior across categories and containment boundaries.
- `WebApp.Tests/Client/` — test any extracted comic viewer page/fit state model: starts at page one, prevents under/overflow, preserves fit selection, and produces correct display labels.

**Integration tests:**

- `WebApp.Tests/Endpoints/ArchiveEndpointsComicTests.cs` or `ArchiveEndpointsTests.cs` — use the existing `VideoManagerFactory`/`WebApplicationFactory` pattern with a minimal CBZ fixture; verify metadata and image page endpoint status, content type, body, category isolation, bad index handling, safe JSON/error bodies, and no root-path disclosure.
- `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs` — verify `.cbz` file upload validation accepts an allowed archive under existing category permissions and leaves other upload policy unchanged.

## Manual Verification

1. Create or copy a safe CBZ containing sequentially named supported image pages (for example `1.jpg`, `2.jpg`, `3.jpg`) into a folder in each of two archive categories; use a nested folder for one case.
2. Start the application with `make docker-run` and open the applicable Archive Browser.
3. Confirm each CBZ renders as a comic card, while images, EPUBs, PDFs, videos, and music retain their current cards/activation behavior.
4. Open a comic. Confirm the fullscreen viewer shows the archive filename and “Page 1 of 3,” with Previous disabled and no footer player selection.
5. Switch between fit-height and fit-width on desktop and a narrow responsive viewport. Confirm pages remain undistorted and the selected fit mode persists while changing pages.
6. Navigate using Next/Previous buttons, left/right keyboard arrows, a left/right tap-zone tap, and horizontal swipes on a touch device or device emulator. Confirm each input advances exactly one page and neither end wraps.
7. Confirm Escape and the exit control return to the same archive folder view.
8. Try an empty CBZ, malformed CBZ, unsupported-only ZIP, oversized-policy fixture, invalid page URL, and an opaque ID copied from another category. Confirm safe errors/404 responses contain neither the host archive path nor entry names.
9. Upload a valid `.cbz` through the existing archive upload UI and confirm it appears/open as a comic.
10. Run `make test` and confirm the isolated Docker Compose test stack passes.

## Definition of Done

- Requirements, Plan, and Validation are complete in this spec folder.
- All FRs have automated coverage appropriate to the existing xUnit service/endpoint/client pattern.
- `make test` passes in the documented Docker Compose workflow.
- UI covers desktop/mobile responsive layout, loading, empty, error, focus, keyboard, touch, and non-color accessibility cues.
- ZIP safety limits are documented, validated at startup, and tested.
- No physical/root-relative archive path or ZIP-entry name reaches browser-visible data or normal logs.
- `README.md` Current Supported Features includes CBZ comic support.
- Microsoft documentation evidence in `Plan.md` supports ZIP and Blazor interop decisions.

## Rollback Plan

- Revert the CBZ classification/upload allowlist and `ComicViewer` activation branch to restore generic-file treatment.
- Remove the comic metadata/page endpoint mappings and `ComicBookService` registration; existing archive item routes remain unaffected.
- No migration, cache, source extraction, or persistent reading state exists, so rollback requires no data cleanup.
