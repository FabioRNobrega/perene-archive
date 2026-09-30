# Requirements: SQLite Authentication Foundation

## Problem Statement

Perene Archive currently has no account boundary: every trusted-LAN visitor can use every API and all application state is shared. This first phase establishes the durable SQLite, Identity, cookie, antiforgery, backup, and operator-command foundation required before folder authorization activates ordinary users.

## User Stories

- Given a fresh deployment, when the operator creates the first Admin with `make admin-create`, then no default credential exists and the one-time password is displayed only in that terminal.
- Given an Admin account, when the Admin signs in, changes its temporary password, or enrolls optional TOTP, then account state is stored durably and cookies survive a container rebuild that preserves app data.
- Given any unauthenticated request, when it targets an application page or API, then access is denied except for the explicitly anonymous sign-in flow and required static assets.
- Given a password reset, deactivation, role change, or expired temporary password, when the user next signs in or makes an authenticated request, then the security rules are consistently enforced.

## Functional Requirements

1. FR1 — Configure one SQLite database at validated, absolute `Database:Path` (default `/appdata/perene.db`) on a dedicated `appdata` Docker volume. Startup rejects missing, unwritable, relative, or archive/cache/output-overlapping paths.
2. FR2 — Add EF Core migrations and an `AppDbContext`; migrations run only at web-host startup, foreign keys are enabled on every connection, and SQLite uses WAL/busy-timeout connection behavior.
3. FR3 — Persist `ApplicationUser` with username-only sign-in, display name, creator/time, active state, forced-password-change state, and temporary-password expiry. Email, self-registration, external login, forgotten-password, and personal-data flows are absent.
4. FR4 — Register the sole `Admin` role and prevent deletion, deactivation, or demotion of the last active Admin.
5. FR5 — Provide static-SSR Bootstrap account pages for login, logout, forced password change, optional TOTP sign-in/recovery-code sign-in, and authenticator enrollment/management. Enrollment renders a server-generated inline SVG QR code and generates ten recovery codes.
6. FR6 — Apply a fallback authenticated-user policy to pages and APIs. Only the account sign-in flow, required static assets, and the health-free error page are anonymous.
7. FR7 — Implement `ApplicationSignInManager` checks for inactive users and expired temporary passwords on password, TOTP, and recovery-code sign-in paths. Before password verification, failures use one generic message; expiry is disclosed only after the temporary password proves valid.
8. FR8 — While `MustChangePassword` is set, permit only change-password and logout. A successful change atomically clears the forced-change state and refreshes the session.
9. FR9 — Provide Admin lifecycle operations for creating Admin users, password reset, TOTP reset, deactivate/reactivate, and delete after owner reassignment. Passwords, token values, TOTP keys, and recovery codes are never logged or stored in audit data.
10. FR10 — Lower cookie security-stamp validation to one minute and persist an `AuthzVersion` value that lifecycle changes update atomically.
11. FR11 — Provide `make admin-create USER=<name>` and `make admin-recover USER=<name>` CLI modes that never start the host or migrate the database. They refuse a schema mismatch, print a temporary password only once, and record recovery without a secret.
12. FR12 — Protect unsafe API methods with the authenticated no-store `GET /api/antiforgery` request-token endpoint and a WebAssembly delegating handler. Static SSR account/admin forms use framework antiforgery independently. Buffered requests retry once after token rejection; multipart and streamed bodies do not retry.
13. FR13 — Configure authentication cookies as HttpOnly with SameSite Lax or Strict and Secure policy `SameAsRequest`, without changing the existing host-header allowlist or HTTPS behavior.
14. FR14 — Add client authentication-state serialization/deserialization, `AuthorizeRouteView`, an authenticated shell user menu, and Admin-only navigation. UI visibility is never authorization.
15. FR15 — Persist Data Protection keys at `/appdata/keys` with application name `PereneArchive`; backups include that key directory.
16. FR16 — `make db-backup` creates and verifies an online SQLite backup (integrity check and migration version) plus Data Protection keys. Live database-file copies while writes are accepted are not documented as a backup method.
17. FR17 — An expired temporary password is recoverable only through an Admin reset or `make admin-recover`; the Admin list shows an expiry badge without exposing secrets.
18. FR18 — Until P2 registers and seeds folder access enforcement, non-Admin accounts cannot be created, activated, or signed in. No setting bypasses this invariant.
19. FR19 — Authentication cannot be disabled by configuration, environment variable, or automatic rollback. Documentation distinguishes normal verified-backup deployment rollback from an explicit emergency deployment of an older unauthenticated build.
20. FR20 — `admin-create`, `admin-recover`, and `db-backup` may run beside the host but never migrate; `db-unlock-migration` is a separate confirmed command that refuses while migration activity is detected.

## Non-Functional Requirements

- Keep browser-facing IDs opaque and do not expose filesystem paths, secrets, or key material in responses or normal logs.
- Use Bootstrap-first static and interactive UI, the existing dark/light design tokens, accessible feedback, and `FeatureErrorBoundary` isolation for client API work.
- All package installation, migrations, builds, and tests run through Docker Make targets.
- Use current Microsoft Learn guidance for Blazor Web App Identity, EF Core SQLite, antiforgery, and Data Protection.

## Out of Scope

- Ordinary-user folder access, permissions, or activation (P2).
- Migration of notes, progress, favorites, jobs, naming counters, or filesystem operations (P3–P5).
- Email, self-registration, external authentication, JWTs, and Internet hosting.

## Open Questions

- None. The umbrella spec fixes the P1 implementation decisions.
