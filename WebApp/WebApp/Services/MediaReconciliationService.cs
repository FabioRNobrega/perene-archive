using Microsoft.EntityFrameworkCore;
using WebApp.Authorization;
using WebApp.Client.Models;
using WebApp.Data;
using WebApp.Data.Entities;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>A file on disk described only by server-side facts.</summary>
internal sealed record MediaFile(string RootKey, string RelativePath, string PhysicalPath, long Size, DateTime LastWriteTimeUtc, string Category)
{
    public static string CategoryOf(ArchiveItemEntry entry) =>
        entry.IsBook ? MediaIdentityClassifier.BookCategory : entry.IsComic ? MediaIdentityClassifier.ComicCategory : MediaIdentityClassifier.OtherCategory;
}

internal sealed record MediaReconcileReport(int Relinked, int Reactivated, int SentToReview, int MarkedMissing);

/// <summary>
/// Owns media identity decisions: it finds or creates the Active <see cref="MediaItem"/> for a file, classifies same-path changes
/// (confirmed update, identity-changing replacement, uncertain), and reconciles a scan by fingerprint so moved or renamed files keep
/// their user data. User data is never deleted here; only statuses change.
/// </summary>
internal sealed class MediaReconciliationService(AppDbContext db, MediaItemRepository repository, FolderLocator locator)
{
    private readonly Dictionary<string, long> _rootIds = new(StringComparer.Ordinal);

    public MediaFile? Describe(ArchiveItemEntry entry) =>
        entry.Kind != ArchiveItemKind.File ? null : DescribeFile(entry.PhysicalPath, MediaFile.CategoryOf(entry));

    /// <summary>Describes any file under a known root (library, archive, cut or composition output) by its server-side facts.</summary>
    public MediaFile? DescribeFile(string physicalPath, string category)
    {
        var container = locator.LocateContainer(physicalPath);
        if (container is null) return null;
        var info = new FileInfo(physicalPath);
        if (!info.Exists) return null;
        var file = container.Child(info.Name);
        return new MediaFile(file.RootKey, file.RelativePath, info.FullName, info.Length, info.LastWriteTimeUtc, category);
    }

    /// <summary>The Active item for a file, created on first sight and reclassified when its bytes changed.</summary>
    public async Task<MediaItem?> EnsureAsync(MediaFile file, CancellationToken cancellationToken = default)
    {
        var folderId = await RootIdAsync(file.RootKey, cancellationToken);
        if (folderId is null) return null;

        var active = await repository.GetActiveAsync(folderId.Value, file.RelativePath, cancellationToken);
        if (active is not null) return await ApplyChangeAsync(active, file, cancellationToken);

        var missing = await repository.GetLatestMissingAtPathAsync(folderId.Value, file.RelativePath, cancellationToken);
        if (missing is not null) return await ReappearedAsync(missing, folderId.Value, file, cancellationToken);

        var fingerprint = MediaFingerprint.Compute(file.PhysicalPath);
        if (fingerprint is not null)
        {
            var moved = await repository.GetMissingByFingerprintAsync(fingerprint, cancellationToken);
            if (moved.Count == 1 && moved[0].Category == file.Category &&
                await repository.RelinkAsync(moved[0], folderId.Value, file.RelativePath, file.Size, file.LastWriteTimeUtc, fingerprint, cancellationToken))
                return moved[0];
        }

        return await repository.AddActiveAsync(NewItem(folderId.Value, file, fingerprint), cancellationToken);
    }

