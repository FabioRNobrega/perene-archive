using Microsoft.EntityFrameworkCore;
using WebApp.Client.Models;
using WebApp.Data;
using WebApp.Data.Entities;

namespace WebApp.Services;

internal sealed class SqliteComicProgressService(AppDbContext db, IUserMediaContext context) : IComicProgressService
{
    public async Task<ComicProgressDto?> LoadProgressAsync(string categoryKey, string itemId, CancellationToken cancellationToken)
    {
        var media = await context.RequireAsync(categoryKey, itemId, cancellationToken);
        var row = await db.ComicProgresses.AsNoTracking()
            .FirstOrDefaultAsync(progress => progress.UserId == media.UserId && progress.MediaItemId == media.Item.Id, cancellationToken);
        return row is null ? null : new ComicProgressDto(Math.Max(0, row.PageIndex));
    }

    public async Task SaveProgressAsync(string categoryKey, string itemId, ComicProgressDto progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var media = await context.RequireAsync(categoryKey, itemId, cancellationToken);
        var row = await db.ComicProgresses.FirstOrDefaultAsync(
            existing => existing.UserId == media.UserId && existing.MediaItemId == media.Item.Id, cancellationToken);
        if (row is null)
        {
            row = new ComicProgress { UserId = media.UserId, MediaItemId = media.Item.Id };
            db.ComicProgresses.Add(row);
        }

        row.PageIndex = Math.Max(0, progress.PageIndex);
        row.UpdatedUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
