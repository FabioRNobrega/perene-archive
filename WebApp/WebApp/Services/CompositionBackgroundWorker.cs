using WebApp.Authorization;
using WebApp.Models;

namespace WebApp.Services;

internal sealed class CompositionBackgroundWorker(
    ICompositionJobQueue queue,
    ICompositionGenerator generator,
    IVideoCompositionService compositions,
    ICompositionJobStatusStore statusStore,
    IFolderJobAuthorizer jobAuthorizer,
    ILogger<CompositionBackgroundWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            CompositionJob job;
            try
            {
                job = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            statusStore.MarkProcessing(job.JobId);
            logger.LogInformation(
                "Composition generation started for job {JobId} with {ClipCount} clips.", job.JobId, job.OrderedSources.Count);

            try
            {
                if (!await jobAuthorizer.CanRunAsync(job, stoppingToken))
                {
                    statusStore.MarkFailed(job.JobId, "You no longer have access to complete this composition.");
                    logger.LogWarning("Composition generation skipped for job {JobId}: the requester no longer has access.", job.JobId);
                    continue;
                }

                var result = await generator.GenerateAsync(job, stoppingToken);
                switch (result.Status)
                {
                    case CompositionGenerationStatus.Success:
                        var entries = await compositions.ScanAsync(stoppingToken);
                        var resultEntry = entries.FirstOrDefault(entry => entry.PhysicalPath == result.DestinationPath);
                        statusStore.MarkCompleted(job.JobId, resultEntry?.Id ?? string.Empty);
                        logger.LogInformation("Composition generation succeeded for job {JobId}.", job.JobId);
                        break;
                    case CompositionGenerationStatus.Cancelled:
                        logger.LogInformation("Composition generation cancelled for job {JobId}.", job.JobId);
                        break;
                    default:
                        statusStore.MarkFailed(job.JobId, result.Diagnostic);
                        logger.LogWarning(
                            "Composition generation failed for job {JobId} at stage {Stage}: {Diagnostic}",
                            job.JobId, result.Stage, result.Diagnostic);
                        break;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                statusStore.MarkFailed(job.JobId, "unexpected error during composition generation");
                logger.LogError(exception, "Composition generation threw for job {JobId}.", job.JobId);
            }
            finally
            {
                queue.Complete();
            }
        }
    }
}
