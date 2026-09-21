# Requirements: VOB Conversion and Embedded Subtitle Burn-in

## Table of Contents

- [Problem Statement](#problem-statement)
- [User Stories](#user-stories)
- [Functional Requirements](#functional-requirements)
- [Non-Functional Requirements](#non-functional-requirements)
- [Out of Scope](#out-of-scope)
- [Open Questions](#open-questions)

## Problem Statement

The Archive conversion workflow supports several non-browser video containers but not `.vob`, and its current `ConversionProfileResolver` rejects every source with embedded subtitle streams. DVD-style VOB subtitles are commonly image-based streams, so they cannot be served by the existing same-named-SRT-to-WebVTT playback pipeline. Users need to convert one archive VOB (or another supported conversion source) to a sibling MP4 and, when it contains embedded subtitles, select one detected language to permanently burn into that MP4.

## User Stories

- Given a single `.VOB` file in an editable archive category, when I open **Convert / Compress to MP4**, then I can review and submit the same server-validated conversion plan as other convertible sources.
- Given a VOB, MKV, or another conversion source with embedded subtitle streams, when its plan opens, then I can choose one available subtitle language through an accessible Bootstrap radio-button group before starting conversion.
- Given I select a subtitle language and start conversion, when the job succeeds, then the original file remains unchanged and a collision-safe sibling MP4 contains that subtitle visibly burned into its video frames.
- Given an embedded subtitle stream has no language tag, when its plan opens, then I can still select it using a safe ordinal label such as `Track 1 (language unknown)`.
- Given a source has no embedded subtitle streams, when I convert it, then its existing conversion behavior and profile controls continue without a subtitle-selection requirement.

## Functional Requirements

1. FR1 — `ArchiveService.ConversionSourceExtensions` shall accept `.vob` case-insensitively as an archive conversion/upload source, mark it `IsConvertibleVideo`, and not mark it directly browser-playable or add it to the Video Library playback formats.
2. FR2 — `FfprobeVideoConversionProbe` shall return a browser-safe ordered list of embedded subtitle streams containing only their input-stream index, codec, normalized language tag when available, and a safe display label; it shall preserve no physical paths or raw FFprobe JSON in the browser contract or normal logs.
3. FR3 — `POST /api/archive/{category}/items/{id}/conversion/preview` shall include the safe subtitle choices in `VideoConversionPlanDto` when the resolved source contains embedded subtitles, and shall reject an unrecognized, stale, unreadable, non-convertible, or path-unresolvable item as it does today.
4. FR4 — When a preview contains embedded subtitles, `ArchiveBrowser.razor` shall require the user to select exactly one listed subtitle stream via a labelled Bootstrap radio-button group before enabling **Start conversion**. The label must prefer a reported language and use an ordinal unknown-language fallback; the plan shall state that the selected subtitle is permanently burned into the video and cannot be toggled later.
5. FR5 — A preview and conversion submission shall use a compact subtitle-stream selection value only; `ConversionProfileResolver` shall validate that it exists in the freshly probed source and persist its immutable resolved stream index/label in the server-side `ResolvedVideoConversionProfile`/`VideoConversionJob`, never accepting a client FFmpeg argument, filter string, language label, or path.
6. FR6 — For a confirmed embedded-subtitle selection, `VideoConversionArgumentBuilder` shall map the primary video and optional primary audio as today, use the selected subtitle stream in a fixed server-constructed burn-in filter, and encode browser-compatible H.264/AAC MP4 output. It shall not copy or publish an embedded subtitle stream separately.
7. FR7 — The burn-in path shall compose safely with the current deinterlace and non-upscaling aspect-preserving scale policy, including source paths containing FFmpeg filter-special characters. If the installed FFmpeg image lacks the required subtitle-rendering capability or the selected stream cannot be decoded/rendered, the job shall fail safely, clean its temporary output, retain the source, and return only a path-redacted diagnostic.
8. FR8 — The conversion worker shall retain current source identity revalidation, duplicate-source protection, bounded queue, pause/resume/stop, free-space preflight, atomic sibling publication, output validation, and collision-safe `<stem> Converted NNNN.mp4` naming. On success there shall be exactly the original source plus the new MP4; the output shall not retain a selectable embedded subtitle stream.
9. FR9 — A source with embedded subtitles shall no longer be rejected merely for containing them. Existing sources without embedded subtitles shall retain the existing profile planning/submission behavior, and any VOB or other non-browser conversion source remains accessible only through Archive conversion, not direct browser playback.
10. FR10 — `README.md` shall update its Current Supported Features table to list `.vob` as a conversion input and disclose that one selected embedded subtitle language can be burned into the MP4, rather than preserved as switchable captions.

## Non-Functional Requirements

- Preserve server-only archive containment, opaque archive IDs, and the prohibition on exposing physical/root-relative paths, command lines, process IDs, raw probe output, or unredacted FFmpeg diagnostics to clients or normal logs.
- Reuse the Dockerfile-provided FFmpeg/FFprobe, current singleton bounded conversion queue and worker, `ProcessStartInfo.ArgumentList`, and Docker Compose-only `make test` workflow. No browser media processing, database, additional package, concurrent worker, OCR, or separate FFmpeg service is introduced.
- The Bootstrap 5.3/Bootstrap Icons design system applies: radio controls must have a visible group label, keyboard operation, clear selection/error feedback, 40px minimum target sizing, responsive wrapping, dark/light compatibility, and live status semantics.
- Keep nullable-aware C#, client/server separation, focused service boundaries, deterministic unit tests, and xUnit conventions.

## Out of Scope

- Direct browser playback, thumbnails, hover previews, cuts, playlists, or Video Library scanning of `.vob` sources.
- Joining multiple DVD VOB parts; each user-selected input is one complete VOB file.
- OCR or extraction of bitmap/DVD subtitles to `.srt`/WebVTT; generated subtitles are permanently part of the encoded video image.
- Preserving multiple embedded subtitle tracks, a subtitle-off output variant, subtitle styling controls, external `.srt` burn-in, or multiple audio-track selection.
- Replacing, deleting, renaming, or moving the VOB/MKV/original source; persistent jobs or parallel conversion.

## Open Questions

## Closed-caption extension

The conversion plan also detects ATSC A53 closed-caption frame side data and exposes an optional accessible “Burn closed captions into the video” checkbox. On selection, the server extracts FFmpeg's decoded default closed-caption service to a temporary server-only SRT and burns it into the MP4; that temporary file is deleted on success, failure, or cancellation. The feature does not claim individual CC1–CC4 selection because the Docker FFmpeg decoder has no reliable per-service selector.

- Resolved: the first release adds `.vob` only to Archive conversion/upload support, not direct playback.
- Resolved: the user chooses one embedded subtitle track/language to burn into the sibling MP4; switchable embedded subtitles and text extraction are not part of this release.
- Resolved: input is one complete VOB file; multi-file DVD-title joining is out of scope.
