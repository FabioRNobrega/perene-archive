using WebApp.Authorization;
using WebApp.Client.Models;
using WebApp.Models;

namespace WebApp.Services;

internal sealed class ArchiveMutationBackgroundWorker(
    IArchiveMutationJobQueue queue,
    IArchiveMutationExecutor executor,
    IArchiveMutationJobStatusStore statusStore,
    IFolderJobAuthorizer jobAuthorizer,
    IFolderPathSync folderSync,
    ILogger<ArchiveMutationBackgroundWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ArchiveMutationJob job;
            try
            {
                job = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            statusStore.MarkProcessing(job.JobId);
            logger.LogInformation("Archive mutation job {JobId} ({Kind}) started.", job.JobId, job.Kind);

            try
            {
                if (!await jobAuthorizer.CanRunAsync(job, stoppingToken))
                {
                    statusStore.MarkFailed(job.JobId, "You no longer have access to complete this operation.");
                    logger.LogWarning("Archive mutation job {JobId} skipped: the requester no longer has access.", job.JobId);
                    continue;
                }

                var result = job.Kind switch
                {
                    ArchiveMutationKind.Move =>
                        await executor.MoveAsync(job, processed => statusStore.ReportProgress(job.JobId, processed), stoppingToken),
                    ArchiveMutationKind.MoveToTrash =>
                        await executor.MoveToTrashAsync(job, processed => statusStore.ReportProgress(job.JobId, processed), stoppingToken),
                    ArchiveMutationKind.EmptyTrash =>
                        await executor.EmptyTrashAsync(job, processed => statusStore.ReportProgress(job.JobId, processed), stoppingToken),
                    ArchiveMutationKind.BatchMove or ArchiveMutationKind.BatchMoveToTrash =>
                        await executor.BatchMoveAsync(job, processed => statusStore.ReportProgress(job.JobId, processed), stoppingToken),
                    _ => ArchiveMutationResult.Failed("Unsupported archive mutation kind.")
                };

                if (result.Outcome == ArchiveMutationOutcome.Success)
                {
                    await folderSync.AfterMutationAsync(job, stoppingToken);
                    statusStore.MarkCompleted(job.JobId);
                    logger.LogInformation("Archive mutation job {JobId} completed.", job.JobId);
                }
                else
                {
                    statusStore.MarkFailed(job.JobId, result.Diagnostic);
                    logger.LogWarning("Archive mutation job {JobId} failed: {Diagnostic}", job.JobId, result.Diagnostic);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                statusStore.MarkFailed(job.JobId, "An unexpected error interrupted the archive operation.");
                logger.LogError(exception, "Archive mutation job {JobId} threw.", job.JobId);
            }
            finally
            {
                queue.Complete();
            }
        }
    }
}
