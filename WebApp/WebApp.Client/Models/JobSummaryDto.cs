namespace WebApp.Client.Models;

/// <summary>
/// Browser-safe summary of a non-conversion job (composition, archive move/trash, cut) for the Jobs page. <paramref name="Kind"/> is
/// "Composition", "ArchiveMutation" or "Cut"; <paramref name="State"/> is the job's state name. Never carries a path or an account ID.
/// </summary>
public sealed record JobSummaryDto(
    string JobId,
    string Kind,
    string Title,
    string State,
    string? Detail,
    int? ProcessedItems,
    int? TotalItems,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? FinishedUtc,
    string? StartedBy);
