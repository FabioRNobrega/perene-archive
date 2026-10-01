# Requirements: Folder Access Control

## Problem Statement

P1 introduces authenticated accounts but does not decide which archive content an ordinary user may see or change. P2 activates ordinary accounts only after the archive tree has a server-owned folder model and every media endpoint makes the same fail-closed authorization decision.

## User Stories

- Given `Books/MyLab` is Private, when a user only has Read on `Books`, then MyLab and all of its content appear nonexistent.
- Given a user has Create on a folder, when they create a subfolder, then they own that folder but cannot manage its permissions.
- Given an Admin grants a manager limited rights, when the manager edits permissions, then locked or escalation-prone changes are rejected.

## Functional Requirements

1. FR1 — Seed `FOLDER` records for archive category roots and cut/composition roots, storing server-only path/identity metadata, owner, parent, access mode, and status.
2. FR2 — Add singleton `ACCESS_POLICY` with Shared defaults: Read allowed; Create, Write, and Delete denied; maintain a monotonic global policy version. P1's per-user `ApplicationUser.AuthzVersion` (a concurrency token bumped by lifecycle, password, and TOTP changes) is kept unchanged; an access decision's cache key is the pair (global policy version, user `AuthzVersion`). Folder, permission, policy, and ownership changes bump the global version; user-scoped changes keep bumping the user's own version.
3. FR3 — Add unique per-folder/user `FOLDER_PERMISSION` rows with tri-state operation flags, enforcement, lock mask, concurrency stamp, and grantor.
4. FR4 — Implement one `FolderPermissionAuthorizationHandler` and pure resolver: Admin override, enforced deny, Private-ancestor gate, explicit row, ownership, inherited rows, access-mode default, Read gate, and compound-operation rules.
5. FR5 — Add `IFolderAccessService` (implemented by the scoped `FolderAccessService`) with readable-folder caching (`GetReadableAsync`/`CanReadAsync`) stamped by both versions from FR2, and always-fresh `CheckAsync` decisions that bypass the cache for destructive, rename/move/replace, permission, Admin, and job-execution operations. Consumers depend on the interface, not the concrete class.
6. FR6 — Bump the relevant version (FR2) in the same transaction as every authorization-relevant user, permission, folder, policy, or ownership change.
7. FR7 — Apply the operation matrix to all archive, video, cut, composition, thumbnail, preview, subtitle, cover, image, audio, download, range-stream, mutation, and worker paths: anonymous is 401, unreadable is 404, readable-but-forbidden is 403.
8. FR8 — Filter listings, search, counts, facets, and folder stubs by readable folders. Name-only ancestor stubs never cross a Private folder.
9. FR9 — Folder creation records creator ownership (Read/Write/Create/Delete but never Manage), inheriting mode unless Private is selected.
10. FR10 — Enforce delegation: only Admins grant Manage/enforcement/mode changes; grants cannot exceed grantor rights; locks cannot be loosened; managers cannot edit their own row; stale writes perform one reload/retry then conflict.
11. FR11 — Provide the folder-access editor described in "UI Design" below. It lives inside the Family page's "Family Members" tab (no separate route), is backed by `/api/account/users/{userName}/access` (extending P1's Admin-restricted group and antiforgery filter), and addresses users by `UserName` like every P1 Admin route; no Identity GUID reaches the browser. A future username-rename feature must move these routes to a stable identifier.
12. FR12 — Re-authorize all queued cut, composition, conversion, archive-mutation, and audio-track jobs at enqueue and execution. System thumbnail/preview/subtitle work is exempt.
13. FR13 — Extend P1's existing `AuditEvent` (`WebApp.Identity`, string-based actor/target fields, no foreign keys so rows survive user deletion) to record permission, owner-reassignment, and CSRF-rejection actions without secrets or paths. P1 already audits lifecycle, role, and reset actions; CSRF-rejection auditing does not exist yet and is new work here.
14. FR14 — Enforce configured cascade/SetNull/Restrict relationships. Folder owner uses Restrict as a backstop; P1's `AccountLifecycleService.DeleteAsync` (which already reassigns `CreatedByUserId` to the acting Admin in one transaction) is extended to reassign folder ownership in the same transaction. The Admin override is membership in P1's sole `Admin` role; a member is any user without it.

15. FR15 — Migration behavior: P1 already lets Admins create active members who can use the whole app. When P2 ships, existing members fall to the Shared defaults (Read only) until an Admin grants more. P2 therefore needs no "ordinary accounts active only when enforcement is registered" gate.

## UI Design

