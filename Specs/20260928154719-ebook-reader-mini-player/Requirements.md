# Requirements: Ebook Reader Mini Player

## Table of Contents

- [Problem Statement](#problem-statement)
- [User Stories](#user-stories)
- [Functional Requirements](#functional-requirements)
- [Non-Functional Requirements](#non-functional-requirements)
- [Out of Scope](#out-of-scope)
- [Open Questions](#open-questions)

## Problem Statement

`EpubReader.razor` occupies the full viewport above the persistent footer `Player.razor` rendered by `MainLayout.razor`. A reader who starts a music or video playlist must therefore leave the ebook to pause, stop, or change the active item. The reader needs a discreet, bottom-edge mini player that makes those essential controls available without interrupting reading.

## User Stories

- Given any video or music item is active while I read an EPUB, when I tap the reader's progress footer, then a mini player appears above that footer and shows the current item.
- Given the mini player is open, when I tap the reading surface outside its footer/control area, then it closes while the active media keeps playing or remains paused.
- Given an active playlist is playing, when I use Previous or Next in the mini player, then the existing playlist switches to its adjacent item without leaving the reader.
- Given I select Stop in the mini player, when the command completes, then playback stops and the active persistent player is cleared just like Stop in `Player.razor` today.

## Functional Requirements

1. FR1 — `EpubReader.razor` shall render no mini-player affordance when `PersistentPlayerState` has no active selection, and shall render one for any active music or video selection, including playlist entries.
2. FR2 — Tapping/clicking the EPUB progress footer shall toggle the mini player open and closed without closing the book or changing media playback state.
3. FR3 — The expanded tray shall present the active media name and accessible Previous, Play/Pause, Stop, and Next controls; Previous and Next shall use the existing playlist boundary rules and be disabled when no adjacent item exists.
4. FR4 — Play/Pause shall operate the same underlying media element and reflect its current playing/paused state. Stop shall pause, exit applicable presentation mode, and clear `PersistentPlayerState`, matching `Player.razor`'s existing Stop behavior.
5. FR5 — Tapping/clicking the ebook reading surface outside the expanded mini-player/footer shall dismiss the tray without pausing, resetting, clearing, or otherwise altering the selected media.
6. FR6 — The mini player shall rise from the progress footer with a short bottom-to-top slide and opacity transition, then reverse when dismissed. Under `prefers-reduced-motion: reduce`, it shall appear and disappear without perceptible animated movement.
7. FR7 — The reader-specific mini-player layering shall remain above the EPUB content but shall not interfere with text selection, page navigation, toolbar controls, keyboard use, or the existing reader error/loading states.
8. FR8 — The tray shall follow the design system in `Specs/20260827194328-perene-tech-design-system-refactor/design-guide-en.html`: Bootstrap 5.3.8 composition, semantic theme tokens for dark and Kindle-paper light modes, Bootstrap Icons at `currentColor`, visible focus, and at least 40×40 CSS-pixel icon targets.

## Non-Functional Requirements

- Keep media selection, playlist ordering, and clearing in the existing client-scoped `PersistentPlayerState`; do not add server endpoints, filesystem access, media processing, or browser-visible paths.
- Reuse the one persistent `Player.razor` media element rather than creating a second audio/video element, so there is no duplicate playback, competing media state, or new background-player lifecycle.
- Keep DOM-only coordination behind focused JS interop only where Blazor event handling cannot reliably distinguish an outside reading-surface interaction from the mini-player controls.
- Follow existing xUnit client-state/source-wiring test conventions and Docker Compose-only validation via `make test`.

## Out of Scope

- A queue editor, seek timeline, volume, speed, album artwork, captions, video preview, or full player settings in the reader tray.
- Persistence of mini-player visibility across reloads, books, or clients.
- Changing normal footer-player, playlist-page, music-page, video, archive, or EPUB reading behavior outside this reader overlay.
- Adding new packages, APIs, storage, FFmpeg work, authentication, or remote playback.

## Open Questions

- None. The first slice covers every active media type and only Previous, Play/Pause, Stop, and Next.
