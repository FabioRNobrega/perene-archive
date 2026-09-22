using WebApp.Client.Models;

namespace WebApp.Models;

/// <summary>
/// Server-only descriptor for a queued Move/MoveToTrash/EmptyTrash job. <see cref="DestinationPath"/> is
/// null only for <see cref="ArchiveMutationKind.EmptyTrash"/>. Never serialized to the browser.
/// </summary>
internal sealed record ArchiveMutationJob(
    string JobId,
    ArchiveMutationKind Kind,
    string SourcePath,
    string? DestinationPath,
    bool IsFolder,
    int TotalItems,
    string Label);
