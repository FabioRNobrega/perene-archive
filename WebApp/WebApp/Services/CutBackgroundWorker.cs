using WebApp.Authorization;
using WebApp.Models;

namespace WebApp.Services;

internal sealed class CutBackgroundWorker(
    ICutJobQueue queue,
    ICutGenerator generator,
    IVideoCutService cuts,
    IFolderJobAuthorizer jobAuthorizer,
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

            logger.LogInformation("Cut generation started for media {MediaId} (job {JobId}).", job.SourceEntry.Id, job.JobId);

            try
            {
                if (!await jobAuthorizer.CanRunAsync(job, stoppingToken))
                {
                    logger.LogWarning("Cut generation skipped for media {MediaId} (job {JobId}): the requester no longer has access.", job.SourceEntry.Id, job.JobId);
                    continue;
                }

                var result = await generator.GenerateAsync(job, stoppingToken);
                switch (result.Status)
                {
                    case CutGenerationStatus.Success:
                        logger.LogInformation("Cut generation succeeded for media {MediaId} (job {JobId}).", job.SourceEntry.Id, job.JobId);
                        await cuts.ScanAsync(stoppingToken);
                        break;
                    case CutGenerationStatus.Cancelled:
                        logger.LogInformation("Cut generation cancelled for media {MediaId} (job {JobId}).", job.SourceEntry.Id, job.JobId);
                        break;
                    default:
                        logger.LogWarning(
                            "Cut generation failed for media {MediaId} (job {JobId}): {Diagnostic}",
                            job.SourceEntry.Id, job.JobId, result.Diagnostic);
                        break;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Cut generation threw for media {MediaId} (job {JobId}).", job.SourceEntry.Id, job.JobId);
            }
            finally
            {
                queue.Complete();
            }
        }
    }
}
