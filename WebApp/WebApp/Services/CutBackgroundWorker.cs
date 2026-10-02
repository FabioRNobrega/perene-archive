using WebApp.Authorization;
using WebApp.Models;

namespace WebApp.Services;

internal sealed class CutBackgroundWorker(
    ICutJobQueue queue,
    ICutGenerator generator,
    IVideoCutService cuts,
    IFolderJobAuthorizer jobAuthorizer,
    ICutJobRecorder recorder,
    ILogger<CutBackgroundWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            CutJob job;
            try
            {
                job = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            recorder.MarkProcessing(job.JobId);
            logger.LogInformation("Cut generation started for media {MediaId} (job {JobId}).", job.SourceEntry.Id, job.JobId);

            try
            {
                if (!await jobAuthorizer.CanRunAsync(job, stoppingToken))
                {
                    recorder.MarkFailed(job.JobId, "You no longer have access to complete this cut.");
                    logger.LogWarning("Cut generation skipped for media {MediaId} (job {JobId}): the requester no longer has access.", job.SourceEntry.Id, job.JobId);
                    continue;
                }

                var result = await generator.GenerateAsync(job, stoppingToken);
                switch (result.Status)
                {
                    case CutGenerationStatus.Success:
                        logger.LogInformation("Cut generation succeeded for media {MediaId} (job {JobId}).", job.SourceEntry.Id, job.JobId);
                        recorder.MarkCompleted(job.JobId);
                        await cuts.ScanAsync(stoppingToken);
                        break;
                    case CutGenerationStatus.Cancelled:
                        recorder.MarkFailed(job.JobId, "The cut was cancelled.");
                        logger.LogInformation("Cut generation cancelled for media {MediaId} (job {JobId}).", job.SourceEntry.Id, job.JobId);
                        break;
                    default:
                        recorder.MarkFailed(job.JobId, result.Diagnostic ?? "The cut could not be created.");
                        logger.LogWarning(
                            "Cut generation failed for media {MediaId} (job {JobId}): {Diagnostic}",
                            job.SourceEntry.Id, job.JobId, result.Diagnostic);
                        break;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                recorder.MarkFailed(job.JobId, "Unexpected error during cut generation.");
                logger.LogError(exception, "Cut generation threw for media {MediaId} (job {JobId}).", job.SourceEntry.Id, job.JobId);
            }
            finally
            {
                queue.Complete();
            }
        }
    }
}
