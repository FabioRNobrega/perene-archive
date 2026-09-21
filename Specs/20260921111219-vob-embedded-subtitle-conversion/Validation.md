# Validation: VOB Conversion and Embedded Subtitle Burn-in

## Table of Contents

- [Acceptance Criteria](#acceptance-criteria)
- [Test Cases](#test-cases)
- [Manual Verification](#manual-verification)
- [Definition of Done](#definition-of-done)
- [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | `.vob`/`.VOB` is accepted for archive upload and listed as convertible, but never exposed as directly playable. |
| FR2 | A probe returns only ordered index/codec/language/label subtitle options and produces an unknown-language fallback. |
| FR3 | Preview returns safe subtitle options only for a currently resolvable opaque ID; response JSON contains no filesystem path or raw probe data. |
| FR4 | The plan modal requires an accessible radio selection when subtitle options exist and clearly discloses burn-in. |
| FR5 | Missing, stale, or tampered subtitle indexes are rejected on preview/submission; only a freshly probed index reaches a job profile. |
| FR6 | Generated arguments render exactly the selected stream, encode H.264/AAC MP4, and do not map a subtitle stream into output. |
| FR7 | A rendering failure leaves no published MP4 or temporary output and returns a path-redacted failure. |
| FR8 | A successful job preserves the original and atomically creates one collision-safe sibling MP4 whose final probe contains no subtitle stream. |
| FR9 | Embedded-subtitle input is accepted with selection while a subtitle-free source keeps existing planning behavior; VOB remains conversion-only. |
| FR10 | README accurately documents VOB conversion input and one-track permanent burn-in. |

## Test Cases

**Unit tests:**

- `WebApp.Tests/Services/ArchiveServiceTests.cs` — verify lowercase/uppercase VOB files are conversion/upload eligible and not `IsVideo`.
- `WebApp.Tests/Services/FfprobeVideoConversionProbeTests.cs` — parse fixture JSON with multiple subtitle streams, language tags, no language tag, non-subtitle streams, and invalid input; assert safe ordered options only.
- `WebApp.Tests/Services/ConversionProfileCatalogTests.cs` and `ConversionProfileResolverTests.cs` — verify subtitle-free profiles are unchanged, embedded-stream selection is required, unknown/tampered indices fail, and a selected stream forces transcode instead of remux.
- `WebApp.Tests/Services/VideoConversionArgumentBuilderTests.cs` — assert selected `si`/stream handling, H.264/AAC mapping, no output subtitle map, filter composition with deinterlace/scale, and escaped hostile filename cases remain one `ArgumentList` value.
- `WebApp.Tests/Services/FfmpegVideoConversionGeneratorTests.cs` — verify failed subtitle rendering cleans temporary output and output validation rejects a remaining subtitle stream.

**Integration tests:**

- `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs` — use an opaque archive item fixture and fake probe to verify preview safe JSON, subtitle options, missing/index-tampered selection rejection, re-probe on submission, queue-full behavior, and no source path leakage.
- ⚠️ Add an FFmpeg-backed Docker test fixture with a small legal sample VOB/MKV containing at least two tagged embedded subtitle tracks. Assert the selected track is visually/structurally rendered, output is an H.264/AAC MP4 without subtitle streams, and original bytes/timestamp remain unchanged. Skip only when the test image cannot provide the fixture; do not rely on a host FFmpeg install.

## Manual Verification

1. Place one complete VOB containing at least two embedded subtitle languages in an editable archive category under the configured archive root; record its size and timestamp.
2. Start the Docker-only application with `make docker-run`.
3. Open the Archive category, confirm the VOB is listed with **Convert / Compress to MP4** but does not expose direct Play.
4. Open the conversion plan. Confirm the source facts and all detected subtitle radio buttons render, a language selection is required, and the burn-in warning is visible and keyboard-operable.
5. Select a non-default language, choose a normal compression profile, and start conversion. Inspect Jobs until it completes.
6. Confirm the source VOB still exists unchanged and a sibling `<stem> Converted 0001.mp4` exists. Open the MP4 through the Archive player, seek to subtitle dialogue, and confirm the selected captions are visible but have no player subtitle toggle/track.
7. Repeat with a subtitle-free convertible source and verify no subtitle chooser appears and conversion still works.
8. Test an intentionally unsupported/corrupt subtitle stream or a Docker image without the required filter capability; confirm the job fails without publishing an MP4 or revealing a local path.
9. Run `make test` and confirm the complete isolated Docker test suite passes.

## Definition of Done

- Requirements, Plan, and Validation documents are present in this spec folder.
- VOB conversion eligibility, safe embedded-subtitle probing, server validation, burn-in execution, and README support are implemented.
- Existing conversion behaviors remain covered and new xUnit unit/endpoint tests pass through `make test`.
- An FFmpeg-backed Docker validation proves selected subtitle burn-in and source preservation.
- The plan modal satisfies responsive, keyboard, error/loading, accessible-label, light/dark, and 40px target requirements.
- FFmpeg/FFprobe and ASP.NET Core decisions remain supported by the official documentation linked in `Plan.md`.

## Rollback Plan

## Closed-caption extension

Verify an A53-captioned VOB exposes the checkbox, produces visibly burned captions when selected, and removes the temporary SRT on both a successful conversion and an extraction/rendering failure.

- Revert the `.vob` addition in `ArchiveService.ConversionSourceExtensions` to remove new VOB upload/conversion eligibility.
- Revert the subtitle option DTO/UI, resolver selection validation, and `VideoConversionArgumentBuilder` burn-in branch to restore the prior policy that rejects embedded subtitle streams.
- Existing sources and generated sibling MP4s are not modified by rollback; operators may retain or remove generated outputs through normal archive management. No migration, database, or persistent configuration change is involved.
