# Validation: Kestrel HTTPS + HTTP/2 for LAN Request Streaming

## Table of Contents

- [Acceptance Criteria](#acceptance-criteria)
- [Test Cases](#test-cases)
- [Manual Verification](#manual-verification)
- [Definition of Done](#definition-of-done)
- [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | `make https-cert` produces `./https/perene.key`, `./https/perene.crt`, and `./https/perene.pfx`, whose SAN list (verifiable via `openssl x509 -in perene.crt -noout -text`) contains every host/IP from `HTTPS_SAN_HOSTS` plus `localhost`/`127.0.0.1`. |
| FR2 | `git status`/`git check-ignore -v https/perene.pfx` confirms the directory is ignored; `.env.example` contains only placeholder values for the three new variables; no real IP or password appears in any committed file. |
| FR3 | `docker-compose.yml` exposes both `${WEBAPP_PORT:-8080}` and `${HTTPS_WEBAPP_PORT:-8443}`; the `./https` bind mount is present and read-only. |
| FR4 | With no `HttpsCertificate:Path` configured (today's default), the app starts and serves exactly as before — covered by an automated startup test with the option unset. With a valid generated cert configured, the app still starts successfully — covered by an automated startup test with the option set to a real temp PFX. |
| FR5 | The real-socket integration test (below) proves a client that requests `HttpVersion.Version20` against the configured HTTPS endpoint receives an HTTP/2 response, and that the endpoint's negotiated protocol is not merely HTTP/1.1-over-TLS. |
| FR6 | With a cert configured, an HTTP request to the app is redirected (3xx) to the HTTPS URL for a route outside `/api/archive/uploads` (proving the redirect is not scoped only to uploads); with no cert configured, an HTTP request to the same route returns its normal 2xx/whatever status with no redirect. |
| FR7 | An HTTPS request with a disallowed `Host` header is still rejected by the existing allowlist middleware (400), proving the check runs for both transports. |
| FR8 | `README.md` contains the new subsection with the `.env` variables, the `make https-cert` command, and the manual client-trust instruction. |
| FR9 | `make get-url`/`make get-url-nas` output includes the HTTPS URL when a cert is configured and omits it (unchanged output) when not. |
| FR10 | The manual Chromium DevTools check (below) shows the chunk-upload PUT requests reporting protocol `h2`, and `Program.cs`/`ArchiveBrowser.razor` diffs for this spec contain no changes to the upload streaming/fallback code paths themselves. |

## Test Cases

**Unit tests (xUnit, matching this repo's `WebApp.Tests/Configuration/*OptionsTests.cs` pattern):**

- `WebApp.Tests/Configuration/KestrelHttpsOptionsTests.cs`:
  - `IsEnabled` is `false` when `CertificatePath` is empty/whitespace/null (default state — proves zero-impact-by-default).
  - `IsEnabled` is `false` when `CertificatePath` is set but the file does not exist on disk.
  - `IsEnabled` is `true` when `CertificatePath` points at a real (temp) file.
  - `HasPositivePort` rejects zero/negative ports; `HasPasswordWhenPathConfigured` rejects a configured path with an empty password but allows an unconfigured path with an empty password (mirrors `ArchiveRootOptions`'s "not configured yet is valid" pattern).

**Integration tests (matching this repo's `WebApp.Tests/Endpoints/*Tests.cs` `WebApplicationFactory<Program>` pattern, plus one real-socket test):**

- `WebApp.Tests/Services/KestrelHttpsEndpointConfiguratorTests.cs`:
  - `TryConfigure` returns `false` and does not throw when given a `KestrelServerOptions` and a `KestrelHttpsOptions` with no configured path.
  - `TryConfigure` returns `true` and does not throw when given a real temp self-signed PFX (generated in-test via `CertificateRequest`/`X509Certificate2`, not via the shell script, so the test has no OpenSSL/Docker dependency).
  - **Real-socket HTTP/2 test**: boot a minimal real `WebApplication` (its own tiny `Program`-less app, not the full `WebApp` host) with `KestrelHttpsEndpointConfigurator.TryConfigure` wired to an ephemeral port and the in-test-generated PFX; start it with `await app.StartAsync()`; issue an `HttpClient` request with `HttpRequestMessage.Version = HttpVersion.Version20` and `VersionPolicy = HttpVersionPolicy.RequestVersionExact`, a `SocketsHttpHandler` with `SslOptions.RemoteCertificateValidationCallback` bypassing validation (self-signed, test-only); assert the response succeeds and `response.Version == HttpVersion.Version20`. This is the automated substitute for "Chromium negotiated HTTP/2," since `WebApplicationFactory`'s default `TestServer` never binds a real socket or performs a TLS/ALPN handshake.
  - Repeat with `HttpVersion.Version11` against the same endpoint to prove HTTP/1.1 clients (untrusted-cert LAN devices, or any client that doesn't request h2) are still served correctly on the same port, satisfying the `Http1AndHttp2` requirement.
- `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs` (or a new small test file) extended with:
  - A `VideoManagerFactory`-style factory with no `HttpsCertificate:Path` set: existing archive listing/upload endpoint tests continue to pass unmodified (proves zero regression).
  - A factory variant with `HttpsCertificate:Path` set to a real temp PFX: application still starts (`factory.CreateClient()` succeeds) and existing endpoints still respond correctly over the in-memory `TestServer` (proves the new Options/ConfigureKestrel registration doesn't break request handling even though `TestServer` doesn't exercise the real listener).
  - A request to a non-upload route (e.g. `/api/archive/photos/items`) with the cert configured returns a redirect status when made without following redirects, and returns its normal status when the cert is not configured — covering FR6 automatically. *(⚠️ TODO during implementation: confirm `WebApplicationFactory`'s `TestServer`/`HttpClientHandler` surfaces `UseHttpsRedirection`'s redirect the same way a real socket would; if `TestServer` does not apply scheme/port redirection meaningfully in-memory, this criterion moves to the manual steps below instead and this note is removed.)*

## Manual Verification

Perform these in order; do not skip ahead if an earlier step fails.

1. Start clean with `make docker-down` then `make docker-run` with no `.env` HTTPS variables set. Confirm the site works exactly as before at `http://<lan-ip>:8080` (navigation, playback, a small archive upload). This is the pre-existing baseline.
2. Set `HTTPS_SAN_HOSTS` (your NAS's real LAN IP and/or hostname) and `HTTPS_CERT_PASSWORD` in your local `.env`. Run `make https-cert`. Confirm `./https/perene.pfx` (or configured name) now exists.
3. Restart with `make docker-run`. Confirm the app still starts and `http://<lan-ip>:8080` now redirects to `https://<lan-ip>:8443`.
4. In Chromium, open `https://<lan-ip>:8443`; accept/inspect the self-signed certificate warning; import the certificate (`./https/perene.crt`) into the OS or Chromium's certificate manager so it's trusted.
5. Reload `https://<lan-ip>:8443` with the certificate trusted. Confirm no warning, and confirm normal site usage works end-to-end: navigation, video/audio playback, folder browsing, a small (non-chunked-noticeable) upload.
6. Open DevTools → Network, filter to the request for the page document itself, and confirm the "Protocol" column (enable it via the column picker if hidden) shows `h2`.
7. Start an archive upload large enough to span multiple chunks (e.g. 100 MB+). In DevTools Network, confirm each `PUT .../chunk` request shows protocol `h2`, and confirm no console warning about "Browser request streaming failed... falling back to buffered chunk uploads" appears (that log line, added in `Specs/20260914112308-resumable-archive-uploads/`, firing here would mean this spec did not achieve its goal).
8. From a second device on the LAN that has *not* imported the certificate, browse to `http://<lan-ip>:8080`; confirm it redirects to `https://<lan-ip>:8443` and the browser shows the expected self-signed-certificate warning (the accepted, documented consequence of the all-traffic-redirect decision), rather than a broken connection or silent failure.
9. Stop the app, unset the three `.env` HTTPS variables (or remove `.env` HTTPS lines) without deleting `./https/`, and restart. Confirm the app returns to serving only `http://<lan-ip>:8080` with no redirect and no startup error — proving the feature is fully reversible by configuration alone, independent of whether the generated cert files still exist on disk.
10. Run `make test`; confirm all tests pass (the one pre-existing unrelated EPUB test failure noted in `Specs/20260914112308-resumable-archive-uploads/`, if still present at implementation time, is not a regression introduced by this spec — confirm no *new* failures).

## Definition of Done

- Requirements, Plan, and Validation documents in this folder are implemented.
- `KestrelHttpsOptions` and `KestrelHttpsEndpointConfigurator` exist, are registered in `Program.cs`, and are covered by the unit/integration tests above.
- `make https-cert`, the updated `Dockerfile`/`docker-compose.yml`/`.env.example`/`.gitignore`/`Makefile`, and the `README.md` subsection all exist and match `Plan.md`.
- No secret/certificate/password is present in any committed file (`git status`/`git log -p` spot check on the introducing commit).
- `make test` passes in the Docker Compose test stack with no new failures relative to the pre-existing baseline.
- `ArchiveBrowser.razor` and its upload streaming/fallback logic have zero diffs attributable to this spec (FR10) — any diff there is a signal the design leaked outside its intended seam and must be reconciled with `Specs/20260914112308-resumable-archive-uploads/` instead of silently accepted.
- Manual Verification steps 1–9 have been performed at least once by a human against a real LAN device; step 6/7's `h2` confirmation is the feature's actual success signal and cannot be automated away.
- The Microsoft Learn evidence cited in `Plan.md` remains applicable, including the documented TLS 1.2+/ALPN requirements for `Http1AndHttp2`.

## Rollback Plan

- Fully configuration-reversible: unset `HTTPS_SAN_HOSTS`/`HTTPS_CERT_PASSWORD`/`HTTPS_WEBAPP_PORT` (or delete/rename `./https/perene.pfx`) and restart — `KestrelHttpsOptions.IsEnabled` returns `false`, `KestrelHttpsEndpointConfigurator.TryConfigure` adds no listener, and `UseHttpsRedirection` reverts to its pre-existing no-op behavior. No code revert is required for a deployment rollback.
- To remove the feature from the codebase entirely: revert the `Program.cs`/`Dockerfile`/`docker-compose.yml`/`Makefile`/`.env.example`/`.gitignore`/`README.md` changes and delete the two new `Configuration`/`Services` files and their tests, in one compatible change set, exactly as `Specs/20260914112308-resumable-archive-uploads/Validation.md` models for its own rollback.
- `ArchiveBrowser.razor`'s buffered-fallback path (already shipped) remains the safety net throughout — even a partial or failed rollout of this spec cannot break uploads, only leave them running in the slower buffered mode that was already the working baseline.
