using WebApp.Client.Models;

namespace WebApp.Services;

internal interface IEpubHighlightService
{
    Task<IReadOnlyList<BookHighlightDto>> LoadHighlightsAsync(
        string categoryKey, string itemId, long? sizeBytes, DateTime lastWriteTimeUtc, CancellationToken cancellationToken);

    Task SaveHighlightAsync(
        string categoryKey, string itemId, long? sizeBytes, DateTime lastWriteTimeUtc, BookHighlightDto highlight, CancellationToken cancellationToken);

    Task<bool> RemoveHighlightAsync(
        string categoryKey, string itemId, long? sizeBytes, DateTime lastWriteTimeUtc, string highlightId, CancellationToken cancellationToken);
}
