# Validation: Conversion Planning and Configurable Profiles

## Table of Contents

- [Acceptance Criteria](#acceptance-criteria)
- [Test Cases](#test-cases)
- [Manual Verification](#manual-verification)
- [Definition of Done](#definition-of-done)
- [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | Choosing conversion opens a plan dialog; no conversion job exists until explicit confirmation; cancelling leaves no job/output. |
| FR2 | Preview accepts only a current opaque convertible item ID, rejects invalid/stale sources, and its JSON contains no physical or relative path. |
| FR3 | Preview visibly reports source facts plus selected output facts, clearly marked estimated size, and savings. |
| FR4 | Every supported source extension exposes the same mode/resolution/preset/target-size controls; target size updates the server-authoritative estimate. |
| FR5 | At-or-below-720p sources default to original resolution, higher sources show the documented recommended default, and no selection upscales output. |
| FR6 | Tampered/invalid selections, stale source facts, and unsupported numeric values are rejected; accepted jobs contain only a resolved server profile. |
| FR7 | Confirmed plans honor existing capacity, duplicate, pause/resume/stop, source-preservation, cleanup, and atomic-publication behavior. |
| FR8 | Generated compression output is validated H.264/AAC/yuv420p MP4 at the resolved non-upscaled dimensions and applies the documented interlace policy. |
| FR9 | Probe, catalog, estimate, resolver, argument builder, generator, and job orchestration have focused interfaces/classes and direct unit tests. |
| FR10 | Jobs show a safe selected-profile summary with existing status/control behavior; no UI/API/log response exposes a path, command, PID, or raw FFmpeg output. |
| FR11 | The plan clearly states the embedded subtitle policy; unsupported streams do not produce a misleading completed browser-subtitle result. |
| FR12 | README accurately describes the new per-file planning and output-estimate workflow. |

## Test Cases

**Unit tests:**

- `WebApp.Tests/Services/ConversionProfileCatalogTests.cs` — source-aware defaults, permitted output heights, no-upscale rules, and common controls for `.ts`, `.mkv`, and `.mp4` probe fixtures.
- `WebApp.Tests/Services/ConversionEstimateCalculatorTests.cs` — duration/bitrate estimate math, target-size reverse calculation, MP4-overhead reserve, round-trip tolerance, and invalid/boundary values.
- `WebApp.Tests/Services/ConversionProfileResolverTests.cs` — preset and target-size selections resolve to bounded immutable profiles; tampering/stale/incompatible choices fail safely.
- `WebApp.Tests/Services/VideoConversionArgumentBuilderTests.cs` — each resolved strategy produces separate safe arguments, preserves aspect ratio, cannot upscale, and includes the selected deinterlace/audio policy where applicable.
- Existing `WebApp.Tests/Services/FfmpegVideoConversionGeneratorTests.cs`, `VideoConversionJobStatusStoreTests.cs`, `VideoConversionProcessControllerTests.cs`, and `FfmpegVideoConversionProgressTests.cs` — extend to prove the extraction retains free-space, source identity, cleanup, pause/resume/stop, status, and publish guarantees.

**Endpoint/integration tests:**

- Extend `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs` or add `ArchiveEndpointsConversionPlanningTests.cs` using the existing `VideoManagerFactory` / `WebApplicationFactory` pattern: preview valid opaque source; reject non-convertible/unknown/trash/moved source; confirm no job on preview; reject malformed or stale selection; accept valid selection; preserve queue-full and duplicate-active behavior.
- Assert all preview, submit, and dashboard-job JSON omit the temporary directory and archive root.
- Add a Docker-backed FFmpeg fixture test where practical for a short progressive source and a short interlaced source: output probe reports MP4/H.264/AAC, selected dimensions, positive duration, and source bytes/name remain unchanged. Use a separate unsupported-subtitle fixture once the policy is chosen.

## Manual Verification

1. Create a clean archive test area with one large 1080p convertible file, one 720p-or-smaller source, and one `.ts` source; do not use production paths in committed configuration.
2. Run `make docker-run`.
3. In Archive Browser, open **Convert / Compress to MP4** for each source. Confirm no dashboard job appears before **Start conversion**.
4. Verify source facts, profile controls, default resolution behavior, labelled estimated size/savings, keyboard navigation, Escape/Cancel behavior, dark/light appearance, and narrow-screen layout.
5. Select original-resolution and 720p plans; test a preset and a target-size choice. Verify estimates change and that no plan offers upscale.
6. Start one valid plan. In Dashboard Jobs, verify its safe profile summary, progress/ETA, Pause, Resume, Stop confirmation, and post-stop source/temp-file behavior.
7. Complete a short conversion. Verify the source remains untouched, output is a uniquely named sibling MP4, dimensions/profile match the confirmation, actual size is shown, and Archive Browser can play it.
8. Attempt an invalid/moved item and a duplicate active source; confirm clear safe feedback and no path/command disclosure. Verify the agreed embedded-subtitle policy is visible.
9. Run `make test` and confirm the isolated Docker Compose suite passes.

## Definition of Done

- Requirements, plan, and validation documents are implemented without unresolved deviations; the two listed policy TODOs are decided and recorded.
- Every FR has automated coverage appropriate to its layer, and `make test` passes.
- `ArchiveBrowser.razor`, Dashboard Jobs, DTOs, and README follow the established responsive Bootstrap design system and archive privacy boundary.
- The conversion module has focused testable responsibilities, no arbitrary client FFmpeg arguments, and preserves existing pause/stop/atomic-publish behavior.
- Current official Microsoft Learn and FFmpeg documentation evidence is added to the implementation update before coding vendor-specific API/argument decisions.

## Rollback Plan

Revert the new preview/submit routes and restore the existing immediate conversion action in `ArchiveBrowser.razor` and `ArchiveEndpoints.cs`; retain no migration because planning/job state remains in memory. Any in-progress job continues under its immutable resolved profile until completion/stop, while removing the new service registrations in `Program.cs` disables future planned jobs. Delete only temporary files owned by stopped/failed jobs through the existing generator cleanup path; never delete source or published sibling MP4s automatically.
