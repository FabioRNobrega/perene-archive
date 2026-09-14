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
- [Docker Console](#docker-console)
- [Commercial Licensing and Support](#commercial-licensing-and-support)
- [Troubleshooting](#troubleshooting)
- [Git Guideline](#git-guideline)

## Current Supported Features

| Area | Status | Supported capabilities and formats |
| --- | :---: | --- |
| Video library | ✅ | Browse and play `.mp4`, `.webm`, `.mov`, and `.m4v` videos. |
| Video editing | ✅ | Reframe vertical-video previews, set A/B points, and export stream-copied cuts. |
| Video compositions | ✅ | Combine two or more cuts into a composed MP4 with fades. |
| Video previews | ✅ | Static JPEG thumbnails and hover-preview MP4s are generated server-side. |
| Subtitles | ✅ | Same-named `.srt` subtitle files are converted to WebVTT for video playback. |
| Audio | ✅ | Browse and play `.mp3`, `.m4a`, and `.wav` files, including album artwork from `.png`, `.jpg`, or `.jpeg`. |
| Playlists | ✅ | Play every video/audio file in a folder (including subfolders) as a queue, with a YouTube-style player-and-queue view, auto-advance, Fill-tab expand, and resume of the same track and position across navigation. |
| Images | ✅ | Browse and view `.png`, `.jpg`, and `.jpeg` images in the archive carousel. |
| Books | ✅ | Read `.epub` books with progress, highlights, and notes. |
| Text documents | ✅ | View and edit `.md`, `.markdown`, and `.txt` documents. |
| PDF documents | ✅ | Browse and open `.pdf` documents. |
| Archive management | ✅ | Browse archive categories; create folders and `.txt`/`.md` text files; upload video/music/image/book/text/PDF files via resumable, sequential chunked sessions with acknowledged progress, rate/ETA, and Resume/Cancel recovery after an interruption or restart; rename, move, and send supported files and folders to Trash; permanently empty Trash with a confirmation prompt. |
| Appearance | ✅ | Dark and Kindle-paper light themes, responsive layout, and Fill-tab video mode. |
| System dashboard | ✅ | The home page (`/`) shows System, Memory, Storage, Network, PereneArchive, Docker, Health, History, and Alerts cards with a manual Refresh control, backed by dedicated `/api/dashboard/*` endpoints. |

## Install

1. Clone the repository and enter the project directory.

2. Ensure Docker Compose or Podman Compose is available.

3. Create the host archive layout. By default, the app uses `/home/PereneArchive`; alternatively, copy `.env.example` to `.env` and set `VIDEO_ROOT` to an absolute path on the Docker host.

   The archive root contains folders such as `Videos`, `Pictures`, `Music`, `Documents`, and `Books`. The video workflow also uses `Videos/Cuts` and `Videos/VideoComposition`.

4. Build and start the application:

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

Check that `VIDEO_ROOT` is an absolute host path, exists, and is readable by Docker or Podman. The expected folders must be available beneath it. Start from `.env.example` if you need to create a local `.env`.

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
