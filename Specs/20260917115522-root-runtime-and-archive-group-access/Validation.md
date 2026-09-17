# Validation: Root Runtime and Opt-in Archive Editor Access

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1–FR2 | Image inspection and a running `webapp` show root with no `perenearchive` user; the app directly has the configured socket as a read-only bind. |
| FR3–FR4 | Compose defines no proxy service, proxy URL, socket-GID setting, or proxy health dependency; `/api/dashboard/docker` retains its browser-safe available/unavailable DTO. |
| FR5–FR8 | `make archive-group` safely creates/verifies the group; `make archive-share USER=<existing-account> PERENE_ARCHIVE_ROOT=<path>` grants that account group access, repairs inherited writable POSIX ACL entries, is idempotent, and rejects missing/nonexistent accounts without changing user ownership. |
| FR9 | A root-readable configured PFX starts HTTPS normally; normal root-created certificate files do not require a UID 999 permission adjustment. |
| FR9–FR10 | Standard run/test commands need no Docker-GID step; README documents direct Docker telemetry and opt-in archive sharing. |

## Test Cases

**Unit tests:**

- `DockerEngineApiClient` tests prove the direct Unix socket URI is used.
- `DockerMetricsServiceTests` retain valid conversion and unavailable fallback coverage.
- Remove obsolete proxy option and Compose-proxy policy tests.

**Host/manual tests:**

1. On a disposable archive root, run `make archive-group`, then `make archive-share USER=<existing-admin> PERENE_ARCHIVE_ROOT=<path>` twice; confirm the second run succeeds without changing user ownership.
2. Confirm the requested account can create, edit, and delete content under `2770` shared folders after starting a new login session; confirm a non-shared account cannot.
3. Create a nested folder/file from the UI and confirm it inherits the `perenearchive` group plus writable `group::` and `default:group::` POSIX ACL entries; confirm a non-shared account cannot edit it.
4. Confirm `.uploads` remains `root:root` and is not group-shared; confirm Cuts and VideoComposition use sticky/setgid `3770` mode.
5. Run with a missing account, missing archive root, and name collision; verify each failure is clear and does not alter unrelated groups or user ownership.
6. Run `make docker-run-bg`; confirm `webapp` starts without a proxy and Dashboard Docker data remains available. Temporarily deny the socket and confirm only Docker metrics become unavailable.
7. Set `HTTPS_CERT_PASSWORD`, run `make https-cert`, and confirm the root runtime serves HTTPS/HTTP2.
8. Run `make test` and verify the full suite passes.

## Definition of Done

- Root runtime and direct read-only Docker socket behavior are restored.
- No socket proxy implementation or configuration remains.
- No `perenearchive` user is created in the image or on the host; only the host group exists.
- `make archive-group` and `make archive-share USER=<account> PERENE_ARCHIVE_ROOT=<path>` provide explicit, safe group-based archive editor access and durable UI-folder ACL inheritance.
- README and `.env.example` document the final operator workflow without host-specific values.

## Rollback Plan

Revert the direct-socket/runtime and archive-sharing changes together. Removing the sharing command does not remove existing group membership or archive group modes; an operator may remove a specific account from the group only after confirming that account no longer requires archive access.
