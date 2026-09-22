using WebApp.Client.Models;

namespace WebApp.Models;

/// <summary>
/// Server-only descriptor for a queued Move/MoveToTrash/EmptyTrash/BatchMove job. <see cref="DestinationPath"/> is
/// null only for <see cref="ArchiveMutationKind.EmptyTrash"/>. Never serialized to the browser.
/// </summary>
internal sealed record ArchiveMutationJob(
    string JobId,
    ArchiveMutationKind Kind,
    string SourcePath,
    string? DestinationPath,
    bool IsFolder,
    int TotalItems,
    string Label,
    IReadOnlyList<ArchiveMutationBatchEntry>? BatchEntries = null);

/// <summary>
/// One planned move within a <see cref="ArchiveMutationKind.BatchMove"/> job, produced by
/// <c>ArchiveService.BatchMove</c>'s per-item validation and consumed by
/// <c>ArchiveMutationExecutor.BatchMoveAsync</c>.
/// </summary>
internal sealed record ArchiveMutationBatchEntry(string SourcePath, string DestinationPath, bool IsFolder, int FileCount);
