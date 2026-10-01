using WebApp.Client.Models;

namespace WebApp.Services;

internal interface IEpubProgressService
{
    /// <summary>The signed-in user's saved chapter/position for a book, or null. Progress made against an older content revision is reset to the chapter start.</summary>
    Task<BookProgressDto?> LoadProgressAsync(string categoryKey, string itemId, CancellationToken cancellationToken);

    Task SaveProgressAsync(string categoryKey, string itemId, BookProgressDto progress, CancellationToken cancellationToken);
}
