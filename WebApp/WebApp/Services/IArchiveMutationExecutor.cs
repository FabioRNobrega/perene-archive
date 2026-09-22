using WebApp.Models;

namespace WebApp.Services;

internal enum ArchiveMutationOutcome
{
    Success,
    Failed
}

internal sealed record ArchiveMutationResult(ArchiveMutationOutcome Outcome, string? Diagnostic = null)
{
    public static readonly ArchiveMutationResult Success = new(ArchiveMutationOutcome.Success);

    public static ArchiveMutationResult Failed(string diagnostic) => new(ArchiveMutationOutcome.Failed, diagnostic);
}

/// <summary>
/// The only place that performs the actual filesystem Move/Delete work for a queued
/// <see cref="ArchiveMutationJob"/>. <paramref name="reportProgress"/>-style callbacks report the
/// number of files processed so far (never bytes), matching FR6/FR7 of the archive move/delete
/// progress spec.
/// </summary>
internal interface IArchiveMutationExecutor
{
    Task<ArchiveMutationResult> MoveAsync(ArchiveMutationJob job, Action<int> reportProgress, CancellationToken cancellationToken);

    Task<ArchiveMutationResult> MoveToTrashAsync(ArchiveMutationJob job, Action<int> reportProgress, CancellationToken cancellationToken);

    Task<ArchiveMutationResult> EmptyTrashAsync(ArchiveMutationJob job, Action<int> reportProgress, CancellationToken cancellationToken);

    /// <summary>
    /// Moves every entry in <see cref="ArchiveMutationJob.BatchEntries"/> in order, reporting a
    /// running total across the whole batch (not reset per entry). Stops at the first failing entry.
    /// </summary>
    Task<ArchiveMutationResult> BatchMoveAsync(ArchiveMutationJob job, Action<int> reportProgress, CancellationToken cancellationToken);
}
