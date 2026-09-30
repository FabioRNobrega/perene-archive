# Validation: SQLite Authentication Foundation

## Acceptance Criteria

- FR1–FR2: invalid database locations fail startup; migrations create a real SQLite database; foreign keys are enforced; no pending model changes remain.
- FR3–FR10: username-only Identity works; the last active Admin cannot be removed; inactive and expired-temporary users are rejected without pre-verification disclosure; forced password change and session invalidation work.
- FR11, FR16, FR20: CLI commands do not start the host or migrate, reject schema mismatch, support verified backup, and require explicit migration-unlock confirmation.
- FR12–FR14: every unsafe API and static account/admin form has antiforgery coverage; buffered JSON retries once only; multipart/streamed bodies do not retry; cookies and fallback authorization behave correctly.
- FR15: persisted Data Protection keys preserve valid cookies and antiforgery tokens across a restart.
- FR17–FR19: Admin reset restores expired-temporary access, ordinary users are blocked before P2, and no authentication-bypass setting exists.

## Automated Tests

- `DatabaseOptionsTests` and `AppDbContextTests`: validation, migrations, FK enforcement, UTC storage, and schema checks on real temporary SQLite files.
- `ApplicationSignInManagerTests`, `AccountLifecycleServiceTests`, and `AdminCliTests`: all account lifecycle paths, expiry disclosure, last-admin protection, and CLI schema refusal.
- `AuthenticationFlowTests`, `AccountFormAntiforgeryTests`, and `AntiforgeryCoverageTests`: static SSR forms, fallback policy, TOTP/recovery, API/request-token coverage, and request retry limits.
- `DataProtectionPersistenceTests`, `DatabaseBackupServiceTests`, and `NoAuthBypassTests`: persistent keys, verified backup, and no runtime auth bypass.
- Run all tests through `make test`.

## Manual Verification

1. Run `make docker-build`, `make docker-run-bg`, then `make admin-create USER=admin`.
2. Sign in, complete forced password change, enroll optional TOTP, and use a recovery code after signing out.
3. Confirm unauthenticated APIs return 401 and page navigation redirects to login.
4. Restart while retaining `appdata` and confirm the session remains valid; verify `make db-backup` output with SQLite integrity checking.
5. Confirm no password, recovery code, key material, or archive path appears in normal logs.

## Definition of Done

- All FR1–FR20 acceptance criteria and automated tests pass through `make test`.
- Docker/Makefile/app-data documentation is updated without real environment values or private LAN details.
- The UI meets the existing Bootstrap design and accessibility contract.
- P2 is not implemented here; its activation marker keeps ordinary accounts inactive and unable to sign in.

## Deferred Work

- P2 folder ACLs, P3 per-user data, P4 durable jobs/counters, and P5 filesystem journaling each receive their own later spec.
