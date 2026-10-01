using Microsoft.EntityFrameworkCore;
using WebApp.Client.Models;
using WebApp.Data;
using WebApp.Data.Entities;

namespace WebApp.Services;

internal sealed class SqliteEpubHighlightService(AppDbContext db, IUserMediaContext context) : IEpubHighlightService
{
    public async Task<IReadOnlyList<BookHighlightDto>> LoadHighlightsAsync(string categoryKey, string itemId, CancellationToken cancellationToken)
    {
        var media = await context.RequireAsync(categoryKey, itemId, cancellationToken);
        var rows = await db.BookHighlights.AsNoTracking()
            .Where(highlight => highlight.UserId == media.UserId && highlight.MediaItemId == media.Item.Id)
            .ToListAsync(cancellationToken);
        // SQLite cannot order by DateTimeOffset, so order after loading.
        return rows.OrderBy(highlight => highlight.CreatedUtc).ThenBy(highlight => highlight.Id).Select(row => new BookHighlightDto(
            row.HighlightKey, row.ChapterId, row.TextOffsetStart, row.TextOffsetEnd, row.SelectedText, row.ContextBefore, row.ContextAfter, row.CreatedUtc)).ToList();
    }

    public async Task SaveHighlightAsync(string categoryKey, string itemId, BookHighlightDto highlight, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(highlight);
        var media = await context.RequireAsync(categoryKey, itemId, cancellationToken);
        db.BookHighlights.Add(new BookHighlight
        {
            HighlightKey = highlight.Id,
            UserId = media.UserId,
            MediaItemId = media.Item.Id,
            ChapterId = highlight.ChapterId,
            TextOffsetStart = highlight.TextOffsetStart,
            TextOffsetEnd = highlight.TextOffsetEnd,
            SelectedText = highlight.SelectedText,
            ContextBefore = highlight.ContextBefore,
            ContextAfter = highlight.ContextAfter,
            ContentRevision = media.Item.ContentRevision,
            CreatedUtc = highlight.SavedAt
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveHighlightAsync(string categoryKey, string itemId, string highlightId, CancellationToken cancellationToken)
    {
        var media = await context.RequireAsync(categoryKey, itemId, cancellationToken);
        return await db.BookHighlights
            .Where(highlight => highlight.UserId == media.UserId && highlight.MediaItemId == media.Item.Id && highlight.HighlightKey == highlightId)
            .ExecuteDeleteAsync(cancellationToken) > 0;
    }
}