Visual reference: `Specs/20260827194328-perene-tech-design-system-refactor/design-guide-en.html` (colors, buttons, icon buttons, forms, cards, alerts, badges, tables). Bootstrap components and utilities first; no new custom CSS beyond what Bootstrap cannot express.

**Entry point.** `Family.razor` / `MembersTable.razor` (tab "Family Members"): add an icon-only action to each member's action group, using `bi bi-house-lock-fill`, `btn btn-outline-secondary`, a 40×40 target, `aria-label="Manage folder access for {userName}"`, and a Bootstrap tooltip with the text "Manage folders access" (the design guide requires a tooltip on icon-only buttons; reuse the existing `bootstrapInterop.js` tooltip lifecycle). Admin accounts get no button because the Admin override makes grants meaningless; show a muted "Full access" hint instead. The button is disabled while another action is busy, like its siblings.

**Same-tab view.** Selecting the button replaces the members list, inside the same tab and card, with the access editor (client state in `Family.razor`, e.g. `_accessUser`; no route change, no new tab). The Personal and Security tabs are untouched. Header: a back control (`btn btn-outline-secondary`, `bi-arrow-left`, "Family members"), the heading "Folder access" with the member's display name and username, and the current account-state badges (owner, inert, suspended) reused from the members list.

**Folder tree.** A Bootstrap `table table-sm align-middle` inside `.table-responsive` (the guide's token-table style): one row per folder the Admin can see, indented by depth with `ps-*` utilities and a `bi-folder`/`bi-lock-fill` icon (the latter for Private folders), a collapsible expander per branch (`bi-chevron-right`/`-down`, `aria-expanded`). Columns: Folder, then one column per operation: Read, Write, Create, Delete (Manage appears only when the Admin has the delegation right, FR10). Folder names shown are the server-defined folder labels; never paths.

**Checkbox cells.** Each cell is a real Bootstrap `form-check-input` checkbox with an accessible name ("Read on Books for alice"). Checked = explicit Allow on that folder. Unchecked = nothing set there; the cell then shows the effective inherited result as a muted adjacent state icon (`bi-check2` or `bi-dash`) with a tooltip giving the provenance ("Inherited from Books", "Owner", "Shared default", "Private: no access"). Explicit Deny is a second, compact `btn-check` toggle (`btn-outline-danger`, `bi-slash-circle`, tooltip "Deny") per cell, mutually exclusive with Allow; state is never conveyed by color alone. Cells locked by the lock mask or enforcement are `disabled` with a `bi-lock-fill` cue and tooltip. Owned folders show an "Owner" badge. A checked parent row offers "Apply to subfolders" through a small `dropdown`, never as an automatic cascade.

**Save and return.** A sticky card footer holds `btn btn-primary` "Save access" (the single principal action, gold) and `btn btn-outline-secondary` "Cancel". Save posts only the changed cells with the folder rows' concurrency stamps, disables the form while pending, and on success returns to the members list with a `alert-success` status ("Folder access saved for {userName}"). Cancel and the back control with unsaved edits ask for confirmation in an inline `alert-warning` (not a browser dialog). Nothing is applied until Save.

**States.**
- Loading: `spinner-border` with `role="status"`; empty tree: neutral message.
- Conflict (stale stamp after the one reload/retry, FR10): `alert-warning` with a "Reload" button; the user's pending edits are kept visible.
- Rejected change (lock, escalation, self-edit): `alert-danger` at the top, `role="alert"`, naming the folder and operation without paths.
- Private impact preview: before saving a change to Private or removing a Read that makes content disappear, an inline `alert-info` card lists how many items/folders and which users lose visibility (counts and folder labels only).
- Owner-vs-deny workflow: when the edit would deny the folder's owner or grant over an owner's row, an inline confirm card explains the precedence and offers "Keep owner access" or "Deny anyway"; no silent resolution.
- Reset actions (reset to inherited, clear row) are `btn-outline-secondary` items in the row dropdown with confirmation.
- Responsive: the table scrolls horizontally on narrow screens with the Folder column sticky-left; footer buttons stack with Bootstrap flex utilities. Dark and Kindle-paper light tokens apply via the existing theme; focus is moved to the editor heading on open and back to the originating row button on return.

## Non-Functional Requirements

- P1 authentication and opaque-ID boundaries remain intact; access decisions are never delegated to WASM UI.
- Ambiguity fails closed; no filesystem path appears in browser DTOs or normal logs.
- Use existing Bootstrap-first, responsive, accessible component conventions; the UI Design section above is binding.

## Out of Scope

- Per-user notes/media reconciliation (P3), durable jobs/counters (P4), and journalled filesystem recovery (P5).

## Open Questions

- None; inheritance, Private, and delegation rules are fixed by the umbrella ERD.
