using WebApp.Client.Models;

namespace WebApp.Services;

internal interface IComicProgressService
{
    Task<ComicProgressDto?> LoadProgressAsync(
        string categoryKey, string itemId, long? sizeBytes, DateTime lastWriteTimeUtc, CancellationToken cancellationToken);

    Task SaveProgressAsync(
        string categoryKey, string itemId, long? sizeBytes, DateTime lastWriteTimeUtc, ComicProgressDto progress, CancellationToken cancellationToken);
}
