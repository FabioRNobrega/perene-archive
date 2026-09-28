# Validation: EPUB Reader Themes

## Table of Contents

- [Acceptance Criteria](#acceptance-criteria)
- [Test Cases](#test-cases)
- [Manual Verification](#manual-verification)
- [Definition of Done](#definition-of-done)
- [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | Open two different EPUBs after saving a preference and verify both use the same restored active appearance without book identifiers in theme data. |
| FR2 | Change each of the five fields, close/reopen a book, and verify the change remains while reading position continues from saved progress. |
| FR3 | The Aa panel contains labeled keyboard-accessible font, size, foreground/background color, and line-spacing controls; its existing margins controls remain independent. |
| FR4 | Use Default in both app light and dark mode; verify Montserrat/18px/Normal return and foreground/background inherit the corresponding Bootstrap app colors. |
| FR5 | Save a valid named theme; blank, overlong, and case-insensitively duplicate names are rejected with usable feedback and no loss of the active settings. |
| FR6 | Apply, update, rename, and delete a theme; verify each operation persists across a reader close/reopen, and an ordinary control edit does not mutate the saved theme. |
| FR7 | Delete the selected theme and verify it disappears from the library while its already-applied visual settings remain current. |
| FR8 | API tests prove valid requests return only safe DTO fields; unsupported font/size/line height/color and malformed requests return 400; unknown IDs return 404. |
| FR9 | Missing, malformed, or unreadable theme JSON loads Default/empty state; writes are serialized and leave no `.tmp` files after success. |
| FR10 | Review API payloads, JSON fixture, errors, and logs to verify no physical/root-relative archive path, EPUB member, or source-file metadata appears. |

## Test Cases

**Unit tests:**

- `WebApp.Tests/Services/EpubReaderThemeServiceTests.cs` — modeled on `EpubProgressServiceTests`; verify absent/malformed JSON fallback, default construction, global active-settings round trip, valid theme create/update/rename/delete, case-insensitive duplicate rejection, unknown-ID behavior, unsupported field/color rejection, atomic-write cleanup, and concurrent mutations preserving a valid document.
- `WebApp.Tests/Client/` — add focused state/helper tests if state reconciliation is extracted from `EpubReader.razor`; cover server-response application, Default/inherited color semantics, selection becoming Custom after a local edit, and retained pagination intent.

**Integration tests:**

- `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs` — use the existing `WebApplicationFactory` pattern to GET the default library, save active settings, run every named-theme CRUD route, assert `400`/`404` cases, and inspect that JSON responses do not contain the temporary archive root.
- `make test` — run the full isolated Docker Compose test suite after all unit and endpoint tests are added.

## Manual Verification

1. Create the documented archive layout with at least two EPUB files, then start the app with `make docker-run`.
2. Open a book, open Aa/Text settings, choose Arial, 24px, a non-default line spacing, a text color, and a background color. Navigate within the chapter, close the reader, and reopen this and a different book; verify the appearance is global and reading progress remains correct.
3. Save those choices as a named theme. Close/reopen the reader and apply it from the saved-theme list; verify all five fields change together.
4. Adjust one field, verify the reader marks the appearance Custom and the stored named theme remains unchanged; update it explicitly, then reopen and confirm the updated values persist.
5. Rename the theme, confirm case-insensitive duplicate and blank-name validation, then delete it. Confirm deletion leaves the visible current appearance intact.
6. Select Default in both app appearance modes. Verify default typography returns and reader colors track Kindle-paper light/dark application colors.
7. At desktop and at the existing small-screen breakpoint, operate every field and CRUD action with keyboard only. Verify labels, focus, status/error messaging, and touch-size controls remain usable.
8. Stop the app with `make docker-down`; do not inspect or expose any personal archive path in committed output.

## Definition of Done

- Requirements, Plan, and Validation are complete in this spec folder.
- The Books entry in `README.md` documents globally persisted reader themes.
- The new service, endpoints, DTOs, and reader UI follow the existing server/client boundary and pass `make test`.
- Unit and endpoint coverage cover persistence, CRUD, validation, compatibility fallback, and no-path exposure.
- Responsive, loading, busy, empty, error, keyboard, focus, and light/dark inherited-default states are implemented and manually checked.
- Current Microsoft Learn evidence for the implementation's ASP.NET Core/Blazor-specific decisions is added to `Plan.md`, or the documented pending state is resolved before implementation.

## Rollback Plan

Revert the reader-theme endpoint mappings in `ArchiveEndpoints.cs`, the `IEpubReaderThemeService` registration in `Program.cs`, and the `EpubReader.razor` theme-manager UI/state changes. The standalone `Books/Notes/pereneArchiveReaderThemes.json` file may be left in place safely because no existing feature reads it; removing the feature restores the current in-memory reader defaults without affecting book-progress JSON.
