# Requirements: Durable Jobs and Naming Counters

## Problem Statement

Composition, archive-mutation, and conversion status stores are process-local, while output names are allocated by scanning directories. Restarts lose job state and concurrent workers can race. P4 makes status durable without exposing paths or stale browser snapshot IDs.

## Functional Requirements

1. FR1 — Add a `JOB` table for Composition, ArchiveMutation, VideoConversion, and Cut jobs, with durable status, user, type, and safe JSON payload.
2. FR2 — Payloads store stable `MediaItemId`/`FolderId` and captured identity values, never filesystem paths or snapshot IDs.
3. FR3 — Reimplement the existing composition/archive-mutation/conversion status-store interfaces against SQLite so endpoint and UI shapes remain compatible.
4. FR4 — On startup, mark unsafe non-terminal persisted jobs as generically interrupted/failed rather than letting them vanish or replaying without checks.
5. FR5 — Resolve snapshot IDs to stable identity at enqueue and re-check Active state, captured identity, and fresh P2 authorization at execution; failures are generic.
6. FR6 — Replace cut, composition, image-crop, and video-conversion directory scans with a `NamingCounters` atomic allocator, seeded from existing files on first use.
7. FR7 — Preserve output naming formats and ensure concurrent allocations are unique, consecutive, and never regress after deletion.

## Non-Functional Requirements

- Keep workers, FFmpeg authority, opaque IDs, and browser DTO contracts unchanged except for durable status behavior.
- Use short SQLite transactions and real-file concurrency tests; logs and payloads contain no physical or relative paths.

## Out of Scope

- Filesystem mutation journaling/recovery (P5).

## Open Questions

- None.
