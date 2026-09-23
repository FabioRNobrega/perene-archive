using WebApp.Client.Models;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>
/// Performs the actual Move/MoveToTrash/EmptyTrash filesystem work for a queued
/// <see cref="ArchiveMutationJob"/>, file-by-file, so <see cref="ArchiveMutationBackgroundWorker"/> can
/// observe real incremental progress instead of one opaque <c>Directory.Move</c>/<c>Directory.Delete</c>
/// call. Moving a folder walks its files explicitly (copy-then-delete per file via <see cref="File.Move"/>)
/// rather than relying on <c>Directory.Move</c>'s internal rename, which also removes the previously
/// implicit reliance on same-filesystem renames succeeding across a cross-device destination.
/// </summary>
internal sealed class ArchiveMutationExecutor : IArchiveMutationExecutor
{
    public Task<ArchiveMutationResult> MoveAsync(ArchiveMutationJob job, Action<int> reportProgress, CancellationToken cancellationToken) =>
        MoveEntryAsync(job, reportProgress, cancellationToken);

    public Task<ArchiveMutationResult> MoveToTrashAsync(ArchiveMutationJob job, Action<int> reportProgress, CancellationToken cancellationToken) =>
        MoveEntryAsync(job, reportProgress, cancellationToken);

    public Task<ArchiveMutationResult> EmptyTrashAsync(ArchiveMutationJob job, Action<int> reportProgress, CancellationToken cancellationToken)
    {
        var processed = 0;
        void ReportFileDeleted()
        {
            processed++;
            reportProgress(processed);
        }

        try
        {
            DeleteFolderContentsRecursive(job.SourcePath, cancellationToken, ReportFileDeleted);
            return Task.FromResult(ArchiveMutationResult.Success);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(ArchiveMutationResult.Failed(
                $"Trash could not be fully emptied. {processed} of {job.TotalItems} item(s) were removed before the failure. No rollback was performed."));
        }
    }

    public Task<ArchiveMutationResult> BatchMoveAsync(ArchiveMutationJob job, Action<int> reportProgress, CancellationToken cancellationToken)
    {
        var entries = job.BatchEntries ?? [];
        var processed = 0;
        void ReportFileMoved()
        {
            processed++;
            reportProgress(processed);
        }

        try
        {
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (PathsAreEqual(entry.SourcePath, entry.DestinationPath))
                {
                    for (var index = 0; index < entry.FileCount; index++)
                    {
                        ReportFileMoved();
                    }

                    continue;
                }

                if (entry.IsFolder)
                {
                    MoveFolderRecursive(entry.SourcePath, entry.DestinationPath, cancellationToken, ReportFileMoved);
                    Directory.Delete(entry.SourcePath, recursive: false);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(entry.DestinationPath)!);
                    File.Move(entry.SourcePath, entry.DestinationPath);
                    ReportFileMoved();
                }
            }

            return Task.FromResult(ArchiveMutationResult.Success);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(ArchiveMutationResult.Failed(
                $"The batch move{(job.Kind == ArchiveMutationKind.BatchMoveToTrash ? " to Trash" : string.Empty)} stopped after moving {processed} of {job.TotalItems} item(s). Already-moved items were not rolled back."));
        }
    }

    private static Task<ArchiveMutationResult> MoveEntryAsync(ArchiveMutationJob job, Action<int> reportProgress, CancellationToken cancellationToken)
    {
        if (job.DestinationPath is null)
        {
            return Task.FromResult(ArchiveMutationResult.Failed("A destination path is required."));
        }

        if (PathsAreEqual(job.SourcePath, job.DestinationPath))
        {
            reportProgress(job.TotalItems);
            return Task.FromResult(ArchiveMutationResult.Success);
        }

        var processed = 0;
        void ReportFileMoved()
        {
            processed++;
            reportProgress(processed);
        }

        try
        {
            if (job.IsFolder)
            {
                MoveFolderRecursive(job.SourcePath, job.DestinationPath, cancellationToken, ReportFileMoved);
                Directory.Delete(job.SourcePath, recursive: false);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(job.DestinationPath)!);
                File.Move(job.SourcePath, job.DestinationPath);
                ReportFileMoved();
            }

            return Task.FromResult(ArchiveMutationResult.Success);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(ArchiveMutationResult.Failed(
                $"The operation stopped after moving {processed} of {job.TotalItems} item(s). Already-moved items were not rolled back."));
        }
    }

    private static void MoveFolderRecursive(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken, Action onFileMoved)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destinationDirectory);

        foreach (var subdirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            MoveFolderRecursive(
                subdirectory,
                Path.Combine(destinationDirectory, Path.GetFileName(subdirectory)),
                cancellationToken,
                onFileMoved);
            Directory.Delete(subdirectory, recursive: false);
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(file, Path.Combine(destinationDirectory, Path.GetFileName(file)));
            onFileMoved();
        }
    }

    private static void DeleteFolderContentsRecursive(string directory, CancellationToken cancellationToken, Action onFileDeleted)
    {
        foreach (var subdirectory in Directory.EnumerateDirectories(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteFolderContentsRecursive(subdirectory, cancellationToken, onFileDeleted);
            Directory.Delete(subdirectory, recursive: false);
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(file);
            onFileDeleted();
        }
    }

    private static bool PathsAreEqual(string first, string second)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), comparison);
    }
}
