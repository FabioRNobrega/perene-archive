using WebApp.Client.Models;

namespace WebApp.Services;

internal interface IEpubHighlightService
{
    Task<IReadOnlyList<BookHighlightDto>> LoadHighlightsAsync(string categoryKey, string itemId, CancellationToken cancellationToken);

    Task SaveHighlightAsync(string categoryKey, string itemId, BookHighlightDto highlight, CancellationToken cancellationToken);

    Task<bool> RemoveHighlightAsync(string categoryKey, string itemId, string highlightId, CancellationToken cancellationToken);
}
