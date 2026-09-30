# Requirements: Folder Access Control

## Problem Statement

P1 introduces authenticated accounts but does not decide which archive content an ordinary user may see or change. P2 activates ordinary accounts only after the archive tree has a server-owned folder model and every media endpoint makes the same fail-closed authorization decision.

## User Stories

- Given `Books/MyLab` is Private, when a user only has Read on `Books`, then MyLab and all of its content appear nonexistent.
- Given a user has Create on a folder, when they create a subfolder, then they own that folder but cannot manage its permissions.
- Given an Admin grants a manager limited rights, when the manager edits permissions, then locked or escalation-prone changes are rejected.

## Functional Requirements

1. FR1 — Seed `FOLDER` records for archive category roots and cut/composition roots, storing server-only path/identity metadata, owner, parent, access mode, and status.
2. FR2 — Add singleton `ACCESS_POLICY` with Shared defaults: Read allowed; Create, Write, and Delete denied; maintain a monotonic `AuthzVersion`.
3. FR3 — Add unique per-folder/user `FOLDER_PERMISSION` rows with tri-state operation flags, enforcement, lock mask, concurrency stamp, and grantor.
4. FR4 — Implement one `FolderPermissionAuthorizationHandler` and pure resolver: Admin override, enforced deny, Private-ancestor gate, explicit row, ownership, inherited rows, access-mode default, Read gate, and compound-operation rules.
5. FR5 — Add `IFolderAccessService` with version-stamped readable-folder caching and fresh checks for destructive, rename/move/replace, permission, Admin, and job-execution operations.
6. FR6 — Bump `AuthzVersion` in the same transaction as every authorization-relevant user, permission, folder, policy, or ownership change.
7. FR7 — Apply the operation matrix to all archive, video, cut, composition, thumbnail, preview, subtitle, cover, image, audio, download, range-stream, mutation, and worker paths: anonymous is 401, unreadable is 404, readable-but-forbidden is 403.
8. FR8 — Filter listings, search, counts, facets, and folder stubs by readable folders. Name-only ancestor stubs never cross a Private folder.
9. FR9 — Folder creation records creator ownership (Read/Write/Create/Delete but never Manage), inheriting mode unless Private is selected.
10. FR10 — Enforce delegation: only Admins grant Manage/enforcement/mode changes; grants cannot exceed grantor rights; locks cannot be loosened; managers cannot edit their own row; stale writes perform one reload/retry then conflict.
11. FR11 — Provide `/admin/users/{id}/access` with permission cells, provenance, owner/inert/suspended states, reset actions, Private impact preview, and the owner-vs-deny decision workflow.
12. FR12 — Re-authorize all queued cut, composition, conversion, archive-mutation, and audio-track jobs at enqueue and execution. System thumbnail/preview/subtitle work is exempt.
13. FR13 — Audit all permission, role, lifecycle, owner-reassignment, reset, and CSRF-rejection actions without secrets or paths.
14. FR14 — Enforce configured cascade/SetNull/Restrict relationships, including restricting deletion of a user who owns folders.

## Non-Functional Requirements

- P1 authentication and opaque-ID boundaries remain intact; access decisions are never delegated to WASM UI.
- Ambiguity fails closed; no filesystem path appears in browser DTOs or normal logs.
- Use existing Bootstrap-first, responsive, accessible component conventions.

## Out of Scope

- Per-user notes/media reconciliation (P3), durable jobs/counters (P4), and journalled filesystem recovery (P5).

## Open Questions

- None; inheritance, Private, and delegation rules are fixed by the umbrella ERD.
