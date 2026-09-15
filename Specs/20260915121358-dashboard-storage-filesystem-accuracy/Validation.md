# Validation: Dashboard Storage Filesystem Accuracy

## Acceptance Criteria

| Requirement | Acceptance Criterion |
| --- | --- |
| FR1 | `GET /api/dashboard/storage` reports the archive filesystem's used/total capacity using the same used-space convention as `df`. |
| FR2 | A diskstats fixture containing several devices returns counters for only the requested major:minor device. |
| FR3 | Missing mount metadata or a non-matching diskstats device returns null throughput and a successful endpoint response. |
| FR4 | xUnit tests cover exact-device selection and malformed/unresolved inputs. |
| FR5 | README explains archive-root scope and the private NAS configuration requirement. |

## Test Cases

- `WebApp.Tests/Services/StorageUsageServiceTests.cs` — parsing fixture tests for a mounted partition and unrelated device rows, plus invalid mountinfo/diskstats fallbacks.
- `WebApp.Tests/Endpoints/DashboardEndpointsTests.cs` — existing endpoint smoke test remains green with unavailable throughput permitted.

## Manual Verification

1. On the NAS, set the ignored `.env` `PERENE_ARCHIVE_ROOT` to an archive directory on the intended mounted filesystem.
2. Run `make docker-run-bg` and compare `df -h "$PERENE_ARCHIVE_ROOT"` with the Dashboard Storage capacity.
3. Run `docker compose exec webapp findmnt -T /archive`; if a device is visible in the container, generate archive I/O and refresh twice to observe its throughput.
4. If no backing device is visible, confirm the card says `unavailable` for throughput rather than presenting aggregate values.
5. Run `make test`.

## Definition of Done

- All requirements are implemented and xUnit tests pass through `make test`.
- Dashboard retains explicit unavailable states and no filesystem path/device identifier is exposed.

## Rollback Plan

Revert `StorageUsageService.cs`, its tests, and the README change. This restores the previous archive-capacity and aggregate-diskstats behavior without data migration.
