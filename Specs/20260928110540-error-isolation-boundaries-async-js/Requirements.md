# Requirements: Error Isolation, Async, and JavaScript Resilience

## Table of Contents

- [Problem Statement](#problem-statement)
- [User Stories](#user-stories)
- [Functional Requirements](#functional-requirements)
- [Non-Functional Requirements](#non-functional-requirements)
- [Out of Scope](#out-of-scope)
- [Open Questions](#open-questions)

## Problem Statement

The globally interactive WebAssembly client has a persistent footer `Player` in `Layout/MainLayout.razor`, but currently has no `ErrorBoundary` fault domains. Several component lifecycle methods, event handlers, JS interop operations, and intentionally detached polling/delay tasks can therefore produce an unhandled client failure that interrupts unrelated UI. Examples include `ArchiveBrowser.razor` media/job polling, `Player.razor` media and fullscreen interop, and independent dashboard-card refreshes. Expected local conditions—NAS/API unavailability, temporary rate limiting, generated-media delay, malformed API payload, unavailable browser API, and component cancellation—must degrade the affected feature with useful Bootstrap feedback rather than disrupt the persistent player or unrelated features.

## User Stories

- Given the archive listing, thumbnail polling, or archive JavaScript integration fails, when I am playing media in the persistent footer player, then playback and its controls remain available while the archive shows a clear recovery alert.
- Given one Dashboard metric endpoint fails, when I refresh the dashboard, then the failed card explains that its information is unavailable while the other cards still refresh and render.
- Given a browser media/VR/fullscreen/measurement operation fails, when I use the player, then the affected optional capability is disabled or explained without exposing implementation details or collapsing the rest of the app.
- Given a background client poll, delay, or server-side detached media job fails, when its component/service is still active, then the failure is observed, safely logged, and represented as degraded feature state rather than becoming an unobserved task failure.
- Given a component has an unexpected programming failure, when it is inside a defined fault domain, then a Bootstrap alert identifies the unavailable user feature and offers an appropriate retry/reload action without displaying exception text, stack traces, filesystem paths, or opaque implementation details.

## Functional Requirements

1. FR1 — `Layout/MainLayout.razor` shall isolate routed page content (`@Body`) from the persistent footer `Player` with separate interactive error boundaries, so a routed-page failure does not remove the footer player and a player failure does not remove navigation or routed content.
2. FR2 — Error-boundary fallback UI shall use existing Bootstrap 5.3 alert patterns, explicit user-facing feature names, meaningful next actions, non-color-only communication, and no exception text, stack trace, physical path, request URL, or implementation code. Example messages shall distinguish a temporary condition (such as thumbnail generation being busy) from a feature that cannot currently be used.
3. FR3 — Boundary recovery shall occur only after navigation or an explicit user retry that resets the responsible component state; it shall not be invoked from rendering logic or create a retry loop.
4. FR4 — `ArchiveBrowser.razor`, `ImageCarouselViewer.razor`, `ComicViewer.razor`, `EpubReader.razor`, `TextDocumentEditor.razor`, and `PdfDocumentViewer.razor` shall be placed in fault domains that allow the current archive feature to fail without disrupting the footer player, application shell, or unrelated route.
5. FR5 — Each independently refreshed Dashboard card and `DashboardConversionJobsTab.razor` shall isolate unexpected rendering/lifecycle failures from other dashboard cards, while expected endpoint failures remain local unavailable states.
6. FR6 — `VideoCut.razor`, `VideoComposition.razor`, `ImageCutter.razor`, and their generated-media/hover-preview operations shall contain expected API, cancellation, and delayed-task failures locally, with an unavailable/retry state that does not affect the persistent player.
7. FR7 — The client shall handle expected lifecycle and event-operation failures locally at every audited `HttpClient`/JSON boundary: unsuccessful HTTP response, `HttpRequestException`, caller-initiated cancellation, and invalid JSON where the feature can reasonably degrade. It shall preserve existing endpoint-specific status messaging where present.
8. FR8 — Client JS interop in `Player.razor`, archive/reader/viewer/editor components, tooltip modules, and disposal paths shall treat module import, rejected Promise, serialization, timeout/cancellation, and disconnected/disposed component conditions as expected operational failures when a degraded user experience is possible.
9. FR9 — The client shall not silently swallow unexpected programming exceptions. Detached client tasks started by polling, delayed hover/control/status behavior, or callbacks shall either contain and log expected operational failures or dispatch unexpected failures to their owning component’s error boundary using Blazor’s supported exception-dispatch mechanism.
10. FR10 — The server’s `AudioTrackRemuxService` detached task and all background worker per-job loops shall observe, log, and represent individual job failure without terminating unrelated jobs, a worker loop, video streaming, or the application host.
11. FR11 — Expected operational-failure logging shall use the existing `ILogger<T>` pattern with stable feature/action context and opaque IDs only. It shall never write physical/root-relative paths, request bodies, or exception details to the browser UI.
12. FR12 — The footer `Player` shall remain session-only. This spec shall not persist selection, playback time, preferences, or player state beyond the active WebAssembly session.
13. FR13 — Existing empty, loading, successful, and accessibility behavior shall remain intact. New alerts shall use semantic `role="alert"` for failures requiring immediate attention or `role="status"`/polite live state for nonurgent availability changes, with accessible retry controls where retries are offered.
14. FR14 — `README.md`’s **Current Supported Features** table shall document the implemented user-visible fault-isolation and recovery behavior.

## Non-Functional Requirements

- **Privacy and security:** No browser-visible error UI, client log, or server log may reveal physical paths, root-relative paths, source filenames outside existing browser-safe DTO data, exception messages, stack traces, credentials, or private LAN configuration.
- **Architecture:** Keep the existing hosted Blazor WebAssembly split. Client components own UI state and browser interop; server services own filesystem, queue, and process work. Do not add a frontend framework, a generic catch-all error service, or new external runtime dependencies.
- **Scope of handling:** Catch only failures expected at that boundary. Unexpected exceptions must be logged and routed to the appropriate boundary, not masked by broad silent `catch (Exception)` blocks.
- **Performance:** Poll cadence, range streaming, FFmpeg authorization, queues, and snapshot/opaque-ID boundaries remain unchanged. Failure handling must not create retry storms, unbounded tasks, or render loops.
- **Compatibility:** Preserve Bootstrap 5.3.8/Bootstrap Icons presentation, dark/light tokens, keyboard behavior, user-selected routes, Docker-only workflows, and existing server endpoint contracts.
- **Testability:** Extract only focused, browser-safe policy/state helpers when necessary to unit test error classification and detached-task ownership without a browser. Use the existing xUnit project and Docker Compose test workflow.

## Out of Scope

- Persistent-player recovery after reload, browser restart, fatal WASM runtime failure, or a genuinely unrecoverable player error.
- Player preference/position persistence, new local-storage schemas, or a new player-state spec.
- Server endpoint/DTO contract changes, authentication, rate-limiting infrastructure, retry middleware, or changes to NAS mounts.
- Changing FFmpeg processing scope, queue capacity, output formats, or media-processing authorization.
- Replacing all existing feature-specific alerts with a global notification/toast system.
- Adding broad boundaries around every card, video tile, button, or low-level component where a parent defines the correct fault domain.

## Open Questions

- None. The agreed scope is all audited client/server async and JS areas, session-only player behavior, and Bootstrap alerts that explain the user impact without exposing implementation details.
