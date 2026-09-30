# Validation: SQLite User Accounts and Folder ACL

## Table of Contents

- [Acceptance Criteria](#acceptance-criteria)
- [Test Cases](#test-cases)
- [Manual Verification](#manual-verification)
- [Definition of Done](#definition-of-done)
- [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | `Plan.md` contains a filled spike results table: every ERD spike item (1–7) and every antiforgery scenario (static SSR, startup, login, logout, expiry, role/`MustChangePassword` change, two tabs, JSON/multipart/DELETE handler behavior, coverage test) shows pass with a positive and a negative case; P1 merge is blocked otherwise. |
| FR1 (amended) | The antiforgery spike rows show an invalid/missing/stale/other-user token rejected on **both** a WASM-issued API request and each static account/admin form, each with a passing positive case; retry-on-400 is shown working for a buffered JSON request and **not attempted** for a streamed/multipart upload. |
| FR2 | App fails to start with a clear message when `Database__Path` is relative, missing, unwritable, or inside `/archive`/`/previews`/cut/composition roots; starts and creates the file on the `appdata` volume otherwise. |
| FR3 | A fresh DB is created purely by migrations; `dotnet ef migrations has-pending-model-changes` reports none; `PRAGMA foreign_keys` returns 1 on every opened connection; a bare delete violating an FK fails. |
| FR4 | No UI, endpoint, or DB write ever stores an email; register, external-login, forgot-password, and personal-data routes return 404; sign-in works with username only. |
| FR5 | Attempting to delete, deactivate, or demote the last active Admin is rejected with no state change; with two Admins it succeeds for one. |
| FR6 | Login, 2FA, recovery, change-password, and enroll pages render via static SSR (view-source shows server HTML, no WASM required), reachable by direct URL and `NavLink`; optional TOTP enrollment yields a scannable QR and 10 recovery codes; a user without TOTP signs in with password only. |
| FR7 | An unauthenticated request to any `/api/*` endpoint returns 401 and to any page redirects to login; only the listed anonymous routes respond; a newly added test endpoint without explicit policy is denied. |
| FR8 | Deactivated user, and user whose temporary password expired, cannot sign in at the password step, the TOTP step, or the recovery-code step; responses do not reveal expiry to someone who has not proven the password. |
| FR8 (amended) | Wrong password, unknown user, deactivated user, and expired temporary password all return the identical generic message and timing class before the password verifies; only a correct temporary password shows the "expired, ask an administrator" message. |
| FR9 | Admin-created user receives a one-time temporary password not present in logs, DB plaintext, or audit rows; reset and TOTP reset work; delete requires owner reassignment and completes in one transaction; users cannot reset an Admin unless acting as Admin. |
| FR10 | With `MustChangePassword`, all endpoints except change-password/logout return 403 `password_change_required` and pages redirect; in-grace session can change; post-grace session is signed out; after change both fields are cleared in the same transaction and the session continues. |
| FR11 | After deactivation or demotion the next request from an existing session is rejected (403/401) without waiting for cookie expiry; a password reset ends other sessions within the validation interval. |
| FR12 | `make admin-create USER=x` on an empty DB creates an Admin, prints the password once, and does not bind a port; `make admin-recover USER=x` resets password, clears TOTP, issues recovery codes, removes lockout, and writes an `AdminRecoveryCli` event with null actor; neither writes the password to normal logs. |
| FR13 | POST/PUT/PATCH/DELETE (JSON, multipart, form) without a valid credential return 400 and write a `CsrfRejected` audit row (no token, no path); with a valid one they succeed; the endpoint-coverage test fails when an unprotected unsafe endpoint **or account/admin form** (login, logout, 2FA, recovery, change password, enroll/disable/reset authenticator, admin user forms) is added without validation; the handler retries a buffered JSON request exactly once and never retries a streamed/multipart upload. |
| FR14 | Response cookies carry `HttpOnly`, `SameSite`, and `Secure` when the request is HTTPS; the host allowlist still returns 400 for unknown hosts. |
| FR15 | Signed-in shell shows the user menu; Admin sees Admin nav, non-admin does not; direct navigation to `/admin/*` as non-admin is denied server-side; sign-out ends the session and forces a full reload. |
| FR16 | After first start, `FOLDER` rows exist for each archive category root and the cut/composition roots; serialized API responses never contain the archive root or a relative path (tested by scanning responses). |
| FR17 | Fresh `ACCESS_POLICY` has Read allowed and Create/Write/Delete denied; editing it bumps `AuthzVersion`. |
| FR18 | Unique (folder, user) holds; tri-state values round-trip; stale `ConcurrencyStamp` writes are rejected. |
| FR19 | A table-driven resolver suite reproduces every step of the ERD worked example (defaults, John's Write-without-Create, Private MyLab blocking ancestor grants, stub-only Comics, owner of Drafts and its loss on Private gate, enforced deny, inert explicit rows) and the Read gate. |
| FR20 | Every row of the operation matrix and folder-and-parent table has a passing test, including owner-of-entry, boundary-changing move needing Manage/Admin, and Replace needing Write + Delete. |
| FR21 | For each trigger (permission write, `AccessMode`, owner, parent, status, policy, role, `IsActive`, `MustChangePassword`, user delete, completed move/rename) a test shows `AuthzVersion` increments in the same transaction and a cached decision is recomputed. |
| FR22 | Unreadable item → 404, readable but forbidden operation → 403, unauthenticated → 401 for stream, download, thumbnail, preview, subtitle, audio, cover, image, `?audio=N`, and each range request; `/previews` is not a static root. |
| FR23 | A user with Read only on `Books/Comics` sees a name-only stub for `Books`, cannot open it, and sees no MyLab items in listings, search results, counts, or facets; with a Private folder between them no stub appears. |
| FR24 | A user with Create on a folder who creates a subfolder becomes owner with R/W/C/D (not Manage) on it and it inherits the parent's mode unless Private was chosen. |
| FR25 | Manager cannot change a locked flag, delete a locked row, loosen a locked ancestor deny, grant beyond what they hold, touch their own row, or change `CanManage`/`IsEnforced`/Private switching; can change unlocked flags on the same row; concurrent Admin/manager edits resolve deterministically with one retry then conflict. |
| FR26 | Admin access page shows provenance for every cell, inert/suspended tags, the three-option owner-vs-deny dialog, and the Private impact list; every action goes through the same handler (a UI-made grant that the backend would refuse is impossible). |
| FR27 | A cut/composition/conversion job enqueued by a user without Read or Create is rejected at enqueue; a job whose user loses access while queued ends `Failed` with a generic reason; thumbnail generation still runs for all folders. |
| FR28 | Each listed action creates exactly one `AUDIT_EVENT`; a scan of all event `DetailJson` values finds no password, token, key, recovery code, or path. |
| FR29 | Deleting a user cascades their notes/progress/favorites/permissions/themes/views, nulls `CreatedByUserId`/`GrantedByUserId`/`AUDIT_EVENT.ActorUserId`/`JOB.UserId`, and is blocked while they own a folder. |
| FR30 | Partial unique index exists (`sqlite_master` check); two Active rows at one path fail; historical rows at the same path are allowed; identity keys computed for EPUB and CBZ and null for others. |
| FR31 | Each transition in the D15 table is exercised against the real index in the required order; unlisted transitions throw; classification cases (same identifier → confirmed, different → superseded, unreadable → NeedsReview) behave as specified and annotations never show against new content. |
| FR32 | A renamed/moved file with the same fingerprint is relinked automatically; two candidates go to review; a vanished file becomes `Missing` with data retained; reattach merges rows with newer `UpdatedAt` winning on unique conflicts; no data is auto-deleted. |
| FR33 | Note text containing `==========`, marker-like lines, and multi-line content round-trips intact; deleting one note removes exactly that row; two users' notes on one book are independent. |
| FR34 | For each of highlights, reading progress, comic progress, favorites, reader themes, storage views, two users have isolated rows; admin presets (null user) are visible to all; unique constraints enforced. |
| FR35 | A user cannot read or write another user's rows via any endpoint; after losing Read on the folder, endpoints return 404 and rows remain; access returning restores them. |
| FR36 | Importer fed the legacy fixtures populates the first Admin's rows, reports imported/skipped counts, leaves source files byte-identical, and a second run adds nothing. |
| FR37 | No reference to the old JSON file names remains in `WebApp/WebApp/Services` or DI; no code path writes them (grep and a filesystem-watch test). |
| FR38 | `theme.js` and sort-option `localStorage` behavior are unchanged. |
| FR39 | Existing composition/archive-mutation/conversion endpoints and UI polling return the same shapes backed by `JOB` rows; `JOB.PayloadJson` contains stable `MediaItemId`/`FolderId` identities and captured identity fingerprints only — no paths and no snapshot-scoped IDs. |
| FR40 | After killing the process mid-job and restarting, the job shows a terminal `Failed` ("interrupted") status (or resumed if supported) rather than disappearing. |
| FR41 | Output names remain `<prefix> NNNN.ext`; first use seeds from existing files; allocation uses no directory scan; counters never regress after file deletion. |
| FR42 | 100 parallel allocations for one prefix return 100 distinct consecutive numbers. |
| FR43 | Serialized job rows and status DTOs contain no archive root, relative path, or unresolvable snapshot ID (scan test). |
| FR44 | A `Planned` journal row with captured identities exists and is committed before the first disk change (verified by failing the disk step and inspecting the row); affected folders are `Busy` and reject concurrent operations. |
| FR45 | A destination that appears between plan and act is not overwritten; deletes land in trash, not unlinked; changed `AuthzVersion` triggers re-evaluation or `Aborted`. |
| FR46 | All four recovery tables pass: Move/Rename (forward, back, stranger at old path, stranger at destination, both-match, neither), Delete, Replace (every B/G/T combination), Create; mismatch or missing evidence yields `NeedsReview` and leaves folders inaccessible. |
| FR47 | Trash purge touches only the recorded path of committed rows past retention after re-checking identity; an unrelated file in the trash directory is never removed. |
| FR48 | Reconciliation fixtures: same-path, two-signal unique relink, single-signal → review, empty folder → review, inode reuse discarded when old path exists, private/explicit-permission subtrees → Admin confirmation, unmatched disk folder with Missing candidates → `Private` + `NeedsReview`. |
| FR49 | Review queue lists folders, media, and interrupted operations with evidence; each action is applied, audited, and previews the permission change. |
| FR50 | Reconciliation leaves `FOLDER_PERMISSION`/`OwnerUserId` byte-identical in its tests; pruned rows are gone; no journal field appears in any API response or log output. |
| FR51 | Crash-injection harness kills at each protocol step; restart converges to Committed or Aborted or NeedsReview per the tables, with no data loss in any run. |
| FR52 | With `FsFileId` forced null, folder recoveries lacking other evidence go to `NeedsReview`; file recoveries may still match on size + mtime + fingerprint. |
| FR53 | `make db-backup` produces a backup that opens, passes `PRAGMA integrity_check`, carries the expected migration version, and includes the key directory; a restore of it into a fresh volume boots with the same accounts; a copy of the live `.db`/`-wal`/`-shm` is not offered by any documented procedure; each phase's migration refuses to run (documented checklist/guard) without a recorded verified backup. |
| FR54 | A user whose temporary password expired before first sign-in cannot sign in; an Admin reset revives the account and clears the badge; an Admin-expired Admin is recovered by another Admin or `make admin-recover`; the Users list shows the "Temporary password expired" badge. |
| FR55 | Cookie and antiforgery token issued before an app restart (and before a container rebuild that keeps `appdata`) remain valid; with `appdata/keys` removed they become invalid and the user is sent to login without errors; keys appear in backups and never in logs. |
| FR56 | In P1 a non-Admin account cannot be created, activated, or signed in (UI disabled, API 403 with a clear message, `AccountLifecycleService` refuses); no configuration value enables it; once `IFolderAccessEnforcement` is registered (P2), creation and activation work and pre-existing accounts stay inactive until an Admin activates them. |
| FR57 | No code path, flag, or environment variable removes authentication; the README/runbook rollback section states the exposure and separates deployment rollback from emergency rollback; verified by a documentation check and a grep for any auth bypass switch. |
| FR58 | CLI commands exit non-zero when the schema version differs and never run migrations; `admin-create`/`admin-recover`/`db-backup` succeed while the web container is running and the running server honors the change (session ended, `AuthzVersion` bumped); `admin-recover` never touches `__EFMigrationsLock`; `make db-unlock-migration` refuses without `CONFIRM=yes` or when another process holds the database. |
| FR59 | A job enqueued from a snapshot ID, then restarted (new snapshot IDs), still locates its source by stable identity; it fails generically when the item is `Missing`/`Superseded`, its identity changed, or the user lost permission; a job is never run merely because it was authorized at enqueue. |
| FR60 | Cross-volume move: copy to destination-volume staging, verify, no-clobber publish, commit, then source to source-volume trash, in that order; crash during copy or before verification leaves the source intact and recovery deletes the recorded staging object and aborts (never reports the move complete); crash after publish/commit completes the source trashing; destination-volume-full aborts cleanly; same-volume moves still use rename. |

## Test Cases

All tests are xUnit under `WebApp.Tests`, run only through `make test`, using a real SQLite file in a per-test temp directory (never an in-memory substitute for FK, partial-index, or transaction-order behavior). New folders mirror the repo's `Configuration/`, `Services/`, `Endpoints/`, `Client/` layout plus `Data/`, `Identity/`, `Authorization/`.

**Unit tests:**

- `WebApp.Tests/Configuration/DatabaseOptionsTests.cs`: FR2 path validation, following `ThumbnailCacheOptions` tests.
- `WebApp.Tests/Data/AppDbContextTests.cs`: FR3 FK pragma, migrations create schema, pending-model-changes, FR29 cascade/SetNull/Restrict matrix, FR30 partial index, UTC `DateTime` round trip.
- `WebApp.Tests/Authorization/FolderPermissionResolverTests.cs`: FR19/FR20 table-driven cases covering every ERD rule and worked example.
- `WebApp.Tests/Authorization/DelegationRulesTests.cs` and `PermissionWriteServiceTests.cs`: FR25, including concurrency stamp conflicts and lock masks.
- `WebApp.Tests/Authorization/FolderAccessServiceTests.cs`: FR21 version invalidation per trigger, fresh vs cached, readable-set and stub logic (FR23).
- `WebApp.Tests/Identity/ApplicationSignInManagerTests.cs`: FR8 on all three sign-in paths; `AccountLifecycleServiceTests.cs`: FR5, FR9, FR10 (incl. atomic clear), last-admin guard, owner reassignment; `AdminCliTests.cs`: FR12.
- `WebApp.Tests/Services/MediaIdentityClassifierTests.cs`, `MediaItemRepositoryTests.cs`, `MediaReconciliationServiceTests.cs`: FR30–FR32 against the real index and the transition table.
- `WebApp.Tests/Services/Sqlite{EpubNote,EpubHighlight,EpubProgress,ComicProgress,ReaderTheme,ArchiveFavorites,CustomStorageView}ServiceTests.cs`: FR33–FR35, adapting the existing `*ServiceTests.cs` cases (delimiter-collision case added to notes); `LegacyDataImporterTests.cs`: FR36.
- `WebApp.Tests/Services/SqliteJobStoreTests.cs` (the three existing `*JobStatusStoreTests` re-pointed), `NamingCounterServiceTests.cs` (FR41–FR42, extending `CutNamingServiceTests`/`CompositionNamingServiceTests`/`ImageCropNamingServiceTests`).
- `WebApp.Tests/Services/FsOperationJournalTests.cs`, `FsRecoveryServiceTests.cs` (one class per op type), `FolderReconciliationServiceTests.cs`, `FsIdentityTests.cs`: FR44–FR52.
- `WebApp.Tests/Client/AntiforgeryDelegatingHandlerTests.cs`: header on unsafe methods only, exactly one refresh-and-retry, cached token dropped on auth change.

**Integration tests:**

- `WebApp.Tests/Endpoints/AuthenticationFlowTests.cs` (`WebApplicationFactory`, cookie container): FR4, FR6, FR7, FR10, FR11, FR14, FR15 — login → forced change → 2FA enroll → sign-in with TOTP (code computed in test) → logout; anonymous 401/redirect matrix.
- `WebApp.Tests/Endpoints/AntiforgeryCoverageTests.cs`: FR13 enumerates `EndpointDataSource`; negative/positive request per body type.
- `WebApp.Tests/Endpoints/EndpointAuthorizationMatrixTests.cs`: FR22 for each existing endpoint class; every `/api` endpoint declares a permission/Admin/self-scoped marker.
- Existing `WebApp.Tests/Endpoints/*Tests.cs` are updated to use a shared `AuthenticatedWebApplicationFactory` (test Admin and ordinary user) so their current assertions keep passing.
- `WebApp.Tests/Endpoints/AdminUserEndpointsTests.cs`, `AdminAccessEndpointsTests.cs`, `AdminReviewEndpointsTests.cs`: FR9, FR24–FR26, FR28, FR49.
- `WebApp.Tests/Endpoints/PathLeakTests.cs`: FR16, FR43, FR50 — serialize responses and logs from a representative run and assert the archive root and relative paths never appear.
- `WebApp.Tests/Services/CrashRecoveryTests.cs`: FR51 crash-injection harness (fault points between protocol steps, restart, assert convergence).
- P0 spike verification is a documented manual/browser checklist (below), not an automated test; ⚠️ TODO: add a Playwright-style automated check only if the repo adopts browser automation.
- ⚠️ TODO: Concurrency stress test (workers + requests writing concurrently) for SQLite busy handling.

**Regression tests for the review scenarios (added, not replacing the strategy above):**

1. *Rollback/backup (FR53, FR57):* `WebApp.Tests/Services/DatabaseBackupServiceTests.cs` — online backup while a writer runs opens, passes integrity check, has the expected migration version, includes keys; restore into a fresh directory boots; `WebApp.Tests/Endpoints/NoAuthBypassTests.cs` — no config/env combination yields an unauthenticated app; a P5 test asserts `FsRecoveryService` cannot be disabled while a non-terminal `FS_OPERATION` exists.
2. *Durable jobs (FR59):* `WebApp.Tests/Services/JobEnqueueServiceTests.cs` and `JobExecutionRecheckTests.cs` — enqueue from a snapshot ID, rebuild the snapshot (new IDs), run: succeeds by stable identity; mutate/remove the file, supersede the item, or revoke the user's permission between enqueue and run: job ends `Failed` with a generic reason.
3. *Antiforgery on forms (FR1, FR13):* `WebApp.Tests/Endpoints/AccountFormAntiforgeryTests.cs` — each account/admin form rejects missing, tampered, stale, and other-user tokens; `AntiforgeryDelegatingHandlerTests` — no retry for a streamed/multipart body, exactly one for buffered JSON.
4. *P1 policy (FR56):* `WebApp.Tests/Identity/PhaseOnePolicyTests.cs` — non-Admin create/activate/sign-in refused without `IFolderAccessEnforcement`; allowed with it; no setting flips the behavior.
5. *Temporary-password expiry (FR8, FR54):* `WebApp.Tests/Identity/TemporaryPasswordRecoveryTests.cs` — identical pre-verification responses; expired-message only after verification; Admin reset revives; `make admin-recover` path covered by `AdminCliTests`.
6. *Backup and Data Protection (FR53, FR55):* `WebApp.Tests/Security/DataProtectionPersistenceTests.cs` — cookie/antiforgery token survives host restart with a persisted key directory and fails without it.
7. *Cross-volume moves (FR60):* `WebApp.Tests/Services/CrossVolumeMoveTests.cs` and `CrossVolumeRecoveryTests.cs` — a test harness with two real mount points or an injectable `IFsVolumeProbe` and separate temp roots; crash injection at copy, verify, publish, commit, and trash steps; destination-full failure; partial staging never counted as complete. ⚠️ TODO: confirm the CI container can create two distinct filesystems (tmpfs mounts); otherwise the probe is faked and a manual two-volume check covers the real case.
8. *CLI/migrations (FR58):* `WebApp.Tests/Identity/AdminCliConcurrencyTests.cs` — CLI runs beside a live host on the same file; CLI refuses on schema mismatch and never migrates; `MigrationLockCommandTests.cs` — `db-unlock-migration` refuses without confirmation or while the database is held, and `admin-recover` leaves the lock table untouched.

## Manual Verification

1. `make docker-reset` (note this deletes `appdata`), `make docker-build`, then `make docker-run-bg`.
2. Open the app unauthenticated: confirm redirect to `/account/login`; confirm `curl -i http://localhost:8080/api/videos` returns 401.
3. Run `make admin-create USER=admin`; note the one-time password (check `make docker-logs` does not contain it). Sign in, complete the forced password change.
4. Enroll TOTP on the Manage 2FA page, scan the QR, store recovery codes, sign out, sign in with password + code, then sign in once with a recovery code.
5. As Admin create users `john` and `mia`; confirm the temporary password is shown once; sign in as `john`, confirm forced change.
6. Reset `john`'s password from another session and confirm the old session ends within about a minute; deactivate `mia` and confirm her next request is rejected.
7. Try to delete/demote the only Admin and confirm it is blocked; create a second Admin and retry.
8. On `/admin/users/{id}/access`: confirm Shared defaults (Read only), give `john` Write on `Books` while leaving Create off, set `Books/MyLab` Private and verify `john` gets 404 for its items, grant Read on MyLab and verify inert/provenance labels update; test the owner-vs-deny dialog and the Private impact list.
9. As `john`, upload into a folder he can Create in, create a subfolder (becomes owner), rename and delete it, and confirm siblings remain untouchable.
10. As two users, open the same EPUB: add notes (including text containing `==========`), highlights, progress, and favorites; confirm isolation; repeat with a CBZ.
11. Start a video conversion, `docker compose kill webapp`, restart, and confirm the job shows an interrupted/failed status instead of vanishing; run two cuts of the same video and confirm consecutive, non-colliding names.
12. On the NAS path outside the app, rename a folder and a book; run a scan; confirm the Review queue shows the ambiguous ones and that `Private`/explicit-permission subtrees are not auto-relinked; relink one and read the effect preview.
13. Kill the app mid-move (`docker compose kill webapp` during a large move), restart, confirm recovery converges and no files are lost.
14. Design pass: every new page in dark and Kindle-paper light at desktop, tablet, and phone widths; keyboard-only traversal with visible focus; screen-reader check of alerts/live regions; icon-only buttons have names, tooltips, and ≥ 40×40 targets; gold/green button hierarchy; reduced-motion enabled; CDN blocked → semantic controls still usable.
15. Run `make test` and confirm all tests pass from a clean checkout.
16. Before each phase's migration: run `make db-backup`, restore it into a scratch volume, confirm it boots with the same accounts, and record the backup path and migration version.
17. P1 only: confirm creating a non-Admin user is refused; after P2, confirm creation works and ordinary accounts see only Shared Read content.
18. Let a temporary password expire (or edit the expiry through the test hook in a dev build), confirm the generic message, then reissue from `/admin/users` and sign in.
19. `make docker-down`, `make docker-run-bg` (keeping `appdata`) and confirm the existing session is still valid; remove `appdata/keys` and confirm a clean return to login.
20. While the app runs, execute `make admin-recover USER=admin` and confirm the running app ends that admin's session; confirm `make db-unlock-migration` refuses without `CONFIRM=yes`.
21. P5 only: move a file between two different filesystems (different disks, or a second mounted volume) and kill the app during the copy; after restart confirm the source is intact and the partial destination staging file is gone; repeat after the verify step and after publish.

## Definition of Done

- Requirements, Plan, and Validation docs in this folder are updated, and the P0 spike results table in `Plan.md` is complete.
- Every phase's gate (its FRs' acceptance criteria and manual steps) passed before the next phase began, including G1 (no ordinary users before server-side folder authorization), G2 (JSON services retired only after a verified importer run and verified backup), G3 (P5 deployed only after same-volume and cross-volume recovery tests), and G4 (verified backup before each phase's migration).
- All existing tests still pass; new behavior has tests matching the repo's xUnit conventions; `make test` is green.
- All new NuGet packages and the `dotnet-ef` tool were installed via Docker (`make dotnet ...`), with versions and justification recorded in `Plan.md`.
- New UI follows the design guide and Bootstrap-first rules, with responsive, empty, loading, error, and accessibility states verified.
- Vendor decisions are supported by the Learn evidence in `Plan.md`, re-verified at implementation time.
- `AGENTS.md` (constraints: no-auth statement replaced, repo map, architecture, commands, design/security notes) is updated through `init-agent`, `.env.example`/`docker-compose*.yml`/`Makefile`/`Dockerfile` changes are documented, and the README "Current Supported Features" table has rows for accounts/2FA, folder access control, per-user notes/progress/favorites, durable jobs, and the Review queue.
- No real `.env` value, personal path, or LAN IP is committed; no physical/relative path appears in responses, logs, audit, or job payloads.

## Rollback Plan

Rollback is split into two different things, because reverting code is not the same as reverting data and must never silently remove security.

**A. Deployment rollback (normal):** return to the previous phase's build.

- Before each phase's migration a verified backup is taken (FR53, gate G4). Migrations are forward-only, so returning to the previous phase's build means restoring that phase's verified backup (database **and** Data Protection keys) with the web container stopped.
- **What a restore discards, and the runbook must say so:** every account change, permission change, note, highlight, progress/favorite/theme/view change, job record, audit event, and journal row written after the backup. Before restoring, take a fresh verified backup of the current state so nothing is unrecoverable, and tell users that recent notes/progress will be lost.
- Filesystem changes made by the app after the backup (moves, renames, deletes, uploads) are **not** undone by a database restore. After restoring, run reconciliation and review the Review queue; do not restore across an in-flight P5 operation (see below).
- **P3 specifics:** until FR37 lands, the legacy JSON/text files are untouched by the importer, so re-registering the JSON-backed services restores old behavior for data created before the import only; data created in SQLite since then is not in those files. After FR37 (gate G2: only after a verified importer run and verified backup), restore from the pre-P3 backup and the archived `Books/Notes` and `Dashboard` folders.
- **P4 specifics:** re-register the in-memory status stores and directory-scan naming services; `NamingCounters` and `JOB` rows can be ignored. Persisted jobs are not resumed.
- **P5 specifics:** the journal and `FsRecoveryService` must **never** be disabled while any `FS_OPERATION` row is non-terminal (Planned, DiskApplied, NeedsReview). First let recovery finish or have an Admin resolve every `NeedsReview` row in the Review queue; only then may the journaled path be reverted to direct operations. If a restore of the database is required while operations are unresolved, restore the matching backup only after a filesystem consistency check and with trash and staging directories preserved.

**B. Emergency rollback to an unauthenticated build (exceptional):**

- There is deliberately **no flag, environment variable, or automatic path** that turns authentication off (FR57). Once ordinary accounts are active, deploying a build without authentication exposes the whole archive to every device on the LAN.
- It requires an explicit, documented operator decision: the operator first tries the safer alternatives (restore a verified backup, deactivate ordinary accounts so only Admins can sign in, `make admin-recover` for lockout), and if they still choose to deploy an older unauthenticated build they must do so by checking out that commit manually, understanding the exposure, and treating the `appdata` volume as out of service (it is not deleted).
- **Emergency access without rollback:** `make admin-recover USER=<name>` restores Admin access without the web host. A stuck migration lock is cleared only with `make db-unlock-migration CONFIRM=yes` after confirming no migration process is alive (FR58); it is never part of admin recovery.
