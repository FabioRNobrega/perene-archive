# Validation: Folder Access Control

## Acceptance Criteria

- Folder seeding, Shared Read-only defaults, permission uniqueness, lock masks, and `AuthzVersion` transactions work on real SQLite files.
- Table-driven resolver tests cover every ERD operation and Private/owner/enforced-deny case.
- Every protected endpoint and range request returns 401/404/403 according to authentication, Read, and operation permission.
- Readable-set filtering excludes inaccessible content and only emits legal pass-through stubs.
- Permission delegation, optimistic concurrency, audit writes, user-deletion constraints, and folder-creation ownership are enforced server-side.
- Jobs re-check access freshly before execution; ordinary accounts can become active only when this P2 enforcement is registered.

## Automated Tests

- `FolderPermissionResolverTests`, `FolderAccessServiceTests`, `DelegationRulesTests`, and `PermissionWriteServiceTests`.
- `EndpointAuthorizationMatrixTests`, `AdminAccessEndpointsTests`, and `PathLeakTests` with an authenticated `WebApplicationFactory`.
- Migration/cascade tests in `AppDbContextTests`; execution re-authorization tests in worker/job tests.
- Run `make test`.

## Manual Verification

1. As Admin, grant a user Write but not Create on `Books`; confirm the expected operation matrix.
2. Mark `Books/MyLab` Private, verify the inherited user receives 404, then grant Read and inspect provenance.
3. Create a child folder as a Create-capable user and verify ownership without Manage.
4. Test lock, conflict, owner-vs-deny, and Private-impact UI states in dark/light and narrow layouts.

## Definition of Done

- All requirements and tests pass; permissions are enforced on every server path, not merely hidden in the UI.
- No physical/root-relative path or database ID reaches browser DTOs or normal logs.
- P3–P5 remain out of scope.
