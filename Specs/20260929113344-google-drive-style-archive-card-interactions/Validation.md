# Validation: Google Drive-Style Archive Card Interactions

## Table of Contents

- [Acceptance Criteria](#acceptance-criteria)
- [Test Cases](#test-cases)
- [Manual Verification](#manual-verification)
- [Definition of Done](#definition-of-done)
- [Rollback Plan](#rollback-plan)

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | A plain click/tap on an unselected card leaves exactly that opaque item ID selected and shows the selected-items toolbar. |
| FR2 | A plain click/tap on a selected card removes it; no toolbar remains when it was the final selection. |
| FR3 | Ctrl+click toggles individual IDs and Shift+click selects the inclusive visible range without activating folders/files. |
| FR4 | Pressing the options button opens its card's actions and leaves selection IDs and toolbar unchanged. |
| FR5 | Right-click suppresses the native menu and opens only that card's actions without changing selection. |
| FR6 | Double-click/double-tap opens folders and sends each supported file type through its existing activation path. |
| FR7 | Blank grid space clears selection; toolbar, menu, dialog, breadcrumb, search, and upload interactions do not change selection unless their existing behavior explicitly does so. |
| FR8 | Card/options controls remain keyboard operable with accessible names, correct pressed/selected state, visible focus, and usable menu focus/dismissal. |
| FR9 | Drag/drop, current menu commands, filtering/sorting, and opaque-ID boundary continue to work with no new API or server behavior. |

## Test Cases

**Unit tests:**

- `WebApp.Tests/Client/ArchiveCardSelectionRulesTests.cs` (only if a pure helper is added) — cover unselected single selection, selected-item toggle-off, Ctrl toggle, Shift range, and isolation of an options/right-click action from selection mutation.
- `WebApp.Tests/Endpoints/ArchiveEndpointsTests.cs` — extend the existing `Archive_browser_markup_uses_unified_dropdown_cards_and_keeps_video_grid_specialized` test to assert the card has distinct single-click, double-click, options, and context-menu wiring plus propagation prevention; retain existing unified-card assertions.

**Integration tests:**

- No server integration test is expected because this is client-only. Run the complete existing isolated suite with `make test` to ensure archive endpoints and browser source tests remain green.

## Manual Verification

1. Start the application with `make docker-run`, open an Archive Browser category with at least three folders/files, and confirm no item is selected initially.
2. Single-click/tap one card. Confirm it is visibly selected and the toolbar reports one selected item; single-click/tap it again and confirm selection clears.
3. Single-click card A, Ctrl+click B, and Shift+click a later card. Confirm Ctrl toggles only B and Shift produces the expected filtered-order range; confirm no item opens.
4. With one and then multiple cards selected, click/tap the options button on a selected and an unselected card. Confirm only the corresponding menu opens and the selection/count does not change.
5. With a multi-selection active, right-click a selected card and then an unselected card. Confirm the native context menu never appears, the action menu is scoped to the clicked item, and the multi-selection remains unchanged in both cases.
6. Double-click a folder and a representative video, image, text file, and any enabled book/comic/music type. Confirm each retains the existing open/view/play behavior; repeat a representative case by double-tap on a touch device or browser device emulation.
7. Click/tap blank grid space and confirm selection clears. Repeat using the toolbar, menu, search, breadcrumbs, and an open dialog to confirm none creates an unintended selection change.
8. Verify keyboard focus, Enter/Space selection behavior, options-menu focus/dismissal, light/dark themes, and the 40px options target. Repeat drag/drop move on a selected card.
9. Run `make test` and record its result.

## Definition of Done

- Requirements, plan, and validation documents are present in this spec folder.
- The card interaction behavior satisfies FR1–FR9 in desktop and touch verification.
- Existing tests and any new focused regression tests pass through `make test`.
- `ArchiveBrowser.razor` and any narrowly required scoped CSS follow the Bootstrap/design-system and accessibility constraints.
- `README.md`'s Current Supported Features table describes the updated Archive Browser behavior.
- The official Blazor event-directive documentation evidence in `Plan.md` is verified or its pending status is resolved before implementation.

## Rollback Plan

Revert the `ArchiveBrowser.razor` event-handler and selection-state changes (and any matching CSS/tests/README entry) in the implementation change. No migration, setting, server state, endpoint, or cached data is involved; restoring the prior component code restores the prior interaction model.
