using WebApp.Client.Models;

namespace WebApp.Services;

internal interface IComicProgressService
{
    Task<ComicProgressDto?> LoadProgressAsync(string categoryKey, string itemId, CancellationToken cancellationToken);

    Task SaveProgressAsync(string categoryKey, string itemId, ComicProgressDto progress, CancellationToken cancellationToken);
}
