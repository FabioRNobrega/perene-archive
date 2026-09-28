# Validation: Error Isolation, Async, and JavaScript Resilience

## Table of Contents

- [Acceptance Criteria](#acceptance-criteria)
- [Test Cases](#test-cases)
- [Manual Verification](#manual-verification)
- [Definition of Done](#definition-of-done)
- [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | An induced routed-page failure displays its fallback in `<main>` while the footer player remains mounted; an induced player failure leaves sidebar and route content available. |
| FR2 | Every new boundary fallback uses a Bootstrap alert with a feature-specific, user-readable explanation and next action; it exposes no exception/stack/path/code text. |
| FR3 | Navigating after a page failure or using a deliberate retry restores only the relevant boundary; no render/recovery loop occurs. |
| FR4 | Archive browser, image/comic, ebook, text, and PDF failures remain confined to their viewer/archive feature and do not interrupt the footer player. |
| FR5 | A dashboard-card endpoint or render failure leaves all sibling cards and the player usable. |
| FR6 | Utility polling/preview/request failure stops or degrades only that utility operation, leaving navigation and the player usable. |
| FR7 | Expected HTTP status, network, cancellation, and invalid JSON conditions produce contextual degraded state and safe warning logs rather than unhandled lifecycle/event exceptions. |
| FR8 | Expected JS import/invocation/disposal failures degrade only the dependent capability and do not expose technical details. |
| FR9 | Cancellation of a disposed/replaced component produces no error alert or unobserved failure; unexpected detached-task failures reach the owning fault domain. |
| FR10 | A failed audio-track remux or background job is recorded/logged as failed while later work and host streaming continue. |
| FR11 | Logs contain feature/action and opaque IDs where relevant but no physical/root-relative path; UI contains no exception detail. |
| FR12 | Reloading the page does not restore player selection, position, or preferences as a result of this implementation. |
| FR13 | Alerts have correct live semantics, clear text beyond color/icon, keyboard-operable retry controls, and preserve existing loading/empty/success states. |
| FR14 | README describes the implemented fault-isolation behavior in Current Supported Features. |

## Test Cases

**Unit tests:**

- `WebApp.Tests/Client/` — add focused tests for any extracted error-classification/message policy: expected transport, rate-limit/service-unavailable, cancellation, malformed payload, and unexpected-error outcomes must map to the intended safe user message and logging severity without exception text.
- `WebApp.Tests/Client/PersistentPlayerStateTests.cs` — verify this spec does not change session-only player-selection semantics or introduce persistence.
- `WebApp.Tests/Services/AudioTrackRemuxServiceTests.cs` — verify a remux fault is observed/cleared from active tracking and a later request can run/return a terminal state.
- Existing `WebApp.Tests/Services/*JobQueueTests.cs` and status-store tests — extend only where a worker status transition changes; assert a failed job does not prevent another queued job from being processed.
- Existing endpoint tests — verify existing non-success status behavior remains compatible with component message mapping where an endpoint already distinguishes full queue, missing item, or validation failure.

**Integration tests:**

- Run `make test` to execute `WebApp.Tests` in the documented isolated Docker Compose stack.
- ⚠️ TODO: The repository has no bUnit/browser JS interop harness. Add no new framework solely for this spec; use manual browser verification for actual `ErrorBoundary`, lifecycle, module-import, and Promise-rejection behavior.
- Where practical, use a controlled test endpoint/fake service only inside tests to return invalid JSON, 429/503, and network-equivalent failure; do not alter production endpoint contracts solely for testing.

## Manual Verification

1. Run `make docker-run`, load a playable archive video, and confirm the footer player is visible and playing.
2. Trigger an archive listing/thumbnail/preview operational failure using the established development environment. Verify an archive-specific Bootstrap alert explains what is unavailable and what to do next; confirm the footer player continues to play and respond.
3. Navigate to another route after the archive feature fallback. Verify the route boundary recovers and the new page renders. Use the relevant retry control after restoring the dependency and verify the feature itself recovers without a page reload.
4. On Dashboard, make one metric endpoint unavailable. Use Refresh and verify only its card shows an unavailable state; the other cards refresh and the player remains active.
5. On Video Cut, Video Composition, and Image Cutter, interrupt a polling/API request and verify polling stops or degrades with an understandable alert, no repeated alert loop, and no effect on playback.
6. Test archive move/upload, text editor preview/save/export, ebook/chapter/highlight/progress, comic progress, image viewer/crop, and PDF open/close with a temporarily unavailable source. Verify each failure is local to the current feature.
7. Exercise Player fullscreen, drag measurement, media synchronization, subtitles, multi-audio preparation, Save Cut, VR, and navigation/disposal. Simulate an unavailable/rejected JS module or browser capability where feasible; verify ordinary playback remains usable when the optional capability fails and technical exception text never appears.
8. Navigate away while archive job/media polling, utility polling, hover delays, editor preview delays, and player control-hide delays are active. Check browser console and server logs for no unobserved-task failure, and verify caller-initiated cancellation produces no user-facing error.
9. Cause a single audio-track remux/background media job to fail. Verify its status becomes failed or the caller gets the existing safe failure state, server logs are safe, and subsequent work remains available.
10. Reload the browser. Verify no player selection, time, or preferences are newly restored by this change.
11. Test light/dark themes, narrow viewport, keyboard-only alert/retry access, and screen-reader semantics. Run `make test` and confirm it succeeds.

## Definition of Done

- Requirements, Plan, and Validation are complete under this spec folder.
- Narrow boundaries match the documented component hierarchy and do not place player and routed content in one failure domain.
- Every audited expected HTTP/JSON/JS/detached-task path has an explicit, scoped handling policy; unexpected errors are not silently swallowed.
- New failure UI uses Bootstrap alerts, accessible text/roles, safe messages, and appropriate recovery actions.
- No physical paths, exception text, stack trace, source request URL, or private configuration reaches browser UI or normal logs.
- No player persistence/local-storage/session-recovery behavior is added.
- All relevant unit/endpoint/service tests pass via `make test`.
- Manual verification covers player survival, route recovery, archive/viewer/editor isolation, dashboard-card isolation, utility polling, JS interop, cancellation, and server job continuation.
- `README.md` Current Supported Features is updated during implementation.
- No package, endpoint, Docker, mount, FFmpeg, or frontend-framework change is introduced.

## Rollback Plan

- Revert the boundary markup/wrapper and the component-local error-handling changes as one change set. This returns the current global Blazor error-ui behavior without a database migration, endpoint rollback, cache cleanup, or media-file cleanup.
- Revert any focused error-policy helper and associated tests together.
- Revert the `AudioTrackRemuxService` observation change only if it regresses remux scheduling; no persisted task state exists.
- Revert the README feature-table row with the implementation changes. No configuration flag, user data, or browser persistence requires cleanup.
