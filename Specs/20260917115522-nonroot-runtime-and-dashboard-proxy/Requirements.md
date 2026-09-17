# Requirements: Root Runtime and Opt-in Archive Editor Access

## Problem Statement

The application needs root runtime access to preserve direct Docker-dashboard telemetry and access to operator-managed HTTPS certificate material. Operators need a supported host group and explicit way to grant their existing administrative accounts shared archive edit access without creating a `perenearchive` user.

## User Stories

- Given an operator starts the Compose stack, when the web application runs, then it remains root and can read its mounted Docker socket and configured HTTPS PFX.
- Given an operator runs `make archive-group`, then it safely creates or verifies a host `perenearchive` group using an available GID.
- Given an operator wants their existing administrative host account to edit archive folders and files, when they run `make archive-share USER=<account>`, then that account joins the `perenearchive` group and receives the same archive-group access without being renamed, deleted, or made a Docker-socket member.
- Given the Dashboard loads, when it requests Docker metrics, then it uses the direct, read-only mounted Docker socket and preserves its existing browser-safe DTO and unavailable state.

## Functional Requirements

1. FR1 — `Dockerfile` keeps the application runtime user as root and does not create a `perenearchive` user identity.
2. FR2 — `docker-compose.yml` mounts the configured Docker/Podman socket read-only directly into `webapp`; `docker-compose.test.yml` uses the normal root SDK cache location for its root workload.
3. FR3 — The default Compose stack has no Docker socket proxy service, proxy URL configuration, socket-GID configuration, or proxy health dependency.
4. FR4 — `DockerEngineApiClient` uses the direct Unix socket URI; the existing dashboard endpoint and browser-safe DTO contract remain unchanged.
5. FR5 — `make archive-group` invokes a host-side script with elevated authority that creates the `perenearchive` group only when its name is unused, or verifies an existing same-named group. It never claims a fixed GID or changes an unrelated group.
6. FR6 — `make archive-share USER=<account>` validates the named existing account and archive root, adds that account as a supplementary `perenearchive` group member, and recursively applies the archive group ownership plus group-write/setgid directory permissions to `PERENE_ARCHIVE_ROOT`, excluding root-only `.uploads`.
7. FR7 — Archive sharing never creates, renames, deletes, changes primary groups for, or grants Docker-socket access to any host account. It changes only the requested account's supplementary membership and archive-tree group ownership/modes.
8. FR8 — The sharing command is idempotent, reports missing host tools or inaccessible archive roots clearly, and leaves unrelated `.env` settings untouched. It tells the operator that the target user must start a new login session for supplementary group membership to apply.
9. FR9 — The optional HTTPS PFX continues to be read by root at `/https/perene.pfx`; no certificate ownership or ACL change is required for standard Docker runtime.
10. FR10 — `make docker-run`, `make docker-run-bg`, tests, and documentation no longer require `make docker-gid` or a socket proxy.
11. FR11 — README installation and supported-feature documentation explain root runtime, direct read-only Docker telemetry, the host `perenearchive` group, and the opt-in `archive-share` workflow.

## Non-Functional Requirements

- The browser must never receive physical host paths, socket paths, host account details, or Docker credentials.
- The Docker socket mount remains read-only and browser clients never receive its path or credentials.
- Failure to access the direct socket leaves the rest of the dashboard operational and renders the existing Docker unavailable state.
- The implementation supports the repository's Docker/Podman socket selection mechanism through `DASHBOARD_DOCKER_SOCKET`.
- Archive group/mode changes are explicit, host-local, and scoped to `PERENE_ARCHIVE_ROOT` plus the requested existing account.
- Shared archive directories use `2770` (`rwxrws---`) so new files inherit the `perenearchive` group; root-only `.uploads` remains `root:root` and is not shared. Sticky collaborative output directories may use `3770` (`rwxrws--T`) when deletion must be restricted to file owners.

## Out of Scope

- Restricting Docker Engine API methods through an intermediary proxy.
- Creating a host-side `perenearchive` user.
- Modifying user ownership of an operator's existing archive.
- Authentication, Internet exposure, Docker lifecycle management, or dashboard actions that modify Docker resources.

## Open Questions

- ⚠️ TODO: Define exact operator remediation guidance for NAS systems that do not expose standard Linux group-management tools; `make archive-group` and `make archive-share` must report rather than guess.
