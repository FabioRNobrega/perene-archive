# Requirements: Dashboard Storage Filesystem Accuracy

## Problem Statement

The Storage card currently derives used capacity as `TotalSize - AvailableFreeSpace` and sums every eligible entry in `/proc/diskstats`. The first calculation counts filesystem-reserved blocks as used rather than matching `df`; the second can double-count parent devices and partitions or describe an unrelated device. The dashboard must report the filesystem that backs the archive mount and clearly mark throughput unavailable when that device cannot be identified in the container.

## User Stories

- Given PereneArchive runs from an archive directory on an Ubuntu NAS or an Arch/SteamOS host, when I open the Dashboard, then capacity matches `df` for that archive directory.
- Given the archive mount maps to one visible Linux block device, when I refresh the Dashboard, then read and write throughput describe that device only.
- Given a container cannot identify the archive mount's block device, when I open the Dashboard, then throughput says unavailable instead of showing a misleading aggregate.

## Functional Requirements

1. FR1 — `StorageUsageService.GetUsage()` reports usage for the filesystem containing `ArchiveRoot:Path`, calculated with total free blocks so used space follows `df` semantics.
2. FR2 — `StorageUsageService.GetThroughput()` resolves the device backing `ArchiveRoot:Path` from Linux mount metadata and samples only its matching `/proc/diskstats` row.
3. FR3 — If the backing device cannot be resolved or is absent from `/proc/diskstats`, the storage endpoint returns null throughput values without failing capacity, health, history, or alerts.
4. FR4 — Storage parsing is covered with fixtures for a matching partition, unrelated devices, and an unresolved mount/device.
5. FR5 — The README describes that Dashboard Storage follows `PERENE_ARCHIVE_ROOT`, and that a NAS deployment must point this ignored setting at the desired mounted filesystem.

## Non-Functional Requirements

- No physical path, mount source, or device name is returned to the browser or logged.
- Linux pseudo-filesystem read failures remain non-fatal and use the existing unavailable-state behavior.
- The Docker-only Makefile test workflow remains unchanged.

## Out of Scope

- Monitoring a host filesystem that is not mounted into the application as the archive root.
- SMART health, RAID/ZFS pool telemetry, or Docker host-level device discovery outside the container namespace.

## Open Questions

- None. The archive root is the intentional dashboard storage scope; deployments that need `/dev/sdb1` must bind an archive directory located on that filesystem.
