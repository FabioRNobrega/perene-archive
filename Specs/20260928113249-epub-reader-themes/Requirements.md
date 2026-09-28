# Requirements: EPUB Reader Themes

## Table of Contents

- [Problem Statement](#problem-statement)
- [User Stories](#user-stories)
- [Functional Requirements](#functional-requirements)
- [Non-Functional Requirements](#non-functional-requirements)
- [Out of Scope](#out-of-scope)
- [Open Questions](#open-questions)

## Problem Statement

`WebApp/WebApp.Client/Components/EpubReader.razor` keeps its typography controls only in component memory. A reader who chooses Arial at 24px (or adjusts line spacing) loses those choices after closing a book. The reader also has no way to save, name, reuse, update, rename, or remove a complete reading appearance. Readers need one globally persisted active appearance and a server-owned library of named EPUB themes, using the same private JSON persistence approach as book progress.

## User Stories

- Given I change reader options while reading any EPUB, when I close and later open any EPUB, then my last active options are restored.
- Given I have adjusted font, size, text color, background color, and line spacing, when I name and save the appearance, then it is available as a named theme in future reading sessions.
- Given I select a saved theme, when it is applied, then all five appearance values update together without changing my reading location.
- Given I own a saved theme, when I edit, rename, or delete it, then the theme library reflects that change while the reader remains usable.
- Given I reset to Default, when the app is in light or dark mode, then the reader resumes its existing inherited app colors and default typography rather than forcing custom colors.

## Functional Requirements

1. FR1 — `EpubReader.razor` must restore one globally saved active reader appearance before rendering a book, regardless of the book's opaque item ID. It must contain only font family, font size, line spacing, foreground color, and background color; it must not be keyed by or reveal a book path, category path, or file metadata.
2. FR2 — Changing any supported appearance control must immediately update the reading surface, preserve the current reader's best available pagination/word-position behavior, and save the resulting active appearance on a best-effort basis so it is restored after closing/reopening a book.
3. FR3 — The text-settings panel must provide accessible controls for exactly the requested theme fields: the existing supported font choices, existing bounded font sizes, existing bounded line-spacing choices, a text-color picker, and a background-color picker. The existing text-width/margins control remains independent and is not part of a named theme.
4. FR4 — The reader must offer a Default action that restores the present reader defaults: Montserrat, 18px, Normal line spacing, and inherited Bootstrap body foreground/background colors. The default appearance must continue to follow the app-wide light/dark mode; inherited colors are represented without storing browser or host-specific color values.
5. FR5 — The settings panel must let a reader create a named theme from the currently active five theme fields. Names must be trimmed, required, limited to a documented reasonable length, and unique case-insensitively within the global theme library; invalid or conflicting names must produce an accessible, actionable message without losing the active appearance.
6. FR6 — The settings panel must list saved themes and let the reader apply one, update its saved values from the current controls, rename it, and delete it. Applying a theme updates and persists the global active appearance. Editing controls after applying a saved theme makes the current appearance custom until it is explicitly saved/updated; it must not silently mutate that saved theme.
7. FR7 — Deleting a saved theme must remove only that named library entry. If it was selected, its already-applied values remain the global active appearance until the reader applies another theme, changes values, or resets to Default.
8. FR8 — The server must expose a browser-safe reader-theme API for loading the global active appearance and named themes, saving the active appearance, and performing create/update/rename/delete operations. It must validate all payload values against the same supported reader choices and safe CSS color format; unknown theme IDs return Not Found and invalid input returns Bad Request.
9. FR9 — Theme data must be persisted server-side in one JSON file under the existing archive-owned `Books/Notes` persistence location, serialized by a focused theme service with a process-local lock and temp-file-then-atomic-move writes. Missing, malformed, unreadable, or legacy-absent data must safely fall back to the Default appearance and an empty theme library.
10. FR10 — Theme endpoints, JSON contents, HTTP errors, UI messages, and normal application logs must never expose archive filesystem paths, root-relative paths, or EPUB internals.

## Non-Functional Requirements

- Reuse the server-owned JSON and test pattern of `EpubProgressService`; do not add a database, browser storage, authentication model, package, or external service.
- Keep server file access and validation in `WebApp`; keep presentation, transient form state, and HTTP interactions in `WebApp.Client`.
- Use the existing Bootstrap-first reader toolbar/dropdown, Bootstrap Icons, design tokens, visible focus states, keyboard-operable forms, live status/error feedback, and existing responsive panel behavior.
- A transient failed load/save must not prevent opening, navigating, or closing an EPUB; the UI should keep the active in-memory appearance and show concise feedback for user-initiated theme-library mutations.
- Use focused DTOs and an interface-backed service so xUnit unit tests can exercise JSON behavior without a browser and endpoint tests can exercise validation without physical paths.

## Out of Scope

- Per-book, per-category, per-user-account, or cross-installation theme synchronization.
- Persisting margins/text width, toolbar visibility, table-of-contents state, pagination position, highlights, notes, or the app-wide light/dark setting as part of a theme.
- Import/export, sharing, ordering, tags, preview thumbnails, or built-in named presets beyond Default.
- Arbitrary fonts, CSS declarations, gradients, images, or unsupported color syntaxes.

## Open Questions

None — global scope, full CRUD, and Default behavior were resolved during discovery.
