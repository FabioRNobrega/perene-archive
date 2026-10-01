using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebApp.Client.Models;
using WebApp.Configuration;
using WebApp.Data;
using WebApp.Data.Entities;

namespace WebApp.Services;

/// <summary>Custom storage views owned by the signed-in user, plus global views (null owner). Only rows of the current user can be changed.</summary>
internal sealed class SqliteCustomStorageViewService(
    AppDbContext db, ICurrentUser currentUser, IArchiveService archiveService, IOptions<ArchiveRootOptions> options) : ICustomStorageViewService
{
    private const string WholeArchiveTitle = "Archive";
    private readonly string _archiveRootPath = Path.GetFullPath(options.Value.Path);

    public async Task<IReadOnlyList<CustomStorageViewDto>> GetAllAsync(CancellationToken cancellationToken) =>
        await ListAsync(currentUser.RequireUserId(), cancellationToken);

    public async Task<IReadOnlyList<CustomStorageViewDto>> AddAsync(
        string? categoryKey, string? folderId, bool isWholeArchive, long maxSizeBytes, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        string title;
        if (isWholeArchive)
        {
            title = WholeArchiveTitle;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(categoryKey)) throw new ArchiveValidationException("A category is required.");
            var listing = archiveService.List(categoryKey, folderId);
            title = string.IsNullOrWhiteSpace(folderId) ? listing.Category.DisplayName : listing.CurrentFolder.Name;
        }

        db.CustomStorageViews.Add(new CustomStorageView
        {
            PublicId = Guid.NewGuid().ToString("N"),
            UserId = userId,
            CategoryKey = isWholeArchive ? null : categoryKey,
            FolderId = isWholeArchive ? null : folderId,
            IsWholeArchive = isWholeArchive,
            Title = title,
            MaxSizeBytes = maxSizeBytes,
            CreatedUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        return await ListAsync(userId, cancellationToken);
    }

    public async Task<IReadOnlyList<CustomStorageViewDto>?> RemoveAsync(string viewId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var row = await db.CustomStorageViews.FirstOrDefaultAsync(view => view.PublicId == viewId && view.UserId == userId, cancellationToken);
        if (row is null) return null;
        db.CustomStorageViews.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return await ListAsync(userId, cancellationToken);
    }

    public async Task<IReadOnlyList<CustomStorageViewDto>?> UpdateMaxSizeAsync(string viewId, long maxSizeBytes, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var row = await db.CustomStorageViews.FirstOrDefaultAsync(view => view.PublicId == viewId && view.UserId == userId, cancellationToken);
        if (row is null) return null;
        row.MaxSizeBytes = maxSizeBytes;
        await db.SaveChangesAsync(cancellationToken);
        return await ListAsync(userId, cancellationToken);
    }

    private async Task<IReadOnlyList<CustomStorageViewDto>> ListAsync(string userId, CancellationToken cancellationToken)
    {
        var rows = await db.CustomStorageViews.AsNoTracking().Where(view => view.UserId == null || view.UserId == userId).ToListAsync(cancellationToken);
        // SQLite cannot order by DateTimeOffset, so order after loading.
        return rows.OrderBy(view => view.CreatedUtc).ThenBy(view => view.Id).Select(Resolve).ToList();
    }

    private CustomStorageViewDto Resolve(CustomStorageView row)
    {
        try
        {
            var physicalPath = row.IsWholeArchive
                ? _archiveRootPath
                : archiveService.List(row.CategoryKey!, row.FolderId).CurrentFolder.PhysicalPath;
            return new CustomStorageViewDto(row.PublicId, row.Title, true, ComputeRecursiveSize(physicalPath), row.MaxSizeBytes);
        }
        catch (ArchiveException)
        {
            return new CustomStorageViewDto(row.PublicId, row.Title, false, 0, row.MaxSizeBytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CustomStorageViewDto(row.PublicId, row.Title, false, 0, row.MaxSizeBytes);
        }
    }

    private static long ComputeRecursiveSize(string folderPath)
    {
        long total = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(folderPath, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = true,
            }))
            {
                try { total += new FileInfo(path).Length; }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException) { }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }

        return total;
    }
}
