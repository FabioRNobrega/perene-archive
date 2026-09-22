using WebApp.Client.Models;

namespace WebApp.Models;

internal sealed record ArchiveMutationJobStatus(
    string JobId,
    ArchiveMutationKind Kind,
    ArchiveMutationJobState State,
    int TotalItems,
    int ProcessedItems,
    string Label,
    string? Diagnostic = null);
