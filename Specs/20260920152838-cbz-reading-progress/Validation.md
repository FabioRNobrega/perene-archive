# Validation: CBZ Reading Progress

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | Valid opaque comic IDs load/save a page index; invalid/cross-category IDs return 404. |
| FR2 | Different file versions do not share progress. |
| FR3 | Reopening restores a valid page; absent, corrupt, or out-of-range data opens page one. |
| FR4 | Navigating one page triggers automatic save and reopening restores it. |
| FR5 | Concurrent saves remain valid JSON; temporary files are cleaned up. |
| FR6 | Responses and JSON contain no archive path or ZIP entry name. |

## Test Cases

**Unit tests:**

- `WebApp.Tests/Services/ComicProgressServiceTests.cs` — missing, round-trip, overwrite, changed-version isolation, malformed JSON, and atomic-write cleanup, matching `EpubProgressServiceTests.cs`.

**Integration tests:**

- `WebApp.Tests/Endpoints/ArchiveEndpointsComicProgressTests.cs` — GET/PUT success, input validation, category isolation, and response path-disclosure checks using the existing `VideoManagerFactory` pattern.

## Manual Verification

1. Start the application with `make docker-run`.
2. Open a CBZ, navigate to a later page, close it, and reopen it.
3. Confirm it restores the same page; confirm a changed/replaced CBZ starts at page one.
4. Confirm network interruption during saving does not stop navigation.
5. Run `make test`.

## Definition of Done

- Requirements, plan, and validation documents exist in this spec folder.
- Service and endpoint tests cover all requirements.
- `make test` passes through Docker Compose.
- Comic UI restores and automatically saves progress without exposing paths or ZIP entries.
- README feature table is updated during implementation.

## Rollback Plan

Remove comic progress routes, registration, service, and viewer load/save calls. The JSON file is non-authoritative and may remain without affecting CBZ viewing.
