# Validation: Folder Access Control

## Acceptance Criteria

- Folder seeding, Shared Read-only defaults, permission uniqueness, lock masks, and global and per-user `AuthzVersion` transactions work on real SQLite files.
- Table-driven resolver tests cover every ERD operation and Private/owner/enforced-deny case.
- Every protected endpoint and range request returns 401/404/403 according to authentication, Read, and operation permission.
- Readable-set filtering excludes inaccessible content and only emits legal pass-through stubs.
- Permission delegation, optimistic concurrency, audit writes, user-deletion constraints, and folder-creation ownership are enforced server-side.
- Jobs re-check access freshly before execution; existing members drop to Shared Read-only defaults on upgrade; deleting a folder-owning user reassigns ownership to the acting Admin.

## Automated Tests

- Client tests for `FolderAccessEditor` state: Allow/Deny/unset round-trip to DTOs, only changed cells are submitted, unsaved-edit guard, and the members-list action's visibility rules.

- `FolderPermissionResolverTests`, `FolderAccessServiceTests`, `DelegationRulesTests`, and `PermissionWriteServiceTests` (all under `WebApp.Tests/Authorization/`, alongside `EndpointAuthorizationMatrixTests`, `PathLeakTests`, and `FolderAccessEndpointTests`).
- `EndpointAuthorizationMatrixTests`, `AdminAccessEndpointsTests` (routes addressed by `userName`), and `PathLeakTests` with a real-Identity `WebApplicationFactory` that can sign in as a seeded member (not only the fake Admin).
- Migration/cascade tests in `AppDbContextTests`; execution re-authorization tests in worker/job tests.
- Run `make test`.

## Manual Verification

1. As Admin, grant a user Write but not Create on `Books`; confirm the expected operation matrix.
2. Mark `Books/MyLab` Private, verify the inherited user receives 404, then grant Read and inspect provenance.
3. Create a child folder as a Create-capable user and verify ownership without Manage.
4. In Family → Family Members, confirm each non-Admin member shows the house-lock icon button with the tooltip "Manage folders access" and an accessible name; Admin rows show no button.
5. Open the editor: it replaces the list in the same tab, shows the folder tree with Read/Write/Create/Delete checkboxes, provenance tooltips, Private lock icons, and owner badges.
6. Change cells, Cancel/back (confirm prompt appears with unsaved edits), then repeat and Save: the list returns with a success alert and the change is enforced on the member's next request.
7. Test lock, conflict, rejected-escalation, owner-vs-deny, and Private-impact states in dark/light themes, narrow layout, keyboard-only, and with the CDN unavailable (controls remain usable).

## Definition of Done

- All requirements and tests pass; permissions are enforced on every server path, not merely hidden in the UI.
- No physical/root-relative path or Identity/database ID reaches browser DTOs or normal logs.
- P3–P5 remain out of scope.
