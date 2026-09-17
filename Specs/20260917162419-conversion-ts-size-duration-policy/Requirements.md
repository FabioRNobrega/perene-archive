# Requirements: Conversion TS Source and Size/Duration Policy

## Problem Statement

Archive conversion currently excludes MPEG transport-stream (`.ts`) files, and its compression planner relies solely on the existing resolution-based bitrate threshold. Users need `.ts` files to be submitted through the same non-destructive archive conversion workflow, while large short videos are considered for compression only when their bitrate independently justifies it.

## User Stories

- Given a `.ts` archive file, when I open its action menu, then I can submit it for Convert / Compress to MP4.
- Given a browser-compatible video shorter than one hour and 45 minutes and larger than 1 GB, when its bitrate exceeds its resolution threshold, then the planner selects compression.
- Given a submitted conversion job, when the server accepts it, then I see a background-status modal with a link that opens the Dashboard Jobs tab.
- Given the same size/duration case with bitrate at or below the threshold, when I submit it, then it is skipped rather than compressed.

## Functional Requirements

1. FR1 — `ArchiveService` shall classify `.ts` as a convertible source extension, allow it through archive upload validation, and expose the existing browser-safe `IsConvertibleVideo` flag without making it browser-playable by extension alone.
2. FR2 — `ArchiveBrowser.razor` shall consequently show the existing individual Convert / Compress action for a `.ts` file and retain all existing exclusions for folders, Trash, and duplicate active jobs.
3. FR3 — `VideoConversionOptions` shall define tested, named defaults for a 1 GB source-size threshold and a one-hour-and-45-minute maximum duration threshold.
4. FR4 — `MediaConversionPlanner` shall select `CompressVideo` for any compatible MP4 larger than 1 GiB and shorter than one hour and 45 minutes, regardless of bitrate; a completed output shall be published only when it saves at least 15% of the source size.
5. FR5 — Existing opaque-ID, archive containment, source-preservation, temporary-output, and Docker-only constraints remain unchanged.
6. FR6 — After any conversion request is accepted, including a `Keep` job, `ArchiveBrowser.razor` shall present an accessible Bootstrap modal linking to the Dashboard Jobs tab.
7. FR7 — The Dashboard Jobs tab shall use the available panel width for responsive, accessible job summaries rather than a narrow scrolling table.
8. FR8 — Active conversion jobs shall expose browser-safe progress, start time, elapsed time, and an approximate remaining time; this data remains in-memory for the current application session.

## Non-Functional Requirements

- The planner remains pure and independently unit-tested at the size, duration, and bitrate boundaries.
- No physical path, root-relative path, or FFmpeg command becomes browser-visible.

## Out of Scope

- Making `.ts` directly browser-playable.
- Changing transcoding arguments, compression CRF, queue capacity, or other conversion-source extensions.
- Replacing the original source or publishing an optimization that saves less than 15%.

## Open Questions

- None.
