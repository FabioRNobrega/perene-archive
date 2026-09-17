# Validation: Conversion TS Source and Size/Duration Policy

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1–FR2 | A `.ts` archive item is convertible and shows the existing action, but is not classified as playable video. |
| FR3–FR4 | Planner tests prove exact 1 GiB/one-hour-and-45-minute boundaries, allow a qualifying low-bitrate MP4 to be optimized, and prove only outputs at least 15% smaller publish. |
| FR5 | Endpoint contracts remain browser-safe and the existing Docker test workflow passes. |
| FR6 | Each accepted job displays a background-status modal whose link opens the Dashboard Jobs tab. |
| FR7 | The Jobs tab shows responsive cards across the available panel width with readable state and outcome information. |
| FR8 | Active jobs show browser-safe progress, elapsed time, start time, and approximate remaining time with two-second refreshes. |

## Test Cases

- Assert `.ts` is accepted by archive upload validation and resolves as `IsConvertibleVideo=true`, `IsVideo=false`.
- Assert a compatible MP4 larger than 1 GiB and shorter than one hour and 45 minutes selects `CompressVideo` regardless of bitrate.
- Assert exact size/duration boundaries select `Keep`, and exact 15% output savings publish while lower savings are skipped.
- Assert FFmpeg progress parsing handles valid, malformed, and terminal records without exposing paths or commands.
- Run `make test`.

## Manual Verification

1. Place a `.ts` file in an archive category and confirm its action menu includes Convert / Compress to MP4 without a play action.
2. Submit a qualifying high-bitrate short large MP4 and confirm the confirmation modal opens the Jobs tab and reports `CompressVideo`.
3. Submit an otherwise matching low-bitrate MP4 and confirm it is optimized only when the result is at least 15% smaller.

## Definition of Done

- `.ts` is included only in conversion-source handling and README documentation.
- Size/duration settings and planner behavior are covered by xUnit tests.
- `make test` passes in the isolated Docker stack.
- No browser/API path disclosure or source-replacement behavior is introduced.
- The accepted-job modal and Jobs tab remain usable with keyboard navigation at mobile and desktop widths.

## Rollback Plan

Remove `.ts` from `ConversionSourceExtensions` and revert the two planner option fields and related tests. Existing originals and outputs remain untouched.
