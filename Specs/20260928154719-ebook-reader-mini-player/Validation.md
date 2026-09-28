# Validation: Ebook Reader Mini Player

## Table of Contents

- [Acceptance Criteria](#acceptance-criteria)
- [Test Cases](#test-cases)
- [Manual Verification](#manual-verification)
- [Definition of Done](#definition-of-done)
- [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | With no selected media, the ebook footer has no mini-player control; with an active music or video selection, it has one. |
| FR2 | Pressing the footer alternately opens and closes the tray without closing the ebook or altering playback. |
| FR3 | The tray displays current media and accessible Previous, Play/Pause, Stop, and Next controls; boundary controls are disabled correctly. |
| FR4 | Play/Pause changes the existing player media state; Stop clears the active player and removes the reader affordance. |
| FR5 | Tapping the reading surface outside the tray dismisses only the tray and leaves playback state unchanged. |
| FR6 | Normal-motion browsers show the anchored upward/downward drawer transition; reduced-motion browsers do not see material motion. |
| FR7 | Text selection, toolbar, pagination/tap navigation, loading/error feedback, and keyboard controls remain usable with the tray open and closed. |
| FR8 | Dark and Kindle-paper themes use guide tokens; all icon buttons have accessible names, visible focus, and 40px-or-larger targets. |

## Test Cases

**Unit tests:**

- `WebApp.Tests/Client/PersistentPlayerStateTests.cs` — verify any new reader-control state notification/projection, clear-on-stop behavior, and the existing previous/next playlist boundary outcomes used by the tray.
- `WebApp.Tests/Client/EpubReaderPaginationInteropTests.cs` or a focused new `EpubReaderMiniPlayerTests.cs` — follow the current source-wiring test convention to assert registration and disposal of any added reader interop callbacks and that the component contains the expected accessible control wiring.

**Integration tests:**

- No server integration test is needed unless implementation changes an API; this feature must remain entirely client-side.
- ⚠️ TODO: If a testable Blazor component harness is introduced later, add rendered interaction tests for footer toggle, outside dismissal, and disabled queue boundaries.

## Manual Verification

1. Run `make docker-run`, open the Books page, and open a valid EPUB.
2. In another archive section, start a music playlist; return to the already-open book without stopping it.
3. Confirm the reading-progress footer exposes the mini-player affordance. Open it and verify track name plus Previous, Play/Pause, Stop, and Next controls.
4. Play/pause and change tracks; confirm audio changes while the book remains open. At the first/last item, confirm the matching button is disabled.
5. Tap inside the reading content outside the drawer; confirm it retracts while audio continues. Reopen it and press Stop; confirm audio stops and the reader mini-player disappears.
6. Repeat with an active video or mixed video/music playlist; confirm the same controls work and no second media playback occurs.
7. Verify text selection, note menu, table of contents, toolbar collapse/reveal, pagination, keyboard focus, and reader close still work.
8. Switch application theme between dark and Kindle-paper light; inspect contrast, icons, focus indicators, and mobile layout. Enable reduced motion in the operating system/browser and confirm drawer motion is suppressed.
9. Run `make test`.

## Definition of Done

- Requirements, Plan, and Validation documents exist in this spec folder.
- The implementation preserves a single persistent media element and opaque-ID/privacy boundaries.
- New client-state/source-wiring tests and all existing tests pass through `make test`.
- Drawer interactions, outside dismissal, responsive behavior, themes, reduced motion, and accessibility are manually verified.
- `README.md` Current Supported Features table is updated during implementation.
- Vendor-specific guidance remains marked pending unless official documentation verification becomes available.

## Rollback Plan

Revert the reader-only drawer/component and its `ebookReader.js` listener registration. The existing `MainLayout.razor` footer `Player.razor`, persistent state, archive playback endpoints, and ebook reader remain intact, so removal restores the current behavior without data migration or configuration changes.
