using Microsoft.EntityFrameworkCore;
using WebApp.Authorization;
using WebApp.Data;
using WebApp.Data.Entities;

namespace WebApp.Services;

internal sealed class SqliteArchiveFavoritesService(
    AppDbContext db, IUserMediaContext context, IArchiveService archive, FolderLocator locator) : IArchiveFavoritesService
{
    public async Task<IReadOnlySet<string>> GetFavoriteIdsAsync(string category, IEnumerable<string> currentIds, CancellationToken cancellationToken)
    {
        var userId = context.UserId;
        // Only Active items present favorites; the listing's IDs map to (root, path) without reading any file.
        var favorites = await db.Favorites.AsNoTracking()
            .Where(favorite => favorite.UserId == userId && favorite.MediaItem!.Status == MediaStatus.Active)
            .Select(favorite => new { favorite.MediaItem!.FolderId, favorite.MediaItem.RelativePath })
            .ToListAsync(cancellationToken);
        if (favorites.Count == 0) return new HashSet<string>(StringComparer.Ordinal);

        var rootKeys = await db.Folders.AsNoTracking().Where(folder => folder.RelativePath == "").ToDictionaryAsync(folder => folder.Id, folder => folder.RootKey, cancellationToken);
        var keys = favorites.Select(favorite => $"{rootKeys.GetValueOrDefault(favorite.FolderId)}\n{favorite.RelativePath}").ToHashSet(StringComparer.Ordinal);

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in currentIds.Distinct(StringComparer.Ordinal))
        {
            if (!archive.TryResolveItem(category, id, out var entry) || entry is null) continue;
            var container = locator.LocateContainer(entry.PhysicalPath);
            if (container is null) continue;
            var file = container.Child(Path.GetFileName(entry.PhysicalPath));
            if (keys.Contains($"{file.RootKey}\n{file.RelativePath}")) result.Add(id);
        }

        return result;
    }

    public async Task<bool> ToggleAsync(string category, string itemId, CancellationToken cancellationToken)
    {
        var media = await context.RequireAsync(category, itemId, cancellationToken);
        var existing = await db.Favorites.FirstOrDefaultAsync(
            favorite => favorite.UserId == media.UserId && favorite.MediaItemId == media.Item.Id, cancellationToken);
        if (existing is not null)
        {
            db.Favorites.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }

        db.Favorites.Add(new Favorite { UserId = media.UserId, MediaItemId = media.Item.Id, CreatedUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
