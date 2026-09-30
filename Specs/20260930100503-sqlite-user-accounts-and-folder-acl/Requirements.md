# Requirements: SQLite User Accounts and Folder ACL

## Table of Contents

- [Problem Statement](#problem-statement)
- [Baseline Documents](#baseline-documents)
- [Delivery Phases](#delivery-phases)
- [User Stories](#user-stories)
- [Functional Requirements](#functional-requirements)
- [Non-Functional Requirements](#non-functional-requirements)
- [Out of Scope](#out-of-scope)
- [Open Questions](#open-questions)

## Problem Statement

Perene Archive has no authentication or authorization layer (`AGENTS.md`, Constraints): every device on the trusted LAN reads, uploads, moves, deletes, and edits everything, and all bookkeeping is shared. Notes, highlights, reading progress, favorites, reader themes, and custom dashboard views live in hand-rolled JSON/text files under `{ArchiveRoot}/Books/Notes` and `{ArchiveRoot}/Dashboard` (`EpubNoteService`, `EpubHighlightService`, `EpubProgressService`, `ComicProgressService`, `EpubReaderThemeService`, `ArchiveFavoritesService`, `CustomStorageViewService`), keyed by a fragile SHA-256 of category/item/size/mtime, so all readers share one set and a renamed or re-encoded file silently orphans its record. Job status (`CompositionJobStatusStore`, `ArchiveMutationJobStatusStore`, `VideoConversionJobStatusStore`) exists only in memory, and the four `*NamingService` classes derive "next number" by scanning directories (a TOCTOU race).

This spec implements the design in [`sqlite-migration-user-access-erd.md`](sqlite-migration-user-access-erd.md) (v7, full ERD) and the relevant inventory in [`sqlite-migration-candidates.md`](sqlite-migration-candidates.md): a SQLite database owned by the application, ASP.NET Core Identity cookie accounts created only by an Admin, per-(user, folder) permissions with inheritance and Private folders, per-user notes/highlights/progress/favorites/themes/views, durable jobs, atomic naming counters, and a journaled filesystem-operation layer. It **supersedes** the AGENTS.md constraint "no authentication/authorization layer"; the LAN-only, no-Internet-hosting, host-header-allowlist constraints remain.

## Baseline Documents

These two analysis documents live in this spec folder as the design baseline and are referenced by the FRs, Plan, and Validation. Where a requirement here differs from them, **this spec wins** (notably: Shared default is Read-only, TOTP is optional, phases are gated, and the review amendments FR53–FR60 are new).

- [`sqlite-migration-user-access-erd.md`](sqlite-migration-user-access-erd.md) — v7 user/folder-ACL ERD: decisions D1–D15, resolution algorithm, operation matrix, filesystem journal, identity and reconciliation rules, worked example.
- [`sqlite-migration-candidates.md`](sqlite-migration-candidates.md) — inventory of JSON/text/in-memory stores and naming counters that migrate to SQLite.

## Delivery Phases

The user chose the **full ERD** in one spec. Because the pieces have hard ordering dependencies and each phase is independently shippable and testable, implementation proceeds in gated phases; a phase is not started until the previous phase's Validation checks pass.

| Phase | Content | FRs |
| --- | --- | --- |
| P0 | Spike gate: Blazor Identity in this app's global-WASM shape, antiforgery mechanism (API **and** static forms), SQLite FK/`AuthzVersion`, SignInManager paths | FR1 |
| P1 | SQLite foundation, Identity, login/logout/2FA, **Admin-only sign-in**, admin user management, offline admin CLI, fallback auth policy, CSRF, backups and Data Protection keys | FR2–FR15, FR53–FR55, FR57–FR58 |
| P2 | Folder/permission model, resolution handler, endpoint + job enforcement, admin access UI; **activates ordinary accounts** | FR16–FR29, FR56 |
| P3 | Media identity and per-user data migration from the JSON/text files | FR30–FR38 |
| P4 | Unified durable `JOB` table (stable server-side identities) and atomic naming counters | FR39–FR43, FR59 |
| P5 | `FS_OPERATION` journal (same-volume and cross-volume), reconciliation, Review queue | FR44–FR52, FR60 |

**Explicit phase gates** (a phase may not ship, and later phases may not start, until its gate is met):

- **G1 (P1 → ordinary users):** no non-Admin account can be created, activated, or signed in until server-side folder authorization (P2: FR19–FR22) is registered and enforced; see FR56.
- **G2 (P3 → retiring JSON services):** the legacy JSON/text services are removed (FR37) only after the importer has run and been verified (FR36) **and** a verified pre-P3 backup exists (FR53).
- **G3 (P5 → deploy):** P5 is not deployed until recovery has been tested on both same-volume and cross-volume operations (FR46, FR60, FR51).
- **G4 (every phase):** a verified backup is taken and restore-tested immediately before applying that phase's migrations (FR53).

## User Stories

- Given a fresh install with no accounts, when the operator runs `make admin-create`, then the first Admin exists, no default credential exists, and every page and `/api` endpoint except the login flow requires sign-in.
- Given an Admin, when they create a user and hand over the one-time temporary password, then the user must change it at first sign-in and the Admin never sees the new password.
- Given two users reading the same EPUB, when each adds notes, highlights, favorites, and progress, then neither sees the other's data.
- Given `Books/MyLab` set to Private and John holding Read only on `Books`, when John requests any MyLab folder, listing, or file stream, then he receives 404 and nothing under it appears in search or counts.
- Given a manager with Manage on `Books` and an Admin-locked Read flag on Ana's row, when the manager tries to change Ana's Read or delete the row, then the request is rejected and the unlocked Write flag remains editable.
- Given the process is killed between the rename and the database commit of a folder move, when the app restarts, then reconciliation rolls forward or backward by object identity and never guesses.
- Given the app restarts mid video conversion, when the user reopens the page, then the job's last durable status is shown.

## Functional Requirements

### P0 — Spike gate

1. FR1 — Before P1 code is merged, a throwaway-branch spike proves, in this app's shape (global Interactive WebAssembly, `prerender: false`, client-owned `Routes.razor`), all seven items of the ERD's "Hands-on Blazor Identity spike" checklist (static-SSR account pages reachable via `[ExcludeFromInteractiveRouting]`; conditional `PageRenderMode` in `WebApp/WebApp/Components/App.razor`; cookie written by static SSR honored by WASM `fetch`; `AuthorizeRouteView` + serialization/deserialization of auth state after login/logout/reset; one antiforgery token-delivery mechanism across all listed scenarios; `SignInManager` overrides on password, 2FA, and recovery-code paths; SQLite `foreign_keys` and atomic `AuthzVersion` bump). The antiforgery proof must show an **invalid, missing, stale, and other-user token is rejected on both a WASM-issued API request and a static-SSR account form** (login, logout, change password, TOTP enroll/disable/reset, and an admin form), each with a positive case. Results are recorded in `Plan.md`; if no antiforgery mechanism passes, the decision returns to the user.

### P1 — Foundation, authentication, administration

2. FR2 — The app owns one SQLite database file at a configured absolute path (`Database__Path`, default `/appdata/perene.db`) on a dedicated Docker named volume `appdata`; a validated `DatabaseOptions` type fails startup if the directory is missing or unwritable, disjoint from `/archive`, `/previews`, `/videos-cuts`, and `/videos-composition`.
3. FR3 — Schema is created and evolved only by EF Core migrations applied at startup; SQLite table-rebuild limitations are handled by explicit migration code; foreign keys are enforced on every connection and a test asserts it.
4. FR4 — Identity uses `ApplicationUser` (`DisplayName`, `CreatedByUserId`, `CreatedAt`, `IsActive`, `MustChangePassword`, `TemporaryPasswordExpiresAt`) with username + password cookie sign-in only; email is never collected, requested, or confirmed; there is no self-registration, external login, forgot-password, or personal-data page.
5. FR5 — There is exactly one role, `Admin`; the last active Admin can never be deleted, deactivated, or demoted.
6. FR6 — Login, logout, TOTP login, recovery-code login, change-password, and authenticator-enrollment pages are static SSR server pages (`[ExcludeFromInteractiveRouting]`) built with Bootstrap per the design guide; TOTP is **optional and user-enrolled** (QR rendered server-side as inline SVG, 10 recovery codes generated at enrollment).
7. FR7 — A fallback authorization policy requires an authenticated user for every endpoint and page; only login-flow pages, static assets needed by them, and the health-free error page are anonymous.
8. FR8 — `ApplicationSignInManager` rejects sign-in when `IsActive = false` or when `MustChangePassword` is set and `TemporaryPasswordExpiresAt` has passed (checked after the password verifies), on the password, 2FA, and recovery-code paths. Before the password verifies, every failure (unknown user, wrong password, deactivated, expired) returns the same generic message; only a caller who proved the temporary password sees "This temporary password has expired. Ask an administrator for a new one." (FR54 defines the recovery).
9. FR9 — Admin can create a user (username only; server generates a temporary password shown once, never logged or stored), reset a password via `GeneratePasswordResetTokenAsync`/`ResetPasswordAsync` in one server request (sets `MustChangePassword`, 24 h expiry, updates the security stamp), reset TOTP, deactivate/reactivate, and delete (only after reassigning every owned folder in the same transaction).
10. FR10 — While `MustChangePassword` is set, only the change-password page/endpoint and logout are reachable; every other request returns 403 `password_change_required` and the UI redirects; in-grace sessions may finish the change, post-grace sessions are signed out; a successful change clears both fields atomically and refreshes sign-in.
11. FR11 — Deactivation, role change, and password reset invalidate sessions promptly: `SecurityStampValidatorOptions.ValidationInterval` is lowered (default 1 minute), and account state/role are read from the version-checked authorization cache rather than trusted from cookie claims.
12. FR12 — An offline CLI mode of the same image (`make admin-create USER=<name>`, `make admin-recover USER=<name>`) creates the first Admin or recovers one (password token reset, `MustChangePassword`, TOTP cleared, new recovery codes, security stamp updated, lockout removed), prints the temporary password to the terminal once, does not start the web host, and writes an `AdminRecoveryCli` audit event with a null actor.
13. FR13 — Every non-safe endpoint (POST/PUT/PATCH/DELETE), JSON, DELETE, and multipart included, lives in a route group whose endpoint filter validates antiforgery; the WASM client attaches the credential through one `DelegatingHandler` per the mechanism chosen in FR1. The **static-SSR Identity and admin forms are a separate request path and are protected independently** by the Blazor form token (`UseAntiforgery` + `AntiforgeryToken`/`EditForm`), never opted out. The coverage test enumerates `EndpointDataSource` **and** the account/admin form endpoints (login, logout, 2FA, recovery, change password, enroll/disable/reset authenticator, admin user forms) and fails if any unsafe one lacks validation; nothing on a cookie endpoint calls `DisableAntiforgery()`. The handler's refresh-and-retry applies **only to replayable requests** (buffered JSON/form bodies); requests with streamed or multipart upload bodies are never auto-retried, and the client surfaces a "session/form expired, please retry" state instead.
14. FR14 — Cookies are `HttpOnly`, `SameSite=Lax` or `Strict`, `Secure` `SameAsRequest`; the existing host-header allowlist and HTTPS behavior are unchanged.
15. FR15 — The UI has a persistent shell user menu (display name, Manage account/2FA, Sign out) and Admin-only navigation to `/admin/users`; `Routes.razor` uses `AuthorizeRouteView`; client auth state uses `AddAuthenticationStateSerialization`/`Deserialization`; UI hiding is cosmetic and never the enforcement.

### P2 — Folders and ACL

16. FR16 — `FOLDER` (stable `FolderId`, `ParentFolderId`, `OwnerUserId`, `AccessMode` Inherit|Shared|Private, `Status`, server-only `RelativePath`/`FsFileId`/`ChildFingerprint`) mirrors the archive tree under `/archive`, seeded from the existing category roots and the cut/composition output roots; paths never reach the browser or normal logs.
17. FR17 — `ACCESS_POLICY` singleton holds the Shared defaults (**seed: Read allowed; Create, Write, Delete denied**) and the monotonic `AuthzVersion`.
18. FR18 — `FOLDER_PERMISSION` stores one tri-state row per (folder, user) with `CanRead/CanWrite/CanCreate/CanDelete/CanManage`, `IsEnforced`, `AdminLockedMask`, `ConcurrencyStamp`, and `GrantedByUserId` (last writer).
19. FR19 — A single `FolderPermissionAuthorizationHandler` implements the ERD resolution algorithm exactly (Admin; enforced deny; Private gate on every strict Private ancestor; explicit row → ownership → ancestor rows up to the Private boundary → access-mode default; Read gate; compound operations), and is the only place that decision is made.
20. FR20 — The operation matrix and the folder-and-parent rules (including owner-of-entry and boundary-changing moves) are applied identically by every endpoint and worker via `IFolderAccessService`, with a cached readable-folder set and `AuthorizeFreshAsync` for delete/move/rename/replace/permission changes/admin actions/job execution.
21. FR21 — `AuthzVersion` is bumped in the same transaction as every authorization-relevant change; cached decisions older than the current version are recomputed.
22. FR22 — All archive/video/cut/composition endpoints (`Endpoints/*.cs`) check the correct permission per the enforcement matrix: 401 unauthenticated, 404 when Read is missing, 403 when readable but forbidden; per-request checks include each range request; thumbnails, previews, subtitles, covers, images, and audio-track remuxes are endpoint-authorized and never static roots.
23. FR23 — Listings, search, counts, facets, and folder stubs are filtered by the readable-folder set inside the query; pass-through stub breadcrumbs (name only) appear only when no Private folder lies between the readable descendant and the unreadable ancestor.
24. FR24 — Creating a folder through the app records the creator as owner (Read/Write/Create/Delete on that exact folder, never Manage) and inherits the parent's `AccessMode` unless the creator chooses Private.
25. FR25 — Manage delegation follows the ERD: default Deny, Admin-only grant, no escalation beyond what the grantor holds, per-flag Admin lock, no deleting rows with locked bits, no loosening a locked ancestor deny, optimistic concurrency with one reload-and-retry, lock bits computed server-side; `IsEnforced`, `CanManage`, and Private/Shared switching are Admin-only.
26. FR26 — `/admin/users/{id}/access` shows every folder with Read/Write/Create/Delete/Manage cells, provenance labels (default/owner/inherited from X/explicit), inert and suspended-ownership tags, per-cell reset, mode badges, the owner-vs-deny dialog with its three options, and the Switch-to-Private impact list with "Grant Read to these N users".
27. FR27 — Background jobs (cut, composition, conversion, audio-track) authorize at enqueue (Read on sources, Create on output) and again at execution with the stored `JobId` user, never trusting that a queued job was authorized when it was created (see FR59 for identity rechecks); thumbnail/preview/subtitle generation runs as a system principal.
28. FR28 — Every permission, role, password/TOTP reset, user lifecycle, owner reassignment, and CSRF rejection writes an `AUDIT_EVENT` with no secrets or physical paths.
29. FR29 — The four cascade rules hold: user deletion cascades user-scoped rows, sets null on nullable audit/job/grantor/creator FKs, and restricts on `FOLDER.OwnerUserId`.

### P3 — Per-user data and media identity

30. FR30 — `MEDIA_ITEM` gives each archive file a stable `MediaItemId` with `Category`, server-only path, `ContentFingerprint`, `IdentityKey` (EPUB: OPF identifier + normalized title/creator; CBZ: page count + first/last entry name+CRC; otherwise null), `ContentRevision`, `Status`, and a partial unique index on `(FolderId, RelativePath) WHERE Status = 'Active'`.
31. FR31 — Same-path changes are classified confirmed update / identity-changing replacement / uncertain per D13, with the free-then-insert transaction order and the D15 transition table (anything unlisted is rejected); all path lookups go through one repository method that includes `Status = 'Active'`.
32. FR32 — Scan-time reconciliation relinks moved files by `ContentFingerprint`, sends duplicate candidates and unmatched items to `Missing`/`NeedsReview`, and never auto-deletes user data; an Admin can reattach or keep archived from the Review queue.
33. FR33 — `BOOK_NOTE` replaces `EpubNoteService`'s delimiter-based text file; notes are per (user, media item) and cannot be corrupted by note content.
34. FR34 — `BOOK_HIGHLIGHT`, `READING_PROGRESS`, `COMIC_PROGRESS`, `FAVORITE`, `READER_THEME` (nullable user = admin preset), and `CUSTOM_STORAGE_VIEW` (nullable user = global view) replace their JSON files with per-user rows and unique constraints; `ContentRevision` is stored on highlights and progress.
35. FR35 — Reading any of the above requires the row to belong to the caller and Read on the item's folder; rows are kept but inaccessible when access is lost; Admin has no UI to read others' notes.
36. FR36 — A one-time, idempotent importer migrates existing JSON/text data into the first Admin's account (resolving the old hash keys against the current archive), leaves the source files untouched, records what it imported and skipped, and can be re-run without duplicates.
37. FR37 — After import, the legacy JSON/text services are removed from DI and no code path writes the old files.
38. FR38 — `localStorage` theme and sort preferences stay browser-local (unchanged).

### P4 — Jobs and naming counters

39. FR39 — A `JOB` table (type Composition|ArchiveMutation|VideoConversion|Cut, status, `PayloadJson`, `UserId`) backs the three status stores behind their existing interfaces so current endpoints and UI polling keep working. `PayloadJson` holds **stable server-side identities** (`MediaItemId`/`FolderId` plus the identity fingerprint captured at enqueue, FR59), not browser snapshot IDs and never paths.
40. FR40 — On startup, non-terminal jobs from a previous process are marked `Failed` with a generic "interrupted by restart" reason (or resumed where the existing worker already supports it); the UI shows the durable status.
41. FR41 — A `NamingCounters(Prefix, Kind, LastNumber)` table replaces directory scans in `CutNamingService`, `CompositionNamingService`, `ImageCropNamingService`, and `VideoConversionNamingService` through one atomic upsert-returning transaction; output names stay `<prefix> NNNN.ext` and existing files seed the counter on first use so numbers never regress.
42. FR42 — A concurrent-allocation test proves no two callers receive the same number.
43. FR43 — Job payloads and statuses never contain physical or root-relative paths or snapshot-scoped opaque IDs that cannot be resolved after a restart.

### P5 — Filesystem journal and reconciliation

44. FR44 — Every app-initiated move/rename/delete/replace/create writes a durable `FS_OPERATION` row (server-only paths, `SubjectIdentity`, `DestOccupantIdentity`, `StagingIdentity`, `AuthzVersionAtPlan`) **before** touching disk and marks affected folders `Busy`.
45. FR45 — Disk steps use no-clobber primitives, deletes and replaced content go to a per-volume trash, and identity plus `AuthzVersion` are re-validated immediately before acting.
46. FR46 — Startup and periodic reconciliation processes every non-terminal row using the Move/Rename, Delete, Replace, and Create recovery tables; recovery acts only when the object at the path matches the recorded identity; strangers are never touched; mismatch or missing evidence → `NeedsReview` (folders and paths stay `Busy` and inaccessible).
47. FR47 — Trash is purged only for committed rows past retention, at the exact recorded path, after an identity re-check.
48. FR48 — Folder reconciliation of NAS-side changes uses the ERD tiers (same path; ≥ 2 independent signals, bidirectionally unique, ≥ 3 children for fingerprint use, inode-reuse check); Private/enforced/explicit-permission subtrees always need Admin confirmation; unmatched disk folders are `Private` + `NeedsReview` when Missing candidates exist.
49. FR49 — `/admin/folders/review` lists Missing/NeedsReview folders, media items, and interrupted operations with evidence and relink/accept/reattach/keep-archived actions, previewing the permission effect.
50. FR50 — Reconciliation never modifies permissions or ownership; journal rows are pruned after a retention window and never sent to the browser or logged.
51. FR51 — Crash-injection tests kill the process between each protocol step and assert convergence (including a stranger at the old path, a swapped destination, and every Replace B/G/T combination).
52. FR52 — If reading `st_dev:st_ino` is unavailable or unreliable on the target mount, `FsFileId` is null and every folder recovery lacking other evidence goes to `NeedsReview`.

### Review amendments (backups, gates, recovery, cross-volume, CLI)

53. FR53 — **Backup before every phase.** Before applying any phase's migrations, a verified backup exists: either (a) an **online** backup made with SQLite's backup API or `VACUUM INTO` (via a `make db-backup` target that runs in the existing image), or (b) an **offline** copy taken with the web container stopped (database plus any `-wal`/`-shm` together). Copying the live `.db`/`-wal`/`-shm` files while the app accepts writes is explicitly **not** a supported backup. A backup is "verified" only after it opens, passes `PRAGMA integrity_check`, and contains the expected migration version. The same backup set includes the Data Protection key ring (FR55).
54. FR54 — **Temporary-password expiry has a defined recovery.** If the 24-hour period lapses before the user signs in, sign-in fails per FR8; the user contacts an Admin, who issues a new reset from `/admin/users` (which always overwrites password, `MustChangePassword`, and expiry, and is never blocked by an expired one); an Admin whose own temporary password expired is recovered by another Admin or by `make admin-recover`. The Users list shows a "Temporary password expired" badge so Admins can see who needs a reissue.
55. FR55 — **ASP.NET Core Data Protection keys are persisted** to a dedicated directory on the `appdata` volume (`/appdata/keys`, application name fixed) so cookies, antiforgery tokens, and Identity token providers survive container rebuilds; the key directory is included in every backup and restore, and a test proves a cookie and antiforgery token issued before an app restart remain valid afterward.
56. FR56 — **P1 authorization policy (gate G1).** Until server-side folder authorization (FR19–FR22) is registered, only Admin accounts may sign in: creating a non-Admin account, activating one, or signing one in is refused with a clear message, and the admin UI disables "New user" for non-Admins. This is an enforced startup invariant tied to the registration of `IFolderAccessService`, not a configuration switch that can be turned off. On P2, existing non-Admin accounts (if any) remain inactive until an Admin activates them after reviewing the Shared defaults.
57. FR57 — **Emergency rollback is an explicit operator decision.** Once non-Admin accounts are active, deploying a build without authentication is never automatic, never a flag, and is documented as exposing the whole archive to every LAN device; the documented alternatives are restoring a verified backup and/or disabling ordinary accounts.
58. FR58 — **CLI and migration concurrency.** Schema migrations run only at web-host startup, in one process. The offline CLI commands (`admin-create`, `admin-recover`, `db-backup`) never migrate; they refuse to run if the database schema version is not current. `admin-create`/`admin-recover`/`db-backup` are permitted while the web container is running (WAL, `busy_timeout`, short transactions; account changes update the security stamp and bump `AuthzVersion` in the same transaction so the running server sees them). Clearing the `__EFMigrationsLock` row is **never automatic** and never part of `admin-recover`: it requires a separate explicit command (`make db-unlock-migration`) that demands operator confirmation and first checks that no migrating process is alive.
59. FR59 — **Durable jobs resolve to stable identities.** When a job is enqueued the browser's snapshot-scoped opaque ID is resolved to `MediaItemId`/`FolderId` and the identity captured at that moment (size, mtime, `ContentFingerprint`, `ContentRevision`); both are persisted with the job. At execution start the worker rechecks that the media item is still `Active` and its current identity matches, and re-authorizes the stored user fresh; a mismatch, `Missing`/`Superseded` status, or a denied permission ends the job `Failed` with a generic reason. Startup never replays a persisted job merely because it was once authorized.
60. FR60 — **Cross-volume moves use a separate protocol.** Before acting, the journal compares `st_dev` (or an equivalent mount check) of source and destination; same-volume operations use the rename protocol, cross-volume operations use a `CrossVolumeMove` op: (1) plan and journal, (2) copy to a staging file **on the destination volume**, (3) verify the copy (size plus full or chunked hash against the recorded `SubjectIdentity`), (4) publish staging to the destination with a no-clobber primitive, (5) commit the database update, (6) only then move the source into the **source volume's** trash, (7) purge trash by retention. A crash during copying or before verification is **never** interpreted as a completed move: recovery deletes the recorded staging object after an identity check and aborts, leaving the source untouched; a source is never trashed before step 5 commits. Folder moves across volumes are performed per descendant file under one journaled parent operation, or refused with a clear message if not supported in P5. Tests cover crash at each step and a destination-volume-full failure.

## Non-Functional Requirements

- **Docker only:** all restores, migrations (`dotnet ef`), builds, and tests run through the Makefile in Docker; every NuGet package is added with `make dotnet ARGS="add <project> package <id>"` (packages: `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Net.Codecrete.QrCodeGenerator`), with versions pinned to the .NET 10 line and each choice justified in `Plan.md`. `dotnet-ef` is installed as a tool inside the container.
- **Fail closed:** ambiguity denies; 404 hides unreadable content; unmatched items are Private until reviewed.
- **Privacy boundary:** no physical/root-relative path to the browser, logs, audit details, job payloads, or error messages; opaque IDs stay snapshot-scoped and authorization is evaluated per request, never baked into an ID.
- **Design system:** all new UI follows `Specs/20260827194328-perene-tech-design-system-refactor/design-guide-en.html` — Bootstrap 5.3.8 components/utilities, Bootstrap Icons at `currentColor`, gold `btn-primary` for the single principal action and green `btn-secondary` for complementary actions, semantic variants only for matching meaning, Zilla Slab headings and Montserrat body, dark and Kindle-paper light tokens, WCAG AA, icon-only controls with accessible names, tooltips, and ≥ 40×40 targets, live regions for status/errors, reduced-motion respect, responsive Bootstrap layout; custom CSS only for behavior Bootstrap cannot express.
- **Error isolation:** awaited JS interop and API calls in new client components sit inside `FeatureErrorBoundary`-style isolation per `Specs/20260928110540-error-isolation-boundaries-async-js`.
- **Performance:** one integer read per request for `AuthzVersion`; readable-folder set and per-user flags cached per version; listing/search cost stays bounded at household scale (thousands of folders, tens of thousands of media items).
- **Security:** Identity password policy applies to temporary and chosen passwords; lockout counts failed sign-ins; secrets (passwords, TOTP keys, recovery codes) never logged or audited.
- **Design principles:** small interfaces (`IFolderAccessService`, `IMediaItemRepository`, `IFsOperationJournal`, `IJobStore`, `INamingCounterService`, `IAuditWriter`), provider-specific SQLite code behind repositories/`DbContext` configuration, handlers and planners pure and unit-testable, no switch-based god services.
- **Testability:** tests use a real SQLite file in a temp directory (never an in-memory fake for the partial index or FK behavior) and run only via `make test`; `docker-compose.test.yml` gets a disposable `Database__Path`.
- **Documentation:** on implementation, update `AGENTS.md` (constraints, repo map, architecture, commands) via `init-agent` and the README "Current Supported Features" table, per repo rules.

## Out of Scope

- Self-registration, email, password-recovery email, social/external login, JWT/bearer tokens, or Internet-hosted access.
- A "traverse only" permission, blind drop-box (Create without Read), or per-file ACLs (ACLs are per folder).
- Admin UI to read other users' notes or highlights.
- Moving the derivative caches (`ThumbnailCache`, `HoverPreviewCache`, `SubtitleCache`, `AudioTrackCache`, `FolderThumbnailProcessor`) into SQLite.
- Moving `ActiveClientTracker`, `PersistentPlayerState`, `MediaPlayerState`, or the two `localStorage` keys into SQLite.
- Cross-device sync of theme/sort preferences.
- Multi-instance / clustered deployment, SQLite replication, or a non-SQLite provider.
- Changes to the FFmpeg pipelines beyond authorization and naming-counter integration.
- Recovery of an Admin without host access (by design).

## Open Questions

- ⚠️ TODO: The P0 spike decides the antiforgery token-delivery mechanism (ERD candidates A/B/C); candidate C needs explicit user sign-off as a trade-off.
- ⚠️ TODO: Whether `stat` P/Invoke for `st_dev:st_ino` is acceptable under repo constraints and works on the NAS bind mount; otherwise FR52 fallback applies.
- ⚠️ TODO: Fingerprint chunk offsets/sizes and cost on large NAS files (FR30, FR32).
- ⚠️ TODO: EPUB `dc:identifier` reliability on the household library, which sets Review-queue volume (FR31).
- ⚠️ TODO: Whether pass-through stubs (which reveal folder names) are acceptable or those folders should be fully hidden (FR23).
- ⚠️ TODO: Whether the Docker-only rule needs an explicit exception line for the offline CLI targets; this spec authorizes `make admin-create`/`make admin-recover` running in the existing image.
- ⚠️ TODO: Session length and sliding-expiration values for the cookie on the LAN.
- ⚠️ TODO: Whether folder moves across volumes ship in P5 (per-file under a parent operation) or are refused until a later spec (FR60).
- ⚠️ TODO: Whether Data Protection keys need encryption at rest beyond volume permissions (proposed: operator-only volume, documented; revisit if the NAS is shared).
- ⚠️ TODO: Trash retention and journal/audit pruning windows (proposed defaults: 30 days trash, 90 days terminal journal rows, 90 days Missing flagging).
