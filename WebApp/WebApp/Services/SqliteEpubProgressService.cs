using Microsoft.EntityFrameworkCore;
using WebApp.Client.Models;
using WebApp.Data;
using WebApp.Data.Entities;

namespace WebApp.Services;

internal sealed class SqliteEpubProgressService(AppDbContext db, IUserMediaContext context) : IEpubProgressService
{
    public async Task<BookProgressDto?> LoadProgressAsync(string categoryKey, string itemId, CancellationToken cancellationToken)
    {
        var media = await context.RequireAsync(categoryKey, itemId, cancellationToken);
        var row = await db.ReadingProgresses.AsNoTracking()
            .FirstOrDefaultAsync(progress => progress.UserId == media.UserId && progress.MediaItemId == media.Item.Id, cancellationToken);
        if (row is null) return null;
        // Offsets from an older revision of the content no longer line up: keep the chapter, restart it.
        return new BookProgressDto(row.ChapterId, row.ContentRevision < media.Item.ContentRevision ? 0 : Math.Max(0, row.WordOffset));
    }

    public async Task SaveProgressAsync(string categoryKey, string itemId, BookProgressDto progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var media = await context.RequireAsync(categoryKey, itemId, cancellationToken);
        var row = await db.ReadingProgresses.FirstOrDefaultAsync(
            existing => existing.UserId == media.UserId && existing.MediaItemId == media.Item.Id, cancellationToken);
        if (row is null)
        {
            row = new ReadingProgress { UserId = media.UserId, MediaItemId = media.Item.Id, ChapterId = progress.ChapterId };
            db.ReadingProgresses.Add(row);
        }

        row.ChapterId = progress.ChapterId;
        row.WordOffset = Math.Max(0, progress.WordOffset);
        row.ContentRevision = media.Item.ContentRevision;
        row.UpdatedUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
