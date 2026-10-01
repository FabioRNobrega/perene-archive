# PERENE ARCHIVE | Fábio R. Nóbrega

Perene Archive is a private, Docker-based media and document archive for a local or NAS-hosted collection. It provides a Blazor Web App for browsing files, playing media, reading books and documents, and organizing archive folders from trusted devices on your local network.

We use:

- .NET 10 and ASP.NET Core
- Blazor Web App with Interactive WebAssembly
- Docker Compose
- FFmpeg and FFprobe for the supported video-processing workflows
- xUnit for tests

Table of contents
=================

- [Current Supported Features](#current-supported-features)
- [Install](#install)
- [Usage](#usage)
- [Tests](#tests)
- [HTTPS / LAN Request Streaming (optional)](#https--lan-request-streaming-optional)
- [Docker Console](#docker-console)
- [Commercial Licensing and Support](#commercial-licensing-and-support)
- [Troubleshooting](#troubleshooting)
- [Git Guideline](#git-guideline)

## Current Supported Features

| Archive favorites | Mark an individual archive file or folder from its actions menu. Favorites are per signed-in user and appear first in the default sort; explicit sorts keep their ordinary order. |

| Area | Status | Supported capabilities and formats |
| --- | :---: | --- |
| Account access | ✅ | The pre-sign-in and TOTP verification pages are static server-rendered pages that use the PereneArchive dark design system and work without JavaScript. |
| Account security | ✅ | Every signed-in user can edit their display name, change their password, and turn their authenticator app on (confirmed with a current code, with ten one-time recovery codes shown once) or off (confirmed with their password) from the Family page. Password fields have a show/hide eye button (on the sign-in page it appears only when JavaScript is available). Administrators manage members from Family Members: add members or administrators with a temporary password that expires after seven days (an expiry badge shows when it has lapsed), reset a password or authenticator, deactivate/reactivate, change the administrator role, and delete (accounts they created are reassigned to the deleting administrator). The last active administrator can never be deactivated, demoted or deleted. A user menu in the sidebar shows who is signed in and offers sign-out; the member-management shortcut is administrator-only. |
| Sign-in and sessions | ✅ | Every page and API requires a signed-in user; only the sign-in flow, static assets and the error page are anonymous (unauthenticated API calls get `401`). A fresh install seeds an `admin` administrator (initial password `admin`) that must choose a new password at first sign-in; restarts never touch an existing account. Sign-in uses a username and password, optionally followed by an authenticator or recovery code, and repeated wrong passwords lock the account briefly. Cookies are HttpOnly/SameSite=Lax and re-validated every minute; every unsafe API call carries an antiforgery token added automatically by the browser client. Accounts live in a SQLite database on the `appdata` volume with the Data Protection keys. |
| Account recovery and backup | ✅ | `make admin-recover USER=<name>` prints a one-time temporary password for an account (offline, never migrates). `make db-backup` writes an integrity-checked online SQLite backup plus the Data Protection keys to `./backups` (or `PERENE_BACKUP_DIR`). `make db-unlock-migration CONFIRM=yes` clears a stale migration lock and refuses while database activity is detected. Authentication cannot be turned off by configuration; to roll back, restore a verified backup, and only deliberately deploy an older, unauthenticated build in an emergency. |
| Folder access control | ✅ | Members start with the Shared defaults: Read everywhere, no Write, Create or Delete. In Family → Family Members, an administrator opens a member's folder-access editor (house-lock button, same tab) to Allow or Deny Read/Write/Create/Delete per folder, see why each cell is effective (inherited, owner, Shared default, Private), make a folder Private, and review which content and users would lose visibility before saving. Every archive, video, cut, composition, thumbnail, preview, subtitle, download and range-stream route is checked on the server: unreadable content answers `404` and appears nowhere in listings, playlists, ZIP downloads or counts; readable-but-forbidden actions answer `403`. Whoever creates a folder owns it (never with permission management); queued cut, composition, conversion, move/trash and audio-track jobs are re-checked when they run. Trash starts Private, so only administrators (or members granted access) see it. Saves are optimistic-concurrency checked and audited without paths. |
| Video library | ✅ | Browse and play `.mp4`, `.webm`, `.mov`, and `.m4v` videos. |
| Video conversion | ✅ | Plan each supported source (including `.vob`) before queueing a non-destructive, collision-safe sibling MP4: choose a compatible/compression profile, non-upscaling resolution (original, 1080p, 720p, or 480p; 720p stays the recommended default for larger sources), quality or target size, and review a server-calculated estimated output size. Current-session jobs can pause/resume or stop safely; burn one embedded subtitle track or detected closed captions permanently into the video rather than preserve switchable captions. |
| Video editing | ✅ | Reframe vertical-video previews, set A/B points, and export stream-copied cuts. |
| Video compositions | ✅ | Combine two or more cuts into a composed MP4 with fades. |
| Video previews | ✅ | Static JPEG thumbnails and hover-preview MP4s are generated server-side. |
| Audio tracks | ✅ | The Video Library and Archive Browser player can switch between multiple embedded audio tracks (e.g. original-language and dubbed) from a translate-icon menu. The server stream-copies the chosen track into a cached MP4 on first use (no re-encoding), then the player swaps sources at the same position. |
| VR POV | ✅ | A manual toggle in the video player projects the left half of any side-by-side VR180 frame onto a 180° curved WebGL view that can be looked around by dragging (yaw/pitch with mouse, touch or pen; mouse-wheel or pinch to zoom; double-click recenters); available for every video in the Video Library and Archive Browser. |
| Subtitles | ✅ | Same-named `.srt` subtitle files are converted to WebVTT for video playback. |
| Audio | ✅ | Browse and play `.mp3`, `.m4a`, and `.wav` files, including album artwork from `.png`, `.jpg`, or `.jpeg`. |
| Playlists | ✅ | Play every video/audio file in a folder (including subfolders) as a queue, with a YouTube-style player-and-queue view, auto-advance, Fill-tab expand, and resume of the same track and position across navigation. While an active playlist is open, transferable player preferences (VR POV, saturation, volume/mute, speed, subtitles, and Fill-tab) carry between applicable entries; loops, A/B points, crop, audio-track choice, and camera view stay per item. |
| Images | ✅ | Browse and view `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`, `.avif`, `.bmp`, and `.ico` raster images in the archive carousel; SVG remains unsupported. |
| Comics | ✅ | Browse and read `.cbz` comics as read-only, single-page viewers with bounded ZIP safety limits and automatic reading-position restore. |
| Books | ✅ | Read `.epub` books with progress, highlights, notes, shared reader themes (everyone can use any saved theme; only its creator can edit or delete it; each user keeps their own active settings), and an in-reader mini player for controlling active media. Notes, highlights, reading and comic progress, favorites, and custom storage views are stored per user in the database and follow a book across renames and moves; an Admin can run a reconcile scan (`POST /api/admin/media/reconcile`) after files are moved outside the app. |
| Text documents | ✅ | View and edit `.md`, `.markdown`, and `.txt` documents. |
| PDF documents | ✅ | Browse and open `.pdf` documents. |
| Archive management | ✅ | Browse archive categories; create folders and `.txt`/`.md` text files; upload video/music/image/book/text/PDF/subtitle files via resumable, sequential chunked sessions with acknowledged progress, rate/ETA, Resume/Cancel recovery after an interruption or restart, and a bounded batch progress preview with aggregate status and completed-only bulk dismissal; download individual files and folders as ZIPs; rename, move (via a Finder-style column picker that can browse any move-eligible category and folder depth, including moving items across categories), and send supported files and folders to Trash; permanently empty Trash with a confirmation prompt. A click or tap selects/deselects a card, Ctrl+Click/Shift+Click builds a multi-selection, and double-click/double-tap opens the card; selection is visibly marked with a green border and badge. The card actions button and right-click open that card's in-app menu without changing selection. Dragging a card or the whole selection onto a folder card or an ancestor breadcrumb moves it there immediately through the same batch job. The selection toolbar also has a confirmed "Move to Trash" action that sends the whole selection to Trash in one batch job (hidden inside Trash). Move, send-to-Trash, Empty Trash, and batch Move/Trash run as background jobs with a Bootstrap progress popup showing a live processed/total file count, so large folders and a full Trash no longer appear to freeze the page. |
| Archive sort | ✅ | A sort dropdown beside the Archive Browser name filter orders the current folder by Name (A-Z), Name (Z-A), Size (largest first), or Date (newest first), always keeping folders before files. It composes with the filter, works in every category including Trash, runs client-side, and is remembered in browser storage. |
| Folder thumbnails | ✅ | Set a custom cover image for any Archive Browser folder from its options panel; the image is cropped to fill 640×360, stored as a reserved file physically inside the folder (so it travels with rename/move/Trash automatically), and can be removed to revert to the default folder icon. |
| Appearance | ✅ | Dark and Kindle-paper light themes, responsive layout, and Fill-tab video mode. |
| Fault isolation | ✅ | Page, archive/viewer, dashboard-card, and footer-player failures are isolated with accessible Bootstrap recovery alerts, so an unavailable feature does not interrupt unrelated navigation or active playback. |
| System dashboard | ✅ | The home page (`/`) shows System, Memory, Storage, Network, PereneArchive, Docker, Health, History, and Alerts cards with a manual Refresh control, backed by dedicated `/api/dashboard/*` endpoints. Docker telemetry uses a direct, read-only Engine socket mount. The Storage card's "+" button opens a Finder-style column picker to track any archive folder (or the whole archive) against a user-chosen max size, with editable/removable rows persisted server-side. |
| HTTPS / LAN request streaming | ✅ | Optional self-signed HTTPS + HTTP/2 Kestrel endpoint (`make https-cert`) so chunked archive uploads can stream instead of buffering; HTTP-only by default. |

## Install

1. Clone the repository and enter the project directory.

2. Ensure Docker Compose or Podman Compose is available.

3. Create the host archive layout. By default, the app uses `/home/PereneArchive`; alternatively, copy `.env.example` to `.env` and set `PERENE_ARCHIVE_ROOT` to an absolute path on the Docker host. The Dashboard Storage card measures this archive filesystem, so on a NAS set it to a directory actually mounted on the volume you want to monitor (for example, the filesystem reported by `df -h "$PERENE_ARCHIVE_ROOT"`).

   The archive root contains folders such as `Videos`, `Pictures`, `Music`, `Documents`, and `Books`. The video workflow also uses `Videos/Cuts` and `Videos/VideoComposition`.

4. Optional: grant an existing host administrator shared archive editing access. The webapp runs as root so it can read the direct, read-only Docker telemetry socket and an optional root-owned HTTPS PFX; this does not create a `perenearchive` user. To share archive folders with an existing account, create the host group and explicitly add that account:

```bash
make archive-group
make archive-share USER=<existing-account>
```

When the archive root is not configured in `.env`, provide it for that one command instead:

```bash
make archive-share USER=<existing-account> PERENE_ARCHIVE_ROOT=<absolute-archive-root>
```

The sharing command changes only the account's supplementary `perenearchive` membership and the archive tree's group/modes. It also configures the inherited POSIX ACL policy, so folders and files later created by the UI remain writable by the `perenearchive` group. Shared directories use setgid group-write permissions; `Videos/Cuts` and `Videos/VideoComposition` also use a sticky bit, while `.uploads` remains root-only. The account must start a new login session before the new group membership applies. Do not run this workflow on NAS systems without the standard Linux administration tools (including `setfacl` from the `acl` package); the command will report missing tooling without guessing.

5. Build and start the application:

```bash
make docker-run
```

For background execution:

```bash
make docker-run-bg
```

> Keep `.env` private. Do not commit a personal archive path or LAN address. Use `.env.example` as the safe configuration reference.

## Usage

Open the application locally at:

```text
http://localhost:8080/
```

If you changed `WEBAPP_PORT` in `.env`, use that port instead. For a private LAN URL, run:

```bash
make get-url
```

Useful commands:

```bash
make docker-build       # Build the application image
make docker-logs        # Follow web application logs
make docker-ps          # List running containers
make docker-down        # Stop the application
make docker-reset       # Stop the application and remove its volumes
make archive-group      # Create or verify the host perenearchive group
make archive-share USER=<existing-account> # Share archive editing with an existing account
```

The app is intended only for trusted local or private-LAN devices. Configure any additional allowed LAN hosts in the ignored `.env` file through `ALLOWED_NETWORK_HOSTS`.

## Tests

Run the full test suite in its isolated Docker Compose stack:

```bash
make test
```

To run another .NET command inside the Docker environment:

```bash
make dotnet ARGS="build"
```

## HTTPS / LAN Request Streaming (optional)

Chromium's browser request streaming (used by chunked archive uploads) requires HTTPS + HTTP/2. By default the app serves HTTP-only and uploads fall back to buffered chunks. To enable streaming uploads on your LAN:

1. In `.env`, set `HTTPS_SAN_HOSTS` to your NAS's LAN IP and/or hostname, and set `HTTPS_CERT_PASSWORD` to a password of your choosing.
2. Generate the self-signed certificate:

   ```bash
   make https-cert
   ```

   This writes `./https/perene.key`, `./https/perene.crt`, and `./https/perene.pfx` (all gitignored).

3. Restart the app (`make docker-run`). It now serves HTTPS on `HTTPS_WEBAPP_PORT` (default `8443`) in addition to HTTP, and every HTTP request is redirected to HTTPS.
4. On each client device, manually import `./https/perene.crt` into the OS or browser certificate trust store — this app cannot automate that step. Until a device trusts the certificate, it will see the browser's normal self-signed-certificate warning when redirected to HTTPS.

Unsetting `HTTPS_CERT_PASSWORD` (or removing `./https/perene.pfx`) and restarting reverts the app to HTTP-only with no other changes required.

## Docker telemetry and archive sharing

The Dashboard reads Docker or Podman telemetry through the configured `DASHBOARD_DOCKER_SOCKET`, mounted read-only directly at `/var/run/docker.sock` in the root webapp container. The socket path and its credentials are never exposed to browser clients. If that socket is unavailable, only the Dashboard Docker card reports unavailable; the rest of the app continues to work.

`make archive-group` creates the host-only `perenearchive` group with an available GID, or verifies the existing group without changing its GID. `make archive-share USER=<existing-account>` is opt-in and never creates, renames, deletes, changes the primary group of, or grants Docker-socket access to an account. It does not change `.env` settings.

## Docker Console

Open a new application container shell:

```bash
make docker-shell
```

Open a shell in the currently running web container:

```bash
make docker-exec
```

## Commercial Licensing and Support

Copyright © 2026 PereneTech.

Perene Archive is source-available under the [PolyForm Noncommercial License 1.0.0](LICENSE). Noncommercial use, modification, and redistribution are permitted provided the license and required PereneTech credit are retained.

Commercial use, managed deployment, customization, and technical support are available from PereneTech under a separate written agreement. Contact [fabio.r.nobrega@gmail.com](mailto:fabio.r.nobrega@gmail.com).

## Troubleshooting

### The archive cannot be found

Check that `PERENE_ARCHIVE_ROOT` is an absolute host path, exists, and is readable by Docker or Podman. The expected folders must be available beneath it. Start from `.env.example` if you need to create a local `.env`.

### The app is not available on another local device

Confirm the container is running with `make docker-ps`, then add the device-facing hostname or private IP address to `ALLOWED_NETWORK_HOSTS` in `.env`. Do not put private addresses in committed files.

### Video thumbnails, previews, subtitles, cuts, or compositions are unavailable

Inspect the application logs with:

```bash
make docker-logs
```

These workflows run in the background and require the bundled FFmpeg/FFprobe tools plus writable cache and output locations.

## Git Guideline

Create branches and commits in English using this guideline.

### Branches

- Feature: `feat/branch-name`
- Hotfix: `hotfix/branch-name`
- Proof of concept: `poc/branch-name`

### Commit prefixes

- Chore: `chore(context): message`
- Feature: `feat(context): message`
- Fix: `fix(context): message`
- Refactor: `refactor(context): message`
- Tests: `tests(context): message`
- Documentation: `docs(context): message`

### Pull requests

When opening a pull request on GitHub, use the repository pull-request template when one is available.
