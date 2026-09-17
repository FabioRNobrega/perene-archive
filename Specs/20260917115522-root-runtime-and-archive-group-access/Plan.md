# Plan: Root Runtime and Opt-in Archive Editor Access

## Summary

Keep the existing .NET/Blazor application root runtime and direct, read-only Docker socket transport. Add a host-only `perenearchive` group and explicit operator command to grant a requested existing administrator shared group access. The existing `DashboardEndpoints` → `IDockerMetricsService` → `IDockerApiClient` pattern remains intact.

## Technical Approach

### Root runtime and archive editor setup

`Dockerfile` installs packages and remains root; it does not create a `perenearchive` user. Compose does not set a non-root `user:` override. Root therefore retains normal access to Docker's read-only socket bind and the optional root-owned HTTPS PFX bind.

Create `scripts/setup-archive-group.sh`, called by `make archive-group`, to safely create or verify the `perenearchive` host group using the host's available group allocation. Create `scripts/share-archive.sh`, called by `make archive-share USER=<account> PERENE_ARCHIVE_ROOT=<path>`, to validate the existing target account and archive root, add it as a supplementary group member, recursively set the archive group to `perenearchive`, and apply `2770` plus writable access/default POSIX ACL entries to shared directories. This means `Directory.CreateDirectory` calls from the UI inherit editor access without application-specific per-folder permission logic. The script excludes root-only `.uploads`, applies `3770` to `Videos/Cuts` and `Videos/VideoComposition`, and reports that a new login is required. Re-running either command is idempotent.

### Direct dashboard transport

Remove the `docker-socket-proxy` service and its socket-GID/proxy-base-URL configuration. Restore the read-only configured Docker/Podman socket bind directly on `webapp`; it has no host port mapping beyond the existing application ports. Restore `DockerEngineApiClient`'s Unix socket configuration. The dashboard remains browser-safe and `DockerMetricsService` continues to convert expected transport/API failures to `DashboardDockerDto(false, [])`.

Docker documents the dashboard's required primitives: the Engine API offers container list and inspect endpoints, and container stats are supplied by `GET /containers/{id}/stats`; the current implementation already maps precisely to those reads.

### Verification and documentation

Restore direct-socket tests around `DockerEngineApiClient`/`DockerMetricsService`. Add focused tests or a manual matrix for archive-group and archive-share validation, idempotence, and mode scope. Update README installation with the optional `make archive-share USER=<account>` workflow and direct root Docker-dashboard behavior.

## Component Breakdown

**Existing files to modify:**

- `Dockerfile` — retain root as the final runtime user and remove any in-image `perenearchive` user setup.
- `docker-compose.yml` — restore the direct read-only socket bind, remove the proxy service/dependency and non-root override, and retain normal HTTPS certificate bind behavior.
- `docker-compose.test.yml` — restore root test runtime and root NuGet cache path.
- `Makefile` — remove `user-setup`/`docker-gid`; add `archive-group` and `archive-share USER=<account>` with required account validation and visible help text.
- `.env.example` — retain only `DASHBOARD_DOCKER_SOCKET`; remove proxy socket-GID documentation.
- `WebApp/WebApp/Services/DockerEngineApiClient.cs` — restore direct Unix-socket client construction.
- `WebApp/WebApp/Program.cs` — remove proxy-options registration and test-only proxy transport wiring.
- `README.md` — explain direct read-only Docker telemetry, root HTTPS runtime, the `perenearchive` host group, and opt-in archive sharing.
- Relevant `WebApp.Tests` — remove proxy option/Compose-policy tests and restore direct-socket client tests.

**New files to create:**

- `scripts/setup-archive-group.sh` — safely creates or verifies the host `perenearchive` group.
- `scripts/share-archive.sh` — validates a requested existing account, adds supplementary group membership, and applies scoped group/mode sharing.
- Focused tests or a documented host manual matrix for group creation and archive sharing behavior.

## Dependencies

- Docker Compose/Podman Compose and its host socket, already required by this repository.
- Linux host administration tools: `sudo`, `getent`, `groupadd`, `usermod`, `chgrp`, `chmod`, `find`, `setfacl` (from the `acl` package), and `stat`.
- Existing `Docker.DotNet` package, which continues to call the Docker Engine API through the Unix socket.

## External / Vendor Documentation Evidence

- [Docker Engine API](https://docs.docker.com/reference/api/engine/) documents the Engine's HTTP API and versioned API behavior used by the existing Docker SDK client.
- [Docker Engine API v1.46](https://docs.docker.com/reference/api/engine/version/v1.46/) documents the container list, inspect, and stats endpoints required by `DockerEngineApiClient`.
- [Docker container stats](https://docs.docker.com/reference/cli/docker/container/stats/) confirms that detailed runtime statistics use the `/containers/{id}/stats` endpoint.
- [Linux permissions documentation](https://www.gnu.org/software/coreutils/manual/html_node/Setting-Permissions.html) documents group permissions and setgid directory behavior used for shared archive folders.

## Flow

```mermaid
sequenceDiagram
    participant Operator
    participant Make as make archive-share
    participant Host as Linux/NAS host
    participant Compose
    participant App as webapp (root)
    participant Docker as Docker/Podman socket

    Operator->>Make: make archive-share USER=admin
    Make->>Host: validate existing admin and archive group
    Make->>Host: add admin to group; apply archive group and modes
    Operator->>Compose: make docker-run
    Compose->>App: start root webapp with read-only socket and HTTPS PFX
    App->>Docker: list / inspect / stats
    Docker-->>App: metrics
```

## Risk Assessment

| Risk | Evidence | Mitigation |
| --- | --- | --- |
| Root application access is broad | Root can read the socket and PFX | Keep the socket bind read-only, do not expose it to the browser, and preserve dashboard unavailable fallback. |
| Archive share targets the wrong account | Host accounts are operator-managed | Require an explicit existing `USER` value and print the selected account before group/mode changes. |
| Group tooling is unavailable on a NAS | Not every NAS exposes standard Linux group tools | Detect missing tools and report it without changing ownership. |
| New files lose editor access | File creation mode may remove group write | Use setgid shared directories and container umask `0002`; document manual verification. |
