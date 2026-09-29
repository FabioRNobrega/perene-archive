# Requirements: Google Drive-Style Archive Card Interactions

## Table of Contents

- [Problem Statement](#problem-statement)
- [User Stories](#user-stories)
- [Functional Requirements](#functional-requirements)
- [Non-Functional Requirements](#non-functional-requirements)
- [Out of Scope](#out-of-scope)
- [Open Questions](#open-questions)

## Problem Statement

`ArchiveBrowser.razor` currently treats an ordinary card click as activation and its card-level click handling is reached when the card's options control is used. This makes a user select or activate an item unintentionally while trying to open only its action menu. The Archive Browser needs a predictable file-manager interaction model: selection is separate from activation and item options.

## User Stories

- Given I click or tap an archive card, when it is not selected, then it becomes the sole selection and the selection toolbar appears.
- Given I click or tap the only selected card, then it is deselected and the selection toolbar disappears.
- Given I use a card's options button or right-click its card, then only that card's options menu opens and selection is unchanged.
- Given I Ctrl+click or Shift+click cards, then I can build or replace a multi-selection without activating any item.
- Given I double-click or double-tap a card, then its folder or file opens using the current activation behavior.

## Functional Requirements

1. FR1 — A plain primary click or single touch on an unselected `ArchiveBrowser.razor` card selects that item as the sole entry in `_selectedForMoveIds`, establishes `_selectionAnchorId`, and exposes the existing selected-items toolbar.
2. FR2 — A plain primary click or single touch on a selected card removes it from `_selectedForMoveIds`; the toolbar disappears when that leaves no selected items.
3. FR3 — Ctrl+click retains the existing add/remove multi-selection behavior; Shift+click selects the existing inclusive filtered-item range. Neither gesture activates a card.
4. FR4 — The per-card options button opens its existing `_contextMenu` for that item only and does not select, deselect, activate, or otherwise change the current multi-selection.
5. FR5 — Right-click on a card suppresses the native browser menu and opens the existing options menu for that card only, without selecting it or changing the current multi-selection.
6. FR6 — A double-click on desktop, and an intentional double-tap on touch-capable devices, invokes the existing `ActivateAsync` behavior: folders navigate; files retain their existing player/viewer/editor behavior.
7. FR7 — A click/tap on empty Archive Browser grid space clears selection, while clicking/tapping controls within the selection toolbar, action menu, dialogs, breadcrumbs, search, and uploads does not cause unintended card selection changes.
8. FR8 — Card action-menu accessibility remains intact: the options control retains its accessible name, menu semantics/focus handling remain available, and normal keyboard activation of the card continues to select rather than inadvertently opening it.
9. FR9 — Existing drag-and-drop move, context-menu actions, action-menu dismissal, filtering/sorting, loading/empty/error states, and opaque-ID-only browser boundary remain unchanged except where event propagation must be narrowed to meet FR1–FR7.

## Non-Functional Requirements

- Reuse the client-owned Interactive WebAssembly component and existing selection/context-menu state; no server endpoint, DTO, persistence, filesystem, or FFmpeg change is allowed.
- Use Blazor event directives and Bootstrap controls already present. No new package, framework, or broad JavaScript interaction layer is introduced.
- Preserve a minimum 40×40 CSS-pixel options target, keyboard focus visibility, dark/light theme behavior, and touch usability.
- Continue to expose only opaque archive item IDs; do not log or surface filesystem paths.

## Out of Scope

- Changing the commands offered in the options menu or converting it into a multi-selection menu.
- Right-clicking empty grid space, long-press context menus, keyboard range navigation, or drag selection.
- Altering selection toolbar operations, server-side archive mutation semantics, or file/folder activation destinations.
- A visual redesign beyond indicators needed to distinguish selected cards.

## Open Questions

- None. The interaction contract was confirmed: a selected card toggles off with a plain click/tap, right-click always targets only that card, and touch uses single-tap selection with double-tap activation.
