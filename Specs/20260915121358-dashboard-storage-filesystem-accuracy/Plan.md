# Plan: Dashboard Storage Filesystem Accuracy

## Summary

Extend the existing `IStorageUsageService` implementation so capacity uses `df`-equivalent free-space semantics and throughput is limited to the archive mount's backing device rather than an aggregate of every host-visible disk.

## Technical Approach

`StorageUsageService` remains the sole server-side filesystem boundary used by `DashboardEndpoints`, `StorageEndpoints`, `HealthAggregationService`, and `MetricsHistoryBackgroundWorker`. It will keep `DriveInfo` for capacity, but calculate used bytes from `TotalFreeSpace`, which corresponds to filesystem free blocks and therefore does not treat reserved blocks as ordinary used capacity.

For throughput it will parse `/proc/self/mountinfo` to find the deepest mount covering the configured archive path, extract its major:minor device pair, then retain only that pair's sector counters from `/proc/diskstats`. The delta sampler remains singleton-owned and returns unavailable throughput when either Linux source is unavailable or the device cannot be matched. No device/mount metadata crosses the existing DTO boundary.

## Component Breakdown

**Existing files to modify:**

- `WebApp/WebApp/Services/StorageUsageService.cs` — use total free space for capacity and make diskstats parsing device-specific via mount metadata.
- `WebApp.Tests/Services/StorageUsageServiceTests.cs` — replace aggregate-only expectations with device-scoped parsing and mountinfo fixture tests.
- `README.md` — clarify the archive-root storage scope and NAS setup requirement.

**New files to create:**

- None required.

## Dependencies

- Linux `/proc/self/mountinfo` and `/proc/diskstats`, normally available in the project's Docker runtime. Their absence is handled as unavailable throughput.

## External / Vendor Documentation Evidence

Not applicable. This change relies on Linux procfs formats; no Microsoft Learn MCP tool was available in this session to verify a Microsoft-specific API decision.

## Flow

```mermaid
flowchart LR
    A[ArchiveRoot:Path /archive] --> B[DriveInfo capacity]
    A --> C[/proc/self/mountinfo]
    C --> D[major:minor device]
    D --> E[/proc/diskstats matching row]
    E --> F[StorageUsageService delta]
    B --> G[/api/dashboard/storage]
    F --> G
    G --> H[DashboardStorageCard]
```

## Risk Assessment

| Risk | Mitigation |
| --- | --- |
| Container hides the backing block device | Return unavailable throughput; do not substitute unrelated aggregate I/O. |
| NAS archive root is mounted from the wrong host filesystem | Document that `PERENE_ARCHIVE_ROOT` must name a directory on the intended filesystem; the app cannot observe an unmounted host volume. |
| Reserved filesystem blocks alter usage | Use `TotalFreeSpace` to align displayed used capacity with `df`. |
