# Validation: Resumable Archive Uploads

## Table of Contents

- [Acceptance Criteria](#acceptance-criteria)
- [Test Cases](#test-cases)
- [Manual Verification](#manual-verification)
- [Definition of Done](#definition-of-done)
- [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | Supported uploads can start in every writable category and nested folder; Trash returns forbidden and creates no session. |
| FR2 | Session creation returns only opaque/browser-safe values, rejects unsafe/unsupported names, invalid parent IDs, invalid sizes, and existing final names before creating a partial. |
| FR3 | Restarting the app preserves an incomplete session's metadata and `.part` in the isolated workspace; listing any category never exposes either. |
| FR4 | A valid next chunk advances `ReceivedBytes`; duplicate, gap, overlap, wrong-length, oversized, expired, and completed writes fail without altering acknowledged bytes. |
| FR5 | Boundary-sized test files receive the configured adaptive chunk size and never a client-selected arbitrary size. |
| FR6 | Chromium HTTPS/HTTP/2 requests enable browser request streaming; non-streaming local development still uploads bounded sequential chunks without full-file buffering. |
| FR7 | The UI percentage/bytes match the most recently returned server acknowledgement and presents the required status, rate/ETA when available, and accessible progress semantics. |
| FR8 | An interruption preserves uploaded bytes; reselecting matching name/size resumes at the stored offset, while Cancel removes only that session. |
| FR9 | Completion rejects a short/mismatched `.part`; a matching session atomically produces exactly one final archive item and removes temporary session artifacts. |
| FR10 | A session inactive for more than 24 hours is removed by the worker; an active/locked session is not. |
| FR11 | Endpoint JSON/error bodies and test-captured logs do not contain the temporary or archive root path. |
| FR12 | Existing supported-extension, collision, target-folder, and listing-refresh behavior works through sessions; README states the new capability. |

## Test Cases

**Unit tests (xUnit):**

- `WebApp.Tests/Services/ArchiveUploadServiceTests.cs`: create session in a nested Documents folder; append 2–3 exact sequential chunks; reload a new service instance against the same workspace; complete and verify bytes/final placement.
- `ArchiveUploadServiceTests.cs`: parameterize threshold boundaries (100 MB, 1 GB, 10 GB and just above/below) for adaptive chunk selection; assert configured maximum and invalid total-size handling.
- `ArchiveUploadServiceTests.cs`: reject offset gaps/duplicates/overlaps, truncated body, target collision at creation and completion, unsafe name, unsupported extension, Trash, expired session, and cancellation; assert no final file/path escape.
- `ArchiveUploadServiceTests.cs`: use a controllable clock and per-session concurrent calls to verify 24-hour expiry, activity refresh, atomic metadata, and cleanup/write exclusion.
- `WebApp.Tests/Services/ArchiveServiceTests.cs`: remove/replace `SaveUploadedFileAsync_*` tests with destination-validation/publish tests as responsibilities move; retain existing extension/name/collision containment coverage.
- `WebApp.Tests/Client/ArchiveUploadStateTests.cs`: if state is extracted, test acknowledged-progress calculations, rate/ETA edge cases, status transitions, and matching reselected file identity.

**Integration tests (WebApplicationFactory):**

- `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs`: exercise create → chunk → status → complete over `HttpContent` request bodies, asserting opaque IDs/DTOs and final listing refresh.
- Test a deliberately interrupted multi-chunk upload, recreate the factory over the same temporary archive root, query the session, send the remaining chunks, and verify final bytes.
- Test the chunk handler's HTTP statuses/error payloads for category, parent, offset, length, stale ID, expiration, completion, and cancel; search bodies for the temporary root path.
- Verify the former multipart `/upload` route is unavailable after migration and no `IFormFile` buffered path remains.

## Manual Verification

1. Create the expected archive folders under the configured private `VIDEO_ROOT`, then start with `make docker-run`.
2. In a writable Archive Browser folder and a nested subfolder, upload supported files around 20 MB, 100 MB, 1 GB, and (where storage allows) 10 GB; confirm chunk sizes/progress change according to the configured policy and the final item appears only after completion.
3. During a 1 GB upload, stop the browser/network near 40%. Confirm the page later shows an interrupted upload with preserved acknowledged bytes, reselect the same file, and confirm it resumes from that offset rather than zero.
4. Reselect a same-named file with a different length; confirm Resume is refused and partial bytes remain until the user cancels or provides the matching file.
5. Cancel an interrupted upload and confirm only its `.uploads` artifacts disappear; no target archive item appears.
6. Restart the Compose app during an interrupted upload, return to the same folder, and resume it after reselecting the file.
7. Set a short test TTL/cadence in local configuration, create an inactive session, wait for cleanup, and confirm it is removed; restore the 24-hour default afterward.
8. Run `make test`; confirm all tests pass. Inspect API responses and normal logs to confirm no physical archive/temp paths are emitted.
9. In a Chromium browser over an HTTPS/HTTP/2 deployment, confirm request streaming is enabled in browser diagnostics; repeat through the local HTTP Compose URL to confirm bounded fallback works.

## Definition of Done

- Requirements, Plan, and Validation documents are implemented from this spec.
- `ArchiveUploadOptions`, persistent session storage, sequential endpoints, explicit commit/cancel, and TTL cleanup are registered and covered by tests.
- Legacy multipart/IFormFile archive upload code is removed only after equivalent session coverage is passing.
- `ArchiveBrowser.razor` provides responsive accessible per-file acknowledged progress and recovery actions; its isolated JS bridge owns only file slicing.
- `make test` passes in the Docker Compose test stack.
- `README.md` Current Supported Features describes resumable archive uploads; no private filesystem paths, paths in normal logs, or new packages are introduced.
- The official Microsoft Learn evidence in `Plan.md` remains applicable, including the HTTPS/HTTP/2 limitation for request streaming.

## Rollback Plan

- Revert the registrations and routes for `ArchiveUploadService`/`ArchiveUploadCleanupWorker`, the client upload coordinator/interop module, and the related DTOs. Restore the prior `ArchiveEndpoints.UploadAsync` and `ArchiveService.SaveUploadedFileAsync` only as one compatible rollback change set.
- Existing committed archive files are unaffected. Incomplete `.uploads` sessions are isolated disposable artifacts; after rollback they can be retained until TTL cleanup is restored or deleted manually by the operator from the server-only workspace.
