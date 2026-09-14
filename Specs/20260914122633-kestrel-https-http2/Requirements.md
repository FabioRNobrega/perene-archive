# Requirements: Kestrel HTTPS + HTTP/2 for LAN Request Streaming

## Table of Contents

- [Problem Statement](#problem-statement)
- [User Stories](#user-stories)
- [Functional Requirements](#functional-requirements)
- [Non-Functional Requirements](#non-functional-requirements)
- [Out of Scope](#out-of-scope)
- [Open Questions](#open-questions)

## Problem Statement

`Specs/20260914112308-resumable-archive-uploads/` implemented resumable chunked archive uploads and calls `HttpRequestMessage.SetBrowserRequestStreamingEnabled(true)` on every chunk (`ArchiveBrowser.razor`). Chromium's browser request streaming requires HTTPS + HTTP/2; the Docker Compose dev/LAN deployment only exposes Kestrel over plain HTTP on port 8080 (`Dockerfile` `ASPNETCORE_URLS=http://+:8080`, `docker-compose.yml` `WEBAPP_PORT:-8080`). When a chunk request is attempted over this HTTP/1.1 transport, Chromium fails the request outright (`net::ERR_ALPN_NEGOTIATION_FAILED`) instead of degrading gracefully, so `ArchiveBrowser.razor` had to add a buffered, non-streaming fallback that activates on the very first chunk of every page load. That fallback works, but it means uploads never actually stream — the client always buffers each chunk in WASM memory before sending, and the intended memory/performance benefit of request streaming (documented as the reason for choosing it in `Specs/20260914112308-resumable-archive-uploads/Plan.md`) is never realized in this deployment.

This spec makes Kestrel serve an opt-in HTTPS endpoint with HTTP/2 enabled, using a self-signed certificate whose Subject Alternative Name (SAN) covers the operator's actual LAN address, so that once the operator trusts that certificate on their devices, `SetBrowserRequestStreamingEnabled(true)` succeeds instead of falling back.

## User Stories

- Given an operator has generated and trusted the LAN HTTPS certificate, when they open the app at `https://<lan-host>:8443`, then the site behaves exactly as it does today (navigation, playback, API calls, small uploads) with no functional regression.
- Given HTTPS is configured and trusted, when a Chromium browser inspects a chunk-upload network request in DevTools, then it reports protocol `h2` and the request is not the buffered fallback.
- Given an operator has not generated a certificate, when they run the app exactly as before, then Kestrel behaves exactly as it does today (HTTP-only on 8080, no new failure modes, no new required configuration).
- Given HTTPS is configured, when any client requests a plain-HTTP URL, then the request is redirected to the HTTPS endpoint so the deployment is effectively HTTPS-only for every route once opted in, not just the upload endpoints (per explicit operator decision — see Non-Functional Requirements).
- Given a client device has not yet trusted the self-signed certificate, when it is redirected to HTTPS, then it sees the browser's normal self-signed-certificate warning (an accepted, documented consequence of the all-traffic redirect decision) rather than a broken or silently-failing connection.

## Functional Requirements

1. FR1 — A new `make https-cert` Makefile target generates a self-signed X.509 certificate and private key via OpenSSL inside the existing SDK-image Docker workflow (no new tooling image), whose SAN list includes the LAN hostname/IP(s) supplied via a new `.env` variable (`HTTPS_SAN_HOSTS`, comma/semicolon-separated, mirroring the existing `ALLOWED_NETWORK_HOSTS` convention), plus `localhost`/`127.0.0.1`. It bundles the result into a PFX protected by a password read from a new `.env` variable (`HTTPS_CERT_PASSWORD`) and writes both source material and the PFX into a gitignored `./https/` directory at the repo root.
2. FR2 — `.gitignore` and `.dockerignore` are updated so nothing under `./https/` (cert, key, PFX) is ever committed or sent to the build context beyond what the compose bind mount needs; `.env.example` documents `HTTPS_SAN_HOSTS`, `HTTPS_CERT_PASSWORD`, and the new HTTPS port variable with placeholder/instructional values only, consistent with the existing rule that only `.env.example` may be committed.
3. FR3 — `docker-compose.yml` bind-mounts `./https` read-only into the `webapp` container and publishes a configurable HTTPS port (`HTTPS_WEBAPP_PORT`, default `8443`) alongside the existing HTTP port; the HTTP port and its default (`WEBAPP_PORT`, default `8080`) are unchanged.
4. FR4 — `WebApp/WebApp/Program.cs`/Kestrel configuration binds an HTTPS endpoint only when the configured PFX file actually exists on disk at startup; when it does not exist, Kestrel's behavior is unchanged from today (HTTP-only on the existing port, no new validation failures, no new required environment variables). This existing-behavior-by-default path must be covered by a test.
5. FR5 — When the HTTPS endpoint is enabled, it listens with `HttpProtocols.Http1AndHttp2` (per Microsoft Learn's documented Kestrel endpoint configuration, cited in `Plan.md`) and the certificate/password are sourced from configuration (`.env`-driven container environment variables), never hardcoded in `Program.cs` or committed config files.
6. FR6 — When the HTTPS endpoint is enabled, `app.UseHttpsRedirection()` (already present in `Program.cs` as a no-op today) becomes effective for every route — not only the archive upload endpoints — so the deployment is HTTPS-only in practice once opted in, per the explicit operator decision recorded in Non-Functional Requirements. When the HTTPS endpoint is not enabled, redirection remains a no-op exactly as today.
7. FR7 — The existing host-header allowlist middleware in `Program.cs` continues to gate every request (HTTP and HTTPS) by `Host.Host`; enabling HTTPS does not bypass, weaken, or duplicate this check.
8. FR8 — `README.md` gains a short "HTTPS / LAN request streaming (optional)" section under its existing setup/feature documentation describing: generating the cert (`make https-cert`), the required `.env` variables, the HTTPS URL/port, and the manual client-trust step (importing the generated certificate into each client device's OS or browser trust store) as an explicit operator action this app cannot automate.
9. FR9 — `Makefile`'s `get-url`/`get-url-nas` targets (or new equivalents) print the HTTPS URL in addition to the HTTP URL when a certificate is configured, so operators have a single place to find both.
10. FR10 — No code change is required in `ArchiveBrowser.razor`'s existing chunk-upload streaming/fallback logic (added in `Specs/20260914112308-resumable-archive-uploads/`) for this spec to take effect: once HTTP/2 is negotiable, `SetBrowserRequestStreamingEnabled(true)` succeeds on the first chunk and the existing `_requestStreamingSupported` flag simply never flips to `false`. This spec's Validation must confirm that claim rather than assume it.

## Non-Functional Requirements

- **All-traffic HTTPS redirect is an explicit operator decision, not an accident**: once a certificate is configured, every route redirects to HTTPS, including for client devices that have not yet trusted the certificate (they will see the browser's standard self-signed-certificate warning). This intentionally departs from the alternative of leaving HTTP as a permanent, silent fallback for untrusted devices — the operator chose a consistently HTTPS deployment over a partially-HTTP one.
- **Zero impact when not opted in**: an operator who never runs `make https-cert` and never sets the new `.env` variables must see no change whatsoever in existing behavior, ports, logs, or startup validation.
- **No secrets committed**: certificate material and its password must never enter Git history; this must hold even under a broad `git add`, i.e. the ignore rules must be correct before this feature is used once, not merely documented.
- **Preserve the private LAN posture**: this feature must not open the app to the public internet, weaken the host-header allowlist, or introduce authentication. It only adds a second, optionally-enabled local transport for the same private-network-only app.
- **Testable without a browser**: the opt-in/opt-out branching in Kestrel configuration (cert present vs. absent) must be covered by an automated test using `WebApplicationFactory` or equivalent, matching this repo's existing test conventions; Chromium/HTTP-2/ALPN negotiation itself is inherently manual and is covered in `Validation.md`'s manual steps instead.
- **Certificate lifetime**: the generated self-signed certificate should have a validity period long enough to avoid frequent manual regeneration in a private LAN context (this spec proposes 825 days, the maximum commonly accepted by Chromium for leaf certificates); document the regeneration command (`make https-cert`) as the renewal path.

## Out of Scope

- Automatic certificate trust distribution to client devices (importing into a phone's or laptop's OS/browser trust store remains a manual, documented operator step; there is no MDM or push-trust mechanism here).
- mDNS/Avahi-based stable hostnames (e.g. `perene.local`) — the SAN is populated from operator-supplied LAN IP(s)/hostname(s) in `.env`, not from an auto-discovered name.
- A publicly trusted certificate authority (e.g. Let's Encrypt) — this is a private LAN app with no public DNS name; only a self-signed certificate is in scope.
- Any change to `ArchiveBrowser.razor`'s streaming/fallback code itself (see FR10) — this spec only changes the transport it runs over.
- HTTP/3 or QUIC.
- Removing or renaming the existing HTTP port/behavior.

## Open Questions

- ⚠️ TODO: Confirm the operator's actual LAN addressing scheme (static IP vs. DHCP-assigned) before relying on a single SAN entry surviving router restarts; if the LAN IP changes, the certificate must be regenerated via `make https-cert` with the new value in `.env`. This is a documentation/operator-runbook concern, not a code change, and is called out in the README section from FR8.
