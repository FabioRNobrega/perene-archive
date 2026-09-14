using Microsoft.Extensions.Options;
using WebApp.Configuration;

namespace WebApp.Services;

/// <summary>
/// Periodically removes archive upload sessions whose <c>LastActivityAt</c> is older than the
/// configured TTL. Deletion is coordinated through <see cref="IArchiveUploadService"/>'s own
/// per-session locking so this worker never races an in-flight chunk write or completion.
/// </summary>
internal sealed class ArchiveUploadCleanupWorker(
    IArchiveUploadService uploadService,
    IOptions<ArchiveUploadOptions> options,
    ILogger<ArchiveUploadCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.CleanupIntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                await uploadService.CleanupExpiredSessionsAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Archive upload cleanup pass failed.");
            }
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
