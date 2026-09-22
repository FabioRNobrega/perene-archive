namespace WebApp.Client.Models;

public sealed record ArchiveMutationJobDto(
    string JobId,
    ArchiveMutationKind Kind,
    ArchiveMutationJobState State,
    string Label,
    int TotalItems,
    int ProcessedItems,
    string? Diagnostic);
