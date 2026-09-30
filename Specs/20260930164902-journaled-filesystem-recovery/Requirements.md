# Requirements: Journaled Filesystem Recovery

## Problem Statement

Current archive mutations directly move/delete filesystem entries and cannot prove the intended state after a crash, concurrent NAS change, or cross-volume operation. P5 makes every app-initiated mutation recoverable, conservative, and reviewable.

## Functional Requirements

1. FR1 — Before app-initiated move, rename, delete, replace, or create touches disk, persist an `FS_OPERATION` plan with server-only identities, paths, destination/staging evidence, and planned `AuthzVersion`; mark affected folders Busy.
2. FR2 — Revalidate object identity and authorization immediately before each disk step; use no-clobber publish and move deleted/replaced content to per-volume trash.
3. FR3 — Recover non-terminal operations at startup and periodically using explicit move/rename, delete, replace, and create tables; touch only evidence-matching objects and otherwise set `NeedsReview`.
4. FR4 — Purge only recorded committed trash objects after retention and identity recheck.
5. FR5 — Reconcile external folder changes with conservative identity signals; private/enforced/explicit-permission subtrees require Admin confirmation and ambiguous/unmatched candidates remain Private/NeedsReview.
6. FR6 — Provide `/admin/folders/review` for missing/review folders, media, and operations, with evidence, effect preview, approved resolution actions, and audit entries.
7. FR7 — Recovery/reconciliation never changes folder permissions or ownership; journal fields/paths never reach browser DTOs or normal logs.
8. FR8 — Support same-volume rename and the separate cross-volume protocol: destination staging copy, hash verification, no-clobber publish, DB commit, then source-volume trash. Partial/unverified staging is cleaned only after identity recheck and source remains intact.
9. FR9 — When filesystem IDs are unavailable, use null IDs and require `NeedsReview` for folder recovery lacking independent evidence.

## Non-Functional Requirements

- P2 fresh authorization and P4 durable jobs remain required before filesystem mutation.
- Crash injection and real filesystem fixtures prove no data loss; release requires same- and cross-volume recovery validation.

## Out of Scope

- Automatic guessing, unlinking unknown objects, or recovery that weakens Private/explicit permissions.

## Open Questions

- ⚠️ TODO: Confirm whether CI can mount two real filesystems; otherwise use an injectable volume probe plus manual two-volume validation.