    private async Task<MediaItem> ApplyChangeAsync(MediaItem active, MediaFile file, CancellationToken cancellationToken)
    {
        if (active.Size == file.Size && active.LastWriteTimeUtc == file.LastWriteTimeUtc) return active;

        var newKey = IdentityKeyFor(file);
        var change = MediaIdentityClassifier.Classify(file.Category, active.IdentityKey, newKey, active.Size, active.LastWriteTimeUtc, file.Size, file.LastWriteTimeUtc);
        var fingerprint = MediaFingerprint.Compute(file.PhysicalPath);
        switch (change)
        {
            case SamePathChange.Unchanged:
                return active;
            case SamePathChange.SameItem:
                await repository.RefreshAsync(active, file.Size, file.LastWriteTimeUtc, fingerprint, cancellationToken);
                return active;
            case SamePathChange.ConfirmedUpdate:
                await repository.ConfirmUpdateAsync(active, file.Size, file.LastWriteTimeUtc, fingerprint, cancellationToken);
                return active;
            case SamePathChange.Replacement:
                return await repository.ReplaceAsync(active, MediaStatus.Superseded, NewItem(active.FolderId, file, fingerprint, newKey), cancellationToken);
            default:
                return await repository.ReplaceAsync(active, MediaStatus.NeedsReview, NewItem(active.FolderId, file, fingerprint, newKey), cancellationToken);
        }
    }

    private async Task<MediaItem> ReappearedAsync(MediaItem missing, long folderId, MediaFile file, CancellationToken cancellationToken)
    {
        var newKey = IdentityKeyFor(file);
        var fingerprint = MediaFingerprint.Compute(file.PhysicalPath);
        var sameBytes = fingerprint is not null && fingerprint == missing.ContentFingerprint;
        var change = sameBytes
            ? SamePathChange.Unchanged
            : MediaIdentityClassifier.Classify(file.Category, missing.IdentityKey, newKey, missing.Size, missing.LastWriteTimeUtc, file.Size, file.LastWriteTimeUtc);

        switch (change)
        {
            case SamePathChange.Unchanged or SamePathChange.SameItem or SamePathChange.ConfirmedUpdate:
                if (await repository.ReactivateAsync(missing, file.Size, file.LastWriteTimeUtc, fingerprint, change == SamePathChange.ConfirmedUpdate, cancellationToken))
                    return missing;
                return (await repository.GetActiveAsync(folderId, file.RelativePath, cancellationToken))
                    ?? await repository.AddActiveAsync(NewItem(folderId, file, fingerprint, newKey), cancellationToken);
            case SamePathChange.Replacement:
                return await repository.RetireMissingAndAddAsync(missing, MediaStatus.Superseded, NewItem(folderId, file, fingerprint, newKey), cancellationToken);
            default:
                return await repository.RetireMissingAndAddAsync(missing, MediaStatus.NeedsReview, NewItem(folderId, file, fingerprint, newKey), cancellationToken);
        }
    }

    /// <summary>
    /// Reconciles the archive on disk against known identities: moved or renamed files relink by fingerprint (so user data follows),
    /// ambiguous duplicates go to review, files that vanished become Missing, and reappearing Missing files are reclassified. User data
    /// is retained in every case.
    /// </summary>
    public async Task<MediaReconcileReport> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var rootKeys = await db.Folders.AsNoTracking().Where(folder => folder.RelativePath == "").ToDictionaryAsync(folder => folder.Id, folder => folder.RootKey, cancellationToken);
        var disk = EnumerateArchiveFiles();
        var active = await repository.GetActiveAsync(cancellationToken);
        var missing = await repository.GetByStatusAsync(MediaStatus.Missing, cancellationToken);

        string KeyOf(MediaItem item) => $"{rootKeys.GetValueOrDefault(item.FolderId, string.Empty)}\n{item.RelativePath}";
        var activeKeys = active.Select(KeyOf).ToHashSet(StringComparer.Ordinal);
        var vanished = active.Where(item => !disk.ContainsKey(KeyOf(item))).ToList();
        var pool = vanished.Concat(missing.Where(item => !disk.ContainsKey(KeyOf(item)))).ToList();
        var settled = new HashSet<long>();
        int relinked = 0, review = 0, reactivated = 0;

        var sizes = pool.Select(item => item.Size).ToHashSet();
        foreach (var (key, file) in disk.Where(pair => !activeKeys.Contains(pair.Key) && sizes.Contains(pair.Value.Size)).ToList())
        {
            var fingerprint = MediaFingerprint.Compute(file.PhysicalPath);
            if (fingerprint is null) continue;
            var matches = pool.Where(item => !settled.Contains(item.Id) && item.ContentFingerprint == fingerprint && item.Category == file.Category).ToList();
            if (matches.Count == 1)
            {
                var folderId = await RootIdAsync(file.RootKey, cancellationToken);
                if (folderId is not null && await repository.RelinkAsync(matches[0], folderId.Value, file.RelativePath, file.Size, file.LastWriteTimeUtc, fingerprint, cancellationToken))
                {
                    settled.Add(matches[0].Id);
                    activeKeys.Add(key);
                    relinked++;
                }
            }
            else if (matches.Count > 1)
            {
                foreach (var match in matches)
                {
                    settled.Add(match.Id);
                    if (match.Status == MediaStatus.Active) await repository.MarkMissingAsync(match, DateTimeOffset.UtcNow, cancellationToken);
                    await repository.MarkNeedsReviewAsync(match, cancellationToken);
                    review++;
                }
            }
        }

        var markedMissing = 0;
        foreach (var item in vanished.Where(item => !settled.Contains(item.Id)))
        {
            await repository.MarkMissingAsync(item, DateTimeOffset.UtcNow, cancellationToken);
            markedMissing++;
        }

        foreach (var item in missing.Where(item => !settled.Contains(item.Id) && disk.ContainsKey(KeyOf(item))))
        {
            var file = disk[KeyOf(item)];
            var folderId = await RootIdAsync(file.RootKey, cancellationToken);
            if (folderId is null || await repository.GetActiveAsync(folderId.Value, file.RelativePath, cancellationToken) is not null) continue;
            var result = await ReappearedAsync(item, folderId.Value, file, cancellationToken);
            if (result.Id == item.Id) reactivated++;
            else review++;
        }

        return new MediaReconcileReport(relinked, reactivated, review, markedMissing);
    }

    private Dictionary<string, MediaFile> EnumerateArchiveFiles()
    {
        var files = new Dictionary<string, MediaFile>(StringComparer.Ordinal);
        foreach (var root in locator.Roots.Where(root => root.IsArchiveCategory && Directory.Exists(root.Path)))
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var path in Directory.EnumerateFiles(root.Path, "*", options))
            {
                var info = new FileInfo(path);
                var relative = FolderLocation.Normalize(Path.GetRelativePath(root.Path, path));
                files[$"{root.Key}\n{relative}"] = new MediaFile(root.Key, relative, path, info.Length, info.LastWriteTimeUtc, CategoryFromExtension(info.Extension));
            }
        }

        return files;
    }

    private static string CategoryFromExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".epub" => MediaIdentityClassifier.BookCategory,
        ".cbz" => MediaIdentityClassifier.ComicCategory,
        _ => MediaIdentityClassifier.OtherCategory
    };

    private static string? IdentityKeyFor(MediaFile file) => file.Category switch
    {
        MediaIdentityClassifier.BookCategory => MediaIdentityKeys.ForEpub(file.PhysicalPath),
        MediaIdentityClassifier.ComicCategory => MediaIdentityKeys.ForCbz(file.PhysicalPath),
        _ => null
    };

    private static MediaItem NewItem(long folderId, MediaFile file, string? fingerprint, string? identityKey = null) => new()
    {
        FolderId = folderId,
        Category = file.Category,
        RelativePath = file.RelativePath,
        Size = file.Size,
        LastWriteTimeUtc = file.LastWriteTimeUtc,
        ContentFingerprint = fingerprint,
        IdentityKey = identityKey ?? IdentityKeyFor(file),
        CreatedUtc = DateTimeOffset.UtcNow
    };

    private async Task<long?> RootIdAsync(string rootKey, CancellationToken cancellationToken)
    {
        if (_rootIds.TryGetValue(rootKey, out var cached)) return cached;
        var id = await db.Folders.AsNoTracking().Where(folder => folder.RootKey == rootKey && folder.RelativePath == "")
            .Select(folder => (long?)folder.Id).FirstOrDefaultAsync(cancellationToken);
        if (id is not null) _rootIds[rootKey] = id.Value;
        return id;
    }
}
